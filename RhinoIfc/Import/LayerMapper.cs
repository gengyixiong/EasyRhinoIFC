using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Xbim.Common;
using Xbim.Ifc4.Interfaces;

namespace RhinoIfc.Import
{
    /// <summary>
    /// Maps IFC spatial hierarchy (Project → Site → Building → Storey)
    /// and IFC class names into nested Rhino layers.
    ///
    /// Result looks like:
    ///   Site Name :: Building A :: Ground Floor :: IfcWall
    ///   Site Name :: Building A :: Ground Floor :: IfcSlab
    ///   Site Name :: Building A :: First Floor :: IfcWall
    ///
    /// When parentLayerName is set (ByFile mode), all layers nest under a
    /// top-level layer named after the file:
    ///   Arch :: Site Name :: Building A :: Ground Floor :: Wall
    /// </summary>
    public class LayerMapper
    {
        private readonly RhinoDoc _doc;
        private readonly IModel _model;
        private readonly Dictionary<string, int> _layerCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly string _parentLayerName;
        private readonly int _fileIndex;

        private static readonly Color[] FilePalette =
        {
            Color.FromArgb(220, 80, 80),    // red
            Color.FromArgb(80, 120, 220),    // blue
            Color.FromArgb(80, 180, 80),     // green
            Color.FromArgb(220, 150, 50),    // orange
            Color.FromArgb(160, 80, 200),    // purple
            Color.FromArgb(50, 190, 190),    // cyan
            Color.FromArgb(200, 200, 60),    // yellow
            Color.FromArgb(200, 80, 180),    // magenta
        };

        public LayerMapper(RhinoDoc doc, IModel model, string parentLayerName = null, int fileIndex = 0)
        {
            _doc = doc;
            _model = model;
            _parentLayerName = parentLayerName;
            _fileIndex = fileIndex;
        }

        /// <summary>
        /// Get or create a nested layer path for the given IFC product.
        /// </summary>
        public int GetOrCreateLayer(IIfcProduct product)
        {
            var pathParts = BuildLayerPath(product);

            // If a parent layer name is set, prepend it
            if (!string.IsNullOrEmpty(_parentLayerName))
                pathParts.Insert(0, _parentLayerName);

            string fullPath = string.Join("::", pathParts);

            if (_layerCache.TryGetValue(fullPath, out int cached))
                return cached;

            // Create layers from root to leaf, nesting each under its parent
            int parentIndex = -1;
            string runningPath = "";
            bool isFirst = true;
            foreach (var part in pathParts)
            {
                runningPath = runningPath.Length == 0 ? part : runningPath + "::" + part;

                if (_layerCache.TryGetValue(runningPath, out int existingIdx))
                {
                    parentIndex = existingIdx;
                    isFirst = false;
                    continue;
                }

                // Check if layer already exists in the document
                int docIdx = _doc.Layers.FindByFullPath(runningPath, -1);
                if (docIdx >= 0)
                {
                    _layerCache[runningPath] = docIdx;
                    parentIndex = docIdx;
                    isFirst = false;
                    continue;
                }

                // Create new layer
                bool isParentLayer = isFirst && !string.IsNullOrEmpty(_parentLayerName);
                var layer = new Layer
                {
                    Name = part,
                    Color = isParentLayer
                        ? FilePalette[_fileIndex % FilePalette.Length]
                        : GetDefaultColor(part),
                };

                if (parentIndex >= 0)
                    layer.ParentLayerId = _doc.Layers[parentIndex].Id;

                int newIdx = _doc.Layers.Add(layer);
                if (newIdx >= 0)
                {
                    _layerCache[runningPath] = newIdx;
                    parentIndex = newIdx;
                }

                isFirst = false;
            }

            _layerCache[fullPath] = parentIndex;
            return parentIndex;
        }

        private List<string> BuildLayerPath(IIfcProduct product)
        {
            var parts = new List<string>();

            // Walk the spatial containment chain upward
            var container = product.IsContainedIn;
            if (container != null)
            {
                var current = container as IIfcObjectDefinition;
                var chain = new List<string>();

                while (current != null && !(current is IIfcProject))
                {
                    string name = Sanitize(current.Name?.ToString()) ??
                                  current.GetType().Name.Replace("Ifc", "");
                    chain.Insert(0, name);

                    // Move up via IfcRelAggregates
                    var parent = current.Decomposes
                        .OfType<IIfcRelAggregates>()
                        .FirstOrDefault()?.RelatingObject;
                    current = parent;
                }

                parts.AddRange(chain);
            }

            // Leaf = IFC class name without "Ifc" prefix
            string className = product.ExpressType.ExpressName;
            if (className.StartsWith("Ifc"))
                className = className.Substring(3);
            parts.Add(className);

            // Fallback if somehow empty
            if (parts.Count == 0)
                parts.Add("IFC");

            return parts;
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            // Rhino layer names can't contain ::
            return name.Replace("::", " - ").Trim();
        }

        /// <summary>
        /// Assign default colors by IFC class for visual clarity.
        /// </summary>
        private static Color GetDefaultColor(string layerName)
        {
            var lower = layerName.ToLowerInvariant();
            if (lower.Contains("wall")) return Color.FromArgb(180, 180, 180);
            if (lower.Contains("slab") || lower.Contains("floor")) return Color.FromArgb(200, 200, 190);
            if (lower.Contains("roof")) return Color.FromArgb(160, 80, 60);
            if (lower.Contains("door")) return Color.FromArgb(140, 100, 60);
            if (lower.Contains("window")) return Color.FromArgb(140, 200, 220);
            if (lower.Contains("column")) return Color.FromArgb(140, 140, 150);
            if (lower.Contains("beam")) return Color.FromArgb(150, 150, 160);
            if (lower.Contains("stair")) return Color.FromArgb(170, 160, 140);
            if (lower.Contains("railing")) return Color.FromArgb(100, 100, 100);
            if (lower.Contains("furniture") || lower.Contains("furnishing")) return Color.FromArgb(180, 150, 100);
            return Color.FromArgb(160, 160, 160);
        }
    }
}
