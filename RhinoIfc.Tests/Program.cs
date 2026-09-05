using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RhinoIfc.Export;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.PresentationOrganizationResource;
using Xbim.Ifc4.RepresentationResource;
using Xbim.IO;

namespace RhinoIfc.Tests
{
    internal static class Program
    {
        private static void Main()
        {
            var mappings = new Dictionary<string, string>
            {
                ["Project"] = "IfcProject",
                ["Site"] = "IfcSite",
                ["Building"] = "IfcBuilding",
                ["BuildingStorey"] = "IfcBuildingStorey",
                ["Storey"] = "IfcBuildingStorey",
                ["Space"] = "IfcSpace",
                ["Wall"] = "IfcWall",
                ["Slab"] = "IfcSlab",
                ["Roof"] = "IfcRoof",
                ["Beam"] = "IfcBeam",
                ["Column"] = "IfcColumn",
                ["Door"] = "IfcDoor",
                ["Window"] = "IfcWindow",
                ["CurtainWall"] = "IfcCurtainWall",
                ["Stair"] = "IfcStair",
                ["StairFlight"] = "IfcStairFlight",
                ["Railing"] = "IfcRailing",
                ["Ramp"] = "IfcRamp",
                ["Covering"] = "IfcCovering",
                ["ShadingDevice"] = "IfcShadingDevice",
                ["Footing"] = "IfcFooting",
                ["Pile"] = "IfcPile",
                ["Member"] = "IfcMember",
                ["Plate"] = "IfcPlate",
                ["Opening"] = "IfcOpeningElement",
                ["Furniture"] = "IfcFurniture",
                ["FurnishingElement"] = "IfcFurnishingElement",
                ["BuildingElementPart"] = "IfcBuildingElementPart",
                ["Proxy"] = "IfcBuildingElementProxy",
                ["BuildingElementProxy"] = "IfcBuildingElementProxy"
            };

            foreach (var mapping in mappings)
                AssertEqual(mapping.Value, ClassMapper.MapLayerToIfcClass($"Layer-{mapping.Key}"));

            AssertEqual("IfcWall", ClassMapper.MapLayerToIfcClass("Existing-Building-Exterior-wAlL"));
            AssertEqual("IfcDoor", ClassMapper.MapLayerToIfcClass("Level 01-Storey::Interior::Doors-Door"));
            AssertEqual(null, ClassMapper.MapLayerToIfcClass("Walls"));
            AssertEqual(null, ClassMapper.MapLayerToIfcClass("Mechanical Equipment-Equipment"));
            AssertEqual(null, ClassMapper.MapLayerToIfcClass("Parent-Wall::Exterior"));

            ValidateInstanceTraversal();
            ValidatePresentationLayers();
            ValidateMappedRepresentations();
            ValidateMappedPostProcessing();
            Console.WriteLine("All export tests passed.");
        }

        private static void ValidateInstanceTraversal()
        {
            var red = new Node("Red");
            var blue = new Node("Blue");
            var green = new Node("Green");
            var nested = new Node("Nested", 2, 5, blue, green);
            var root = new Node("Root", 1, 10, red, nested);
            var visits = new List<string>();

            InstanceGraphTraversal.Traverse<Node, int, int>(
                root,
                0,
                n => n.Children != null,
                n => n.Key,
                n => n.Children,
                n => n.Transform,
                (parent, child) => parent + child,
                (node, transform) => visits.Add($"{node.Name}:{transform}"));

            AssertEqual("Red:10|Blue:15|Green:15", string.Join("|", visits));
        }

