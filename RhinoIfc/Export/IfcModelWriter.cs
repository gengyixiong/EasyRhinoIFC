using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Xbim.Common;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.ProductExtension;
using Xbim.Ifc4.RepresentationResource;
using Xbim.Ifc4.SharedBldgElements;
using Xbim.IO;
using Xbim.Ifc4.GeometricConstraintResource;
using RhinoIfc.Util;

namespace RhinoIfc.Export
{
    /// <summary>
    /// Exports Rhino objects to an IFC file.
    /// Creates the spatial skeleton from Rhino layer hierarchy:
    ///   - depth >= 3: top layer → IfcBuilding, second → IfcBuildingStorey, third+ → IFC class
    ///   - depth &lt; 3: single default building/storey, layer name → IFC class
    /// </summary>
    public class IfcModelWriter
    {
        public int Export(RhinoDoc doc, IEnumerable<RhinoObject> objects, string outputPath)
        {
            var editor = new XbimEditorCredentials
            {
                ApplicationDevelopersName = "RhinoIfc",
                ApplicationFullName = "RhinoIfc Plugin",
                ApplicationIdentifier = "RhinoIfc",
                ApplicationVersion = "0.1.0",
                EditorsFamilyName = System.Environment.UserName,
                EditorsGivenName = "",
                EditorsOrganisationName = ""
            };

            int count = 0;
            double unitScale = UnitConverter.RhinoToMetres(doc.ModelUnitSystem);

            using (var model = IfcStore.Create(editor, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel))
            {
                using (var txn = model.BeginTransaction("Init"))
                {
                    var project = model.Instances.New<IfcProject>(p =>
                    {
                        p.Name = doc.Name ?? "Rhino Export";
                        p.Initialize(ProjectUnits.SIUnitsUK);
                    });

                    var site = model.Instances.New<IfcSite>(s =>
                    {
                        s.Name = "Default Site";
                        s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                    });

                    CreateAggregation(model, project, site);

                    var geomContext = model.Instances.New<IfcGeometricRepresentationContext>(c =>
                    {
                        c.ContextType = "Model";
                        c.CoordinateSpaceDimension = 3;
                        c.Precision = 1e-5;
                        c.WorldCoordinateSystem = model.Instances.New<IfcAxis2Placement3D>(a =>
                        {
                            a.Location = model.Instances.New<IfcCartesianPoint>(p =>
                                p.SetXYZ(0, 0, 0));
                        });
                    });

                    txn.Commit();

                    // Caches for dynamic spatial structure
                    var buildingCache = new Dictionary<string, IfcBuilding>(StringComparer.OrdinalIgnoreCase);
                    var storeyCache = new Dictionary<string, IfcBuildingStorey>(StringComparer.OrdinalIgnoreCase);

                    using (var txn2 = model.BeginTransaction("Elements"))
                    {
                        // Create a default building+storey for shallow hierarchies
                        var defaultBuilding = model.Instances.New<IfcBuilding>(b =>
                        {
                            b.Name = "Default Building";
                            b.CompositionType = IfcElementCompositionEnum.ELEMENT;
                        });
                        CreateAggregation(model, site, defaultBuilding);

                        var defaultStorey = model.Instances.New<IfcBuildingStorey>(s =>
                        {
                            s.Name = "Default Storey";
                            s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                            s.Elevation = 0;
                        });
                        CreateAggregation(model, defaultBuilding, defaultStorey);

                        buildingCache[""] = defaultBuilding;
                        storeyCache[""] = defaultStorey;

                        int seq = 0;
                        foreach (var rhinoObj in objects)
                        {
                            var meshes = ExtractMeshes(rhinoObj);
                            if (meshes == null || meshes.Length == 0) continue;

                            string layerFullPath = doc.Layers[rhinoObj.Attributes.LayerIndex].FullPath;
                            var segments = layerFullPath
                                .Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim())
                                .ToArray();

                            // Resolve spatial container from layer depth
                            IfcBuildingStorey targetStorey;
                            string ifcClassName;

                            if (segments.Length >= 3)
                            {
                                // segments[0] → building, segments[1] → storey, segments[2+] → class
                                string bldgName = segments[0];
                                string storeyName = segments[1];
                                string storeyKey = $"{bldgName}::{storeyName}";

                                if (!buildingCache.TryGetValue(bldgName, out var bldg))
                                {
                                    bldg = model.Instances.New<IfcBuilding>(b =>
                                    {
                                        b.Name = bldgName;
                                        b.CompositionType = IfcElementCompositionEnum.ELEMENT;
                                    });
                                    CreateAggregation(model, site, bldg);
                                    buildingCache[bldgName] = bldg;
                                }

                                if (!storeyCache.TryGetValue(storeyKey, out targetStorey))
                                {
                                    targetStorey = model.Instances.New<IfcBuildingStorey>(s =>
                                    {
                                        s.Name = storeyName;
                                        s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                                        s.Elevation = 0;
                                    });
                                    CreateAggregation(model, bldg, targetStorey);
                                    storeyCache[storeyKey] = targetStorey;
                                }

                                // Use remaining segments for class mapping
                                string classPart = string.Join("::", segments.Skip(2));
                                ifcClassName = ClassMapper.MapLayerToIfcClass(classPart);
                            }
                            else
                            {
                                // Shallow hierarchy: use defaults
                                targetStorey = defaultStorey;
                                ifcClassName = ClassMapper.MapLayerToIfcClass(layerFullPath);
                            }

                            string elementName = rhinoObj.Name;
                            if (string.IsNullOrWhiteSpace(elementName))
                                elementName = rhinoObj.Attributes.Name;
                            if (string.IsNullOrWhiteSpace(elementName))
                            {
                                string leafLayer = doc.Layers[rhinoObj.Attributes.LayerIndex].Name;
                                elementName = $"{leafLayer} {++seq}";
                            }

                            var element = CreateElement(model, ifcClassName, elementName);

                            var representation = GeometryExporter.CreateRepresentation(
                                model, geomContext, meshes, unitScale);

                            if (representation != null)
                            {
                                ColorExporter.ApplyColor(model, doc, rhinoObj, representation);

                                element.Representation = model.Instances.New<IfcProductDefinitionShape>(pds =>
                                {
                                    pds.Representations.Add(representation);
                                });
                            }

                            element.ObjectPlacement = model.Instances.New<IfcLocalPlacement>(lp =>
                            {
                                lp.RelativePlacement = model.Instances.New<IfcAxis2Placement3D>(a =>
                                {
                                    a.Location = model.Instances.New<IfcCartesianPoint>(p =>
                                        p.SetXYZ(0, 0, 0));
                                });
                            });

                            AddToContainer(model, targetStorey, element);
                            PropertyExporter.ExportUserStrings(model, element, rhinoObj);

                            count++;
                        }

                        txn2.Commit();
                    }
                }

                model.SaveAs(outputPath, StorageType.Ifc);
            }

