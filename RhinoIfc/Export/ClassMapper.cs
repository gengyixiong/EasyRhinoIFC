using System;
using System.Collections.Generic;

namespace RhinoIfc.Export
{
    public static class ClassMapper
    {
        private static readonly Dictionary<string, string> Map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Project", "IfcProject" },
                { "Site", "IfcSite" },
                { "Building", "IfcBuilding" },
                { "BuildingStorey", "IfcBuildingStorey" },
                { "Storey", "IfcBuildingStorey" },
                { "Space", "IfcSpace" },
                { "Wall", "IfcWall" },
                { "Slab", "IfcSlab" },
                { "Roof", "IfcRoof" },
                { "Beam", "IfcBeam" },
                { "Column", "IfcColumn" },
                { "Door", "IfcDoor" },
                { "Window", "IfcWindow" },
                { "CurtainWall", "IfcCurtainWall" },
                { "Stair", "IfcStair" },
                { "StairFlight", "IfcStairFlight" },
                { "Railing", "IfcRailing" },
                { "Ramp", "IfcRamp" },
                { "Covering", "IfcCovering" },
                { "ShadingDevice", "IfcShadingDevice" },
                { "Footing", "IfcFooting" },
                { "Pile", "IfcPile" },
                { "Member", "IfcMember" },
                { "Plate", "IfcPlate" },
                { "Opening", "IfcOpeningElement" },
                { "Furniture", "IfcFurniture" },
                { "FurnishingElement", "IfcFurnishingElement" },
                { "BuildingElementPart", "IfcBuildingElementPart" },
                { "Proxy", "IfcBuildingElementProxy" },
                { "BuildingElementProxy", "IfcBuildingElementProxy" }
            };

        /// <summary>
        /// Maps only the suffix after the last hyphen in the leaf Rhino layer.
        /// Unknown or missing suffixes are intentionally left unclassified.
        /// </summary>
        public static string MapLayerToIfcClass(string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName)) return null;

            int pathSeparator = layerName.LastIndexOf("::", StringComparison.Ordinal);
            string leaf = layerName.Substring(pathSeparator < 0 ? 0 : pathSeparator + 2).Trim();
            int hyphen = leaf.LastIndexOf('-');
            if (hyphen < 0 || hyphen == leaf.Length - 1) return null;

            string ifcClass;
            return Map.TryGetValue(leaf.Substring(hyphen + 1).Trim(), out ifcClass)
                ? ifcClass
                : null;
        }
    }
}
