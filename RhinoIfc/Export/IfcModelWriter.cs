using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoIfc.Util;
using Xbim.Common;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc4.GeometricConstraintResource;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.ProductExtension;
using Xbim.Ifc4.RepresentationResource;
using Xbim.Ifc4.SharedBldgElements;
using Xbim.Ifc4.SharedComponentElements;
using Xbim.Ifc4.SharedFacilitiesElements;
using Xbim.Ifc4.StructuralElementsDomain;
using Xbim.IO;

namespace RhinoIfc.Export
{
    public class IfcModelWriter
    {
        public int Export(RhinoDoc doc, IEnumerable<RhinoObject> objects, string outputPath)
        {
            var exportObjects = objects?.Where(o => o != null).ToArray() ?? Array.Empty<RhinoObject>();
            var projectLayerName = exportObjects
                .SelectMany(o => GetLayerSegments(doc.Layers[o.Attributes.LayerIndex].FullPath))
                .FirstOrDefault(s => ClassMapper.MapLayerToIfcClass(s) == "IfcProject");

            var editor = new XbimEditorCredentials
            {
                ApplicationDevelopersName = "RhinoIfc",
                ApplicationFullName = "RhinoIfc Plugin",
                ApplicationIdentifier = "RhinoIfc",
                ApplicationVersion = "0.2.0",
                EditorsFamilyName = System.Environment.UserName,
                EditorsGivenName = "",
                EditorsOrganisationName = ""
            };

            int count = 0;
            double unitScale = UnitConverter.RhinoToMetres(doc.ModelUnitSystem);

            using (var model = IfcStore.Create(editor, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel))
            {
                IfcProject project;
                IfcGeometricRepresentationContext geomContext;
                using (var txn = model.BeginTransaction("Init"))
                {
                    project = model.Instances.New<IfcProject>(p =>
                    {
                        p.Name = projectLayerName ?? doc.Name ?? "Rhino Export";
                        IfcProjectUnits.InitializeMetres(p);
                    });

                    geomContext = model.Instances.New<IfcGeometricRepresentationContext>(c =>
                    {
                        c.ContextType = "Model";
                        c.CoordinateSpaceDimension = 3;
                        c.Precision = 1e-5;
                        c.WorldCoordinateSystem = model.Instances.New<IfcAxis2Placement3D>(a =>
                        {
                            a.Location = model.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(0, 0, 0));
                        });
                    });

                    txn.Commit();
                }

                var siteCache = new Dictionary<string, IfcSite>(StringComparer.OrdinalIgnoreCase);
                var buildingCache = new Dictionary<string, IfcBuilding>(StringComparer.OrdinalIgnoreCase);
                var storeyCache = new Dictionary<string, IfcBuildingStorey>(StringComparer.OrdinalIgnoreCase);
                var defaultBuildings = new Dictionary<IfcSite, IfcBuilding>();
                var defaultStoreys = new Dictionary<IfcBuilding, IfcBuildingStorey>();

                using (var txn = model.BeginTransaction("Elements"))
                {
                    IfcSite defaultSite = null;

                    IfcSite GetDefaultSite()
                    {
                        if (defaultSite != null) return defaultSite;
                        defaultSite = model.Instances.New<IfcSite>(s =>
                        {
                            s.Name = "Default Site";
                            s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                        });
                        CreateAggregation(model, project, defaultSite);
                        return defaultSite;
                    }

                    IfcSite GetSite(string key, string name)
                    {
                        if (siteCache.TryGetValue(key, out var site)) return site;
                        site = model.Instances.New<IfcSite>(s =>
                        {
                            s.Name = name;
                            s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                        });
                        CreateAggregation(model, project, site);
                        siteCache[key] = site;
                        return site;
                    }

                    IfcBuilding GetDefaultBuilding(IfcSite site)
                    {
                        if (defaultBuildings.TryGetValue(site, out var building)) return building;
                        building = model.Instances.New<IfcBuilding>(b =>
                        {
                            b.Name = "Default Building";
                            b.CompositionType = IfcElementCompositionEnum.ELEMENT;
                        });
                        CreateAggregation(model, site, building);
                        defaultBuildings[site] = building;
                        return building;
                    }

                    IfcBuilding GetBuilding(string key, string name, IfcSite site)
                    {
                        if (buildingCache.TryGetValue(key, out var building)) return building;
                        building = model.Instances.New<IfcBuilding>(b =>
                        {
                            b.Name = name;
                            b.CompositionType = IfcElementCompositionEnum.ELEMENT;
                        });
                        CreateAggregation(model, site, building);
                        buildingCache[key] = building;
                        return building;
                    }

                    IfcBuildingStorey GetDefaultStorey(IfcBuilding building)
                    {
                        if (defaultStoreys.TryGetValue(building, out var storey)) return storey;
                        storey = model.Instances.New<IfcBuildingStorey>(s =>
                        {
                            s.Name = "Default Storey";
                            s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                            s.Elevation = 0;
                        });
                        CreateAggregation(model, building, storey);
                        defaultStoreys[building] = storey;
                        return storey;
                    }

                    IfcBuildingStorey GetStorey(string key, string name, IfcBuilding building)
                    {
                        if (storeyCache.TryGetValue(key, out var storey)) return storey;
                        storey = model.Instances.New<IfcBuildingStorey>(s =>
                        {
                            s.Name = name;
                            s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                            s.Elevation = 0;
                        });
                        CreateAggregation(model, building, storey);
                        storeyCache[key] = storey;
                        return storey;
                    }

                    IfcBuildingStorey ResolveStorey(string[] segments)
                    {
                        IfcSite site = null;
                        IfcBuilding building = null;
                        IfcBuildingStorey storey = null;
                        string path = "";

                        foreach (var segment in segments)
                        {
                            path = path.Length == 0 ? segment : $"{path}::{segment}";
                            switch (ClassMapper.MapLayerToIfcClass(segment))
                            {
                                case "IfcProject":
                                    site = null;
                                    building = null;
                                    storey = null;
                                    break;
                                case "IfcSite":
                                    site = GetSite(path, segment);
                                    building = null;
                                    storey = null;
                                    break;
                                case "IfcBuilding":
                                    site = site ?? GetDefaultSite();
                                    building = GetBuilding(path, segment, site);
                                    storey = null;
                                    break;
                                case "IfcBuildingStorey":
                                    site = site ?? GetDefaultSite();
                                    building = building ?? GetDefaultBuilding(site);
                                    storey = GetStorey(path, segment, building);
                                    break;
                            }
                        }

                        site = site ?? GetDefaultSite();
                        building = building ?? GetDefaultBuilding(site);
                        return storey ?? GetDefaultStorey(building);
                    }

                    int seq = 0;
                    foreach (var rhinoObj in exportObjects)
                    {
                        IfcShapeRepresentation representation = null;
                        if (rhinoObj.Geometry is Brep brep)
                        {
                            representation = GeometryExporter.CreatePlanarBrepRepresentation(
                                model, geomContext, brep, unitScale, doc.ModelAbsoluteTolerance);
                        }

                        if (representation == null)
                        {
                            var meshes = InstanceMeshExtractor.Extract(rhinoObj);
                            if (meshes == null || meshes.Length == 0) continue;

                            try
                            {
                                representation = GeometryExporter.CreateRepresentation(
                                    model, geomContext, meshes, unitScale);
                            }
                            finally
                            {
                                foreach (var mesh in meshes) mesh?.Dispose();
                            }
                        }

                        var layer = doc.Layers[rhinoObj.Attributes.LayerIndex];
                        string layerFullPath = layer.FullPath;
                        var targetStorey = ResolveStorey(GetLayerSegments(layerFullPath));
                        string ifcClassName = ClassMapper.MapLayerToIfcClass(layer.Name);

                        string elementName = rhinoObj.Name;
                        if (string.IsNullOrWhiteSpace(elementName)) elementName = rhinoObj.Attributes.Name;
                        if (string.IsNullOrWhiteSpace(elementName)) elementName = $"{layer.Name} {++seq}";

                        var element = CreateElement(model, ifcClassName, elementName);
                        ColorExporter.ApplyColor(model, doc, rhinoObj, representation);
                        element.Representation = model.Instances.New<IfcProductDefinitionShape>(pds =>
                        {
                            pds.Representations.Add(representation);
                        });
                        element.ObjectPlacement = model.Instances.New<IfcLocalPlacement>(lp =>
                        {
                            lp.RelativePlacement = model.Instances.New<IfcAxis2Placement3D>(a =>
                            {
                                a.Location = model.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(0, 0, 0));
                            });
                        });

                        AddToContainer(model, targetStorey, element);
                        PropertyExporter.ExportUserStrings(model, element, rhinoObj, layerFullPath);
                        count++;
                    }

                    txn.Commit();
                }

