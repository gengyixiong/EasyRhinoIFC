using System.Collections.Generic;
using Rhino.Geometry;
using Xbim.Ifc4.Interfaces;

namespace RhinoIfc.Import.Geometry
{
    /// <summary>
    /// Resolves an IfcMappedItem by converting (and caching) the source
    /// representation map's geometry once, then transforming a fresh copy
    /// for each instance via MappingOrigin and MappingTarget.
    /// </summary>
    internal static class MappedItemResolver
    {
        public static Brep Resolve(IIfcMappedItem mapped, ConversionContext ctx)
        {
            if (mapped?.MappingSource?.MappedRepresentation == null) return null;

            var source = mapped.MappingSource;
            int sourceLabel = source.EntityLabel;

            if (!ctx.RepresentationMapCache.TryGetValue(sourceLabel, out var cached))
            {
                cached = new List<Brep>();
                foreach (var item in source.MappedRepresentation.Items)
                {
                    var brep = IfcGeometryConverter.ConvertItem(item, ctx);
                    if (brep != null) cached.Add(brep);
                }
                ctx.RepresentationMapCache[sourceLabel] = cached;
            }

            if (cached.Count == 0) return null;

            // Combine the cached source breps into a single Brep (they share
            // the source representation's local frame).
            Brep combined;
            if (cached.Count == 1)
            {
                combined = cached[0].DuplicateBrep();
            }
            else
            {
                var joined = Brep.JoinBreps(cached, ctx.Tolerance);
                if (joined != null && joined.Length > 0)
                    combined = joined[0];
                else
                    combined = cached[0].DuplicateBrep();
            }

            // Apply MappingOrigin (the source's local placement within its map),
            // then MappingTarget (this instance's transformation).
            var origin = PlacementConverter.ConvertAxis2Placement(source.MappingOrigin);
            var target = PlacementConverter.ConvertCartesianTransformationOperator3D(
                mapped.MappingTarget as IIfcCartesianTransformationOperator3D);

            // The full local transform is target * origin.
            var fullTransform = target * origin;
            if (!fullTransform.Equals(Transform.Identity))
                combined.Transform(fullTransform);

            return combined;
        }
    }
}