            return count;
        }

        private static Mesh[] ExtractMeshes(RhinoObject obj)
        {
            if (obj.Geometry is Point || obj.Geometry is TextDot ||
                obj.Geometry is AnnotationBase || obj.Geometry is Light)
                return null;

            if (obj.Geometry is Mesh m)
            {
                CleanMesh(m);
                return new[] { m };
            }

            if (obj.Geometry is Brep brep)
            {
                var meshes = Mesh.CreateFromBrep(brep, MeshingParameters.Default);
                if (meshes == null || meshes.Length == 0) return null;
                foreach (var mesh in meshes) CleanMesh(mesh);
                return meshes;
            }

            if (obj.Geometry is Extrusion ext)
            {
                var b = ext.ToBrep();
                if (b == null) return null;
                var meshes = Mesh.CreateFromBrep(b, MeshingParameters.Default);
                if (meshes == null || meshes.Length == 0) return null;
                foreach (var mesh in meshes) CleanMesh(mesh);
                return meshes;
            }

            if (obj.Geometry is SubD subd)
            {
                var b = subd.ToBrep(SubDToBrepOptions.Default);
                if (b == null) return null;
                var meshes = Mesh.CreateFromBrep(b, MeshingParameters.Default);
                if (meshes == null || meshes.Length == 0) return null;
                foreach (var mesh in meshes) CleanMesh(mesh);
                return meshes;
            }

            return null;
        }

        private static void CleanMesh(Mesh mesh)
        {
            mesh.Vertices.CombineIdentical(true, true);
            mesh.Faces.CullDegenerateFaces();
        }

        private static IfcProduct CreateElement(IfcStore model, string ifcClassName, string name)
        {
            IfcProduct element = ifcClassName switch
            {
                "IfcWall" => model.Instances.New<IfcWall>(),
                "IfcSlab" => model.Instances.New<IfcSlab>(),
                "IfcColumn" => model.Instances.New<IfcColumn>(),
                "IfcBeam" => model.Instances.New<IfcBeam>(),
                "IfcDoor" => model.Instances.New<IfcDoor>(),
                "IfcWindow" => model.Instances.New<IfcWindow>(),
                "IfcRoof" => model.Instances.New<IfcRoof>(),
                "IfcStair" => model.Instances.New<IfcStair>(),
                "IfcRailing" => model.Instances.New<IfcRailing>(),
                "IfcFurnishingElement" => model.Instances.New<IfcFurnishingElement>(),
                _ => model.Instances.New<IfcBuildingElementProxy>()
            };

            element.Name = name;
            return element;
        }

        private static void CreateAggregation(IfcStore model, IfcObjectDefinition parent, IfcObjectDefinition child)
        {
            model.Instances.New<IfcRelAggregates>(rel =>
            {
                rel.RelatingObject = parent;
                rel.RelatedObjects.Add(child);
            });
        }

        private static void AddToContainer(IfcStore model, IfcSpatialStructureElement container, IfcProduct product)
        {
            var existingRel = model.Instances.OfType<IfcRelContainedInSpatialStructure>()
                .FirstOrDefault(r => r.RelatingStructure == container);

            if (existingRel != null)
            {
                existingRel.RelatedElements.Add(product);
            }
            else
            {
                model.Instances.New<IfcRelContainedInSpatialStructure>(rel =>
                {
                    rel.RelatingStructure = container;
                    rel.RelatedElements.Add(product);
                });
            }
        }
    }
}