        private static void ValidatePresentationLayers()
        {
            var credentials = new XbimEditorCredentials
            {
                ApplicationDevelopersName = "EasyRhinoIFC",
                ApplicationFullName = "EasyRhinoIFC Tests",
                ApplicationIdentifier = "EasyRhinoIFC.Tests",
                ApplicationVersion = "1",
                EditorsFamilyName = "Test",
                EditorsGivenName = "",
                EditorsOrganisationName = ""
            };

            string outputPath = Path.Combine(Path.GetTempPath(), $"EasyRhinoIFC-{Guid.NewGuid():N}.ifc");
            try
            {
                using (var model = IfcStore.Create(
                    credentials, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel))
                {
                    using (var transaction = model.BeginTransaction("Presentation layers"))
                    {
                        var assignments = new Dictionary<Guid, IfcPresentationLayerAssignment>();
                        var wallLayerId = Guid.NewGuid();
                        var roofLayerId = Guid.NewGuid();

                        PresentationLayerExporter.Assign(model, assignments, wallLayerId, "Walls",
                            model.Instances.New<IfcShapeRepresentation>());
                        PresentationLayerExporter.Assign(model, assignments, wallLayerId, "Walls",
                            model.Instances.New<IfcShapeRepresentation>());
                        PresentationLayerExporter.Assign(model, assignments, wallLayerId, "Walls",
                            model.Instances.New<IfcTriangulatedFaceSet>());
                        PresentationLayerExporter.Assign(model, assignments, roofLayerId, "Roofs",
                            model.Instances.New<IfcShapeRepresentation>());

                        AssertEqual("2", assignments.Count.ToString());
                        AssertEqual("3", assignments[wallLayerId].AssignedItems.Count.ToString());
                        AssertEqual("1", assignments[roofLayerId].AssignedItems.Count.ToString());
                        transaction.Commit();
                    }

                    model.SaveAs(outputPath, StorageType.Ifc);
                }

                string ifc = File.ReadAllText(outputPath);
                int layerCount = ifc.Split(new[] { "IFCPRESENTATIONLAYERASSIGNMENT(" },
                    StringSplitOptions.None).Length - 1;
                AssertEqual("2", layerCount.ToString());
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        private static void ValidateMappedRepresentations()
        {
            using (var model = IfcStore.Create(new XbimEditorCredentials
            {
                ApplicationDevelopersName = "EasyRhinoIFC",
                ApplicationFullName = "EasyRhinoIFC Tests",
                ApplicationIdentifier = "EasyRhinoIFC.Tests",
                ApplicationVersion = "1",
                EditorsFamilyName = "Test",
                EditorsGivenName = "",
                EditorsOrganisationName = ""
            }, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel))
            using (var transaction = model.BeginTransaction("Mapped representations"))
            {
                var context = model.Instances.New<IfcGeometricRepresentationContext>();
                var definitionRepresentation = model.Instances.New<IfcShapeRepresentation>(representation =>
                {
                    representation.ContextOfItems = context;
                    representation.RepresentationIdentifier = "Body";
                    representation.RepresentationType = "Tessellation";
                });
                var map = MappedRepresentationFactory.CreateMap(model, definitionRepresentation);

                for (var i = 0; i < 2; i++)
                {
                    var transform = model.Instances.New<IfcCartesianTransformationOperator3D>(operation =>
                    {
                        operation.LocalOrigin = model.Instances.New<IfcCartesianPoint>(point => point.SetXYZ(i, 0, 0));
                        operation.Scale = 1;
                    });
                    MappedRepresentationFactory.CreateOccurrence(model, context, map, transform);
                }

                AssertEqual("1", model.Instances.OfType<IfcRepresentationMap>().Count().ToString());
                var mappedItems = model.Instances.OfType<IfcMappedItem>().ToArray();
                AssertEqual("2", mappedItems.Length.ToString());
                AssertEqual(map.EntityLabel.ToString(), mappedItems[0].MappingSource.EntityLabel.ToString());
                AssertEqual(map.EntityLabel.ToString(), mappedItems[1].MappingSource.EntityLabel.ToString());
                AssertEqual("MappedRepresentation", model.Instances.OfType<IfcShapeRepresentation>()
                    .Last().RepresentationType.ToString());
                transaction.Commit();
            }
        }

        private static void ValidateMappedPostProcessing()
        {
            AssertEqual(ExportPostProcessingPath.Mapped.ToString(),
                ExportPostProcessing.Select<object>(true, null).ToString());
            AssertEqual(ExportPostProcessingPath.Object.ToString(),
                ExportPostProcessing.Select<object>(false, null).ToString());
            AssertEqual(ExportPostProcessingPath.Extracted.ToString(),
                ExportPostProcessing.Select(false, new object[0]).ToString());
        }

        private static void AssertEqual(string expected, string actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new InvalidOperationException($"Expected '{expected ?? "null"}', got '{actual ?? "null"}'.");
        }

        private sealed class Node
        {
            public string Name { get; }
            public int Key { get; }
            public int Transform { get; }
            public Node[] Children { get; }

            public Node(string name)
            {
                Name = name;
            }

            public Node(string name, int key, int transform, params Node[] children)
            {
                Name = name;
                Key = key;
                Transform = transform;
                Children = children;
            }
        }
    }
}