                model.SaveAs(outputPath, outputPath.EndsWith(".ifczip", StringComparison.OrdinalIgnoreCase)
                    ? StorageType.IfcZip
                    : StorageType.Ifc);
            }

            return count;
        }

        public static IfcProduct CreateElement(IfcStore model, string ifcClassName, string name)
        {
            IfcProduct element = ifcClassName switch
            {
                "IfcSpace" => model.Instances.New<IfcSpace>(),
                "IfcWall" => model.Instances.New<IfcWall>(),
                "IfcSlab" => model.Instances.New<IfcSlab>(),
                "IfcRoof" => model.Instances.New<IfcRoof>(),
                "IfcBeam" => model.Instances.New<IfcBeam>(),
                "IfcColumn" => model.Instances.New<IfcColumn>(),
                "IfcDoor" => model.Instances.New<IfcDoor>(),
                "IfcWindow" => model.Instances.New<IfcWindow>(),
                "IfcCurtainWall" => model.Instances.New<IfcCurtainWall>(),
                "IfcStair" => model.Instances.New<IfcStair>(),
                "IfcStairFlight" => model.Instances.New<IfcStairFlight>(),
                "IfcRailing" => model.Instances.New<IfcRailing>(),
                "IfcRamp" => model.Instances.New<IfcRamp>(),
                "IfcCovering" => model.Instances.New<IfcCovering>(),
                "IfcShadingDevice" => model.Instances.New<IfcShadingDevice>(),
                "IfcFooting" => model.Instances.New<IfcFooting>(),
                "IfcPile" => model.Instances.New<IfcPile>(),
                "IfcMember" => model.Instances.New<IfcMember>(),
                "IfcPlate" => model.Instances.New<IfcPlate>(),
                "IfcOpeningElement" => model.Instances.New<IfcOpeningElement>(),
                "IfcFurniture" => model.Instances.New<IfcFurniture>(),
                "IfcFurnishingElement" => model.Instances.New<IfcFurnishingElement>(),
                "IfcBuildingElementPart" => model.Instances.New<IfcBuildingElementPart>(),
                _ => model.Instances.New<IfcBuildingElementProxy>()
            };

            element.Name = name;
            return element;
        }

        private static string[] GetLayerSegments(string layerFullPath)
        {
            return layerFullPath
                .Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToArray();
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
                return;
            }

            model.Instances.New<IfcRelContainedInSpatialStructure>(rel =>
            {
                rel.RelatingStructure = container;
                rel.RelatedElements.Add(product);
            });
        }
    }
}
