using System;
using System.Collections.Generic;
using RhinoIfc.Export;

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
