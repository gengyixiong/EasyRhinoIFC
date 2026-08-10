using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Common.XbimExtensions;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;
using RhinoIfc.Import.Geometry;
using RhinoIfc.Util;

namespace RhinoIfc.Import
{
    /// <summary>
    /// Reads an IFC file, tessellates all geometry via xBIM, and adds
    /// the resulting meshes to the active Rhino document.
    /// </summary>
    public class IfcModelReader
    {
        /// <summary>
        /// Legacy single-file import. Returns element count for backward compatibility.
        /// </summary>
        public int Import(RhinoDoc doc, string filePath)
        {
            var options = new ImportOptions
            {
                Grouping = GroupingMode.Flat,
                ParentLayerName = null,
                FileIndex = 0
            };
            var result = Import(doc, filePath, options);
            return result.ElementCount;
        }

        /// <summary>
        /// Import an IFC file with configurable grouping options.
        /// </summary>
        public ImportResult Import(RhinoDoc doc, string filePath, ImportOptions options)
        {
            var sw = Stopwatch.StartNew();
            var result = new ImportResult
            {
                FileName = Path.GetFileName(filePath)
            };
            int errors = 0;

            using (var model = IfcStore.Open(filePath))
            {
                RhinoApp.WriteLine($"IFC schema: {model.SchemaVersion}");

                var context = new Xbim3DModelContext(model);
                context.CreateContext();

                double scaleFactor = UnitConverter.IfcModelToRhino(model, doc.ModelUnitSystem);

                string parentLayerName = options.Grouping == GroupingMode.ByFile
                    ? options.ParentLayerName
                    : null;
                var layerMapper = new LayerMapper(doc, model, parentLayerName, options.FileIndex);

                // ----- Parametric Brep pre-pass -----
                // Try to convert each product's Body representation to a real
                // Brep first. Anything that succeeds is recorded in
                // handledProductLabels and skipped by the mesh fallback below.
                var convCtx = new ConversionContext(model, scaleFactor, doc.ModelAbsoluteTolerance);
                var handledProductLabels = new HashSet<int>();

                foreach (var product in model.Instances.OfType<IIfcProduct>())
                {
                    if (product is IIfcSpatialStructureElement) continue;
                    if (product is IIfcSpace) continue;
                    if (product is IIfcZone) continue;
                    if (product is IIfcOpeningElement) continue; // consumed via host's HasOpenings

                    try
                    {
                        if (!IfcGeometryConverter.TryConvertProduct(product, convCtx, out var breps))
                            continue;

                        bool addedAny = false;
                        foreach (var brep in breps)
                        {
                            if (brep == null || !brep.IsValid) continue;

                            var attributes = new ObjectAttributes();
                            attributes.Name = product.Name?.ToString() ?? product.ExpressType.ExpressName;

                            int layerIndex = layerMapper.GetOrCreateLayer(product);
                            if (layerIndex >= 0)
                                attributes.LayerIndex = layerIndex;

                            var id = doc.Objects.AddBrep(brep, attributes);
                            if (id != Guid.Empty)
                            {
                                var obj = doc.Objects.FindId(id);
                                if (obj != null)
                                    MetadataMapper.AttachMetadata(obj, product);

                                result.ObjectIds.Add(id);
                                result.ElementCount++;
                                addedAny = true;
                            }
                        }

                        if (addedAny)
                            handledProductLabels.Add(product.EntityLabel);
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        string name = product.Name?.ToString() ?? $"#{product.EntityLabel}";
                        RhinoApp.WriteLine($"Warning: Brep conversion failed for '{name}': {ex.Message}");
                        // Fall through — the mesh pass below will handle this product.
                    }
                }

                var shapes = context.ShapeInstances().ToList();
                int total = shapes.Count;
                int processed = 0;

                var statusBar = Rhino.UI.StatusBar.ShowProgressMeter(0, total, "Importing IFC...", true, true);

                try
                {
                    foreach (var shapeInstance in shapes)
                    {
                        processed++;
                        if (processed % 100 == 0)
                            Rhino.UI.StatusBar.UpdateProgressMeter(processed, true);

                        if (shapeInstance.RepresentationType ==
                            XbimGeometryRepresentationType.OpeningsAndAdditionsExcluded)
                            continue;

                        // Skip products already handled by the parametric Brep pre-pass.
                        if (handledProductLabels.Contains(shapeInstance.IfcProductLabel))
                            continue;

                        var product = model.Instances[shapeInstance.IfcProductLabel] as IIfcProduct;
                        if (product == null) continue;

                        // Skip spatial elements (sites, buildings, storeys) and non-geometric types
                        if (product is IIfcSpatialStructureElement) continue;
                        if (product is IIfcSpace) continue;
                        if (product is IIfcZone) continue;

                        try
                        {
                            var shapeGeometry = context.ShapeGeometry(shapeInstance);

                            // Only process PolyhedronBinary format
                            if (shapeGeometry.Format != XbimGeometryType.PolyhedronBinary)
                                continue;

                            var shapeData = ((IXbimShapeGeometryData)shapeGeometry).ShapeData;
                            if (shapeData == null || shapeData.Length == 0) continue;

                            Mesh rhinoMesh = MeshConverter.Convert(shapeData, shapeInstance.Transformation, scaleFactor);
                            if (rhinoMesh == null || rhinoMesh.Vertices.Count == 0) continue;

                            var attributes = new ObjectAttributes();
                            attributes.Name = product.Name?.ToString() ?? product.ExpressType.ExpressName;

                            int layerIndex = layerMapper.GetOrCreateLayer(product);
                            if (layerIndex >= 0)
                                attributes.LayerIndex = layerIndex;

                            if (shapeInstance.HasStyle)
                            {
                                var color = StyleExtractor.GetColor(model, shapeInstance.StyleLabel);
                                if (color.HasValue)
                                {
                                    attributes.ObjectColor = color.Value;
                                    attributes.ColorSource = ObjectColorSource.ColorFromObject;
                                }
                            }

                            var id = doc.Objects.AddMesh(rhinoMesh, attributes);
                            if (id != Guid.Empty)
                            {
                                var obj = doc.Objects.FindId(id);
                                if (obj != null)
                                    MetadataMapper.AttachMetadata(obj, product);

                                result.ObjectIds.Add(id);
                                result.ElementCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            string name = product.Name?.ToString() ?? $"#{shapeInstance.IfcProductLabel}";
                            RhinoApp.WriteLine($"Warning: skipped element '{name}': {ex.Message}");
                        }
                    }
                }
                finally
                {
                    Rhino.UI.StatusBar.HideProgressMeter();
                }
            }

            if (errors > 0)
                RhinoApp.WriteLine($"Completed with {errors} element(s) skipped due to errors.");

            sw.Stop();
            result.Duration = sw.Elapsed;
            return result;
        }
    }
}
