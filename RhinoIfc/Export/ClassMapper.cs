using System;
using System.Collections.Generic;

namespace RhinoIfc.Export
{
    /// <summary>
    /// Maps Rhino layer names to IFC entity class names.
    /// Scans each segment of the layer path for known keywords.
    /// Falls back to IfcBuildingElementProxy when no match is found.
    /// </summary>
    public static class ClassMapper
    {
        private static readonly Dictionary<string, string> Map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Walls
                ["wall"]        = "IfcWall",
                ["walls"]       = "IfcWall",
                ["wand"]        = "IfcWall",       // German
                ["mur"]         = "IfcWall",       // French

                // Slabs / floors
                ["slab"]        = "IfcSlab",
                ["slabs"]       = "IfcSlab",
                ["floor"]       = "IfcSlab",
                ["floors"]      = "IfcSlab",
                ["decke"]       = "IfcSlab",       // German

                // Roof
                ["roof"]        = "IfcRoof",
                ["roofs"]       = "IfcRoof",
                ["dach"]        = "IfcRoof",

                // Columns
                ["column"]      = "IfcColumn",
                ["columns"]     = "IfcColumn",
                ["stütze"]      = "IfcColumn",

                // Beams
                ["beam"]        = "IfcBeam",
                ["beams"]       = "IfcBeam",
                ["träger"]      = "IfcBeam",

                // Doors
                ["door"]        = "IfcDoor",
                ["doors"]       = "IfcDoor",
                ["tür"]         = "IfcDoor",

                // Windows
                ["window"]      = "IfcWindow",
                ["windows"]     = "IfcWindow",
                ["fenster"]     = "IfcWindow",

                // Stairs
                ["stair"]       = "IfcStair",
                ["stairs"]      = "IfcStair",
                ["treppe"]      = "IfcStair",

                // Railing
                ["railing"]     = "IfcRailing",
                ["railings"]    = "IfcRailing",
                ["geländer"]    = "IfcRailing",

                // Furniture
                ["furniture"]   = "IfcFurnishingElement",
                ["furnishing"]  = "IfcFurnishingElement",
                ["möbel"]       = "IfcFurnishingElement",

                // Curtain wall
                ["curtainwall"] = "IfcCurtainWall",
                ["curtain wall"]= "IfcCurtainWall",

                // Plate
                ["plate"]       = "IfcPlate",
                ["plates"]      = "IfcPlate",

                // Covering
                ["covering"]    = "IfcCovering",
                ["ceiling"]     = "IfcCovering",
            };

        /// <summary>
        /// Scans each segment of the full layer path (split by ::) for known keywords.
        /// Returns the IFC class name, or "IfcBuildingElementProxy" as fallback.
        /// </summary>
        public static string MapLayerToIfcClass(string layerFullPath)
        {
            if (string.IsNullOrWhiteSpace(layerFullPath))
                return "IfcBuildingElementProxy";

            // Check each segment of the layer path
            var segments = layerFullPath.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                string trimmed = segment.Trim();
                if (Map.TryGetValue(trimmed, out var ifcClass))
                    return ifcClass;
            }

            // Also try the last segment as a partial match (e.g. "Exterior Walls" contains "wall")
            if (segments.Length > 0)
            {
                string lastSegment = segments[segments.Length - 1].Trim().ToLowerInvariant();
                foreach (var kvp in Map)
                {
                    if (lastSegment.Contains(kvp.Key))
                        return kvp.Value;
                }
            }

            return "IfcBuildingElementProxy";
        }
    }
}
