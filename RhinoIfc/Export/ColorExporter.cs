using System.Drawing;
using Rhino;
using Rhino.DocObjects;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.PresentationAppearanceResource;

namespace RhinoIfc.Export
{
    /// <summary>
    /// Exports Rhino object/layer/material colors as IFC surface styles.
    /// Creates: IfcSurfaceStyleRendering → IfcSurfaceStyle → IfcStyledItem → representation item.
    /// </summary>
    public static class ColorExporter
    {
        /// <summary>
        /// Resolve the effective display color for a Rhino object and apply it
        /// as an IFC surface style on the representation's first item.
        /// </summary>
        public static void ApplyColor(IfcStore model, RhinoDoc doc, RhinoObject rhinoObj, IIfcRepresentation representation)
        {
            if (representation == null) return;

            Color? color = ResolveColor(doc, rhinoObj);
            if (!color.HasValue) return;

            var c = color.Value;

            // Find the first representation item
            IfcRepresentationItem repItem = null;
            foreach (var item in representation.Items)
            {
                repItem = item as IfcRepresentationItem;
                break;
            }
            if (repItem == null) return;

            // IfcColourRgb (0.0–1.0 channels)
            var rgb = model.Instances.New<IfcColourRgb>(col =>
            {
                col.Red = c.R / 255.0;
                col.Green = c.G / 255.0;
                col.Blue = c.B / 255.0;
            });

            // IfcSurfaceStyleRendering with optional transparency
            var rendering = model.Instances.New<IfcSurfaceStyleRendering>(r =>
            {
                r.SurfaceColour = rgb;
                if (c.A < 255)
                    r.Transparency = 1.0 - (c.A / 255.0);
            });

            // IfcSurfaceStyle
            var surfaceStyle = model.Instances.New<IfcSurfaceStyle>(ss =>
            {
                ss.Side = IfcSurfaceSide.BOTH;
                ss.Styles.Add(rendering);
            });

            // IfcStyledItem links the style to the geometry
            model.Instances.New<IfcStyledItem>(si =>
            {
                si.Item = repItem;
                si.Styles.Add(surfaceStyle);
            });
        }

        private static Color? ResolveColor(RhinoDoc doc, RhinoObject rhinoObj)
        {
            var source = rhinoObj.Attributes.ColorSource;

            if (source == ObjectColorSource.ColorFromObject)
                return rhinoObj.Attributes.ObjectColor;

            if (source == ObjectColorSource.ColorFromLayer)
            {
                int idx = rhinoObj.Attributes.LayerIndex;
                if (idx >= 0 && idx < doc.Layers.Count)
                    return doc.Layers[idx].Color;
            }

            if (source == ObjectColorSource.ColorFromMaterial)
            {
                int matIdx = rhinoObj.Attributes.MaterialIndex;
                if (matIdx >= 0 && matIdx < doc.Materials.Count)
                    return doc.Materials[matIdx].DiffuseColor;
            }

            // Default: use layer color as fallback
            {
                int idx = rhinoObj.Attributes.LayerIndex;
                if (idx >= 0 && idx < doc.Layers.Count)
                    return doc.Layers[idx].Color;
            }

            return null;
        }
    }
}
