using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using Xbim.Ifc4.Interfaces;

namespace RhinoIfc.Import.Geometry
{
    /// <summary>
    /// Top-level entry point for the parametric Brep import path. Walks an
    /// IfcProduct's Body representation, converts each item to a Rhino Brep,
    /// applies opening subtractions and the product's world-space placement,
    /// and returns the resulting Brep list. Returns false (and an empty list)
    /// when any Body item fails to convert — the caller then falls through
    /// to the legacy mesh tessellation path so the element is never lost.
    /// </summary>
    internal static class IfcGeometryConverter
    {
        /// <summary>
        /// Top-level: try to convert a product's Body representation into one
        /// or more world-space Breps. The all-or-nothing rule guarantees no
        /// duplication with the mesh fallback pass.
        /// </summary>
        public static bool TryConvertProduct(IIfcProduct product, ConversionContext ctx, out List<Brep> breps)
        {
            breps = new List<Brep>();
            if (product?.Representation == null) return false;

            // Find the "Body" representation (the geometric one). Other
            // representations like "Axis" / "FootPrint" / "Box" are skipped.
            IIfcRepresentation bodyRep = null;
            foreach (var rep in product.Representation.Representations)
            {
                var ident = rep.RepresentationIdentifier;
                if (ident.HasValue && string.Equals(ident.Value, "Body", StringComparison.OrdinalIgnoreCase))
                {
                    bodyRep = rep;
                    break;
                }
            }
            if (bodyRep == null) return false;

            // Convert each item in Body. All-or-nothing: if any item fails,
            // we abort and let the mesh path handle the whole product.
            var localBreps = new List<Brep>();
            foreach (var item in bodyRep.Items)
            {
                var brep = ConvertItem(item, ctx);
                if (brep == null || !brep.IsValid) return false;
                localBreps.Add(brep);
            }
            if (localBreps.Count == 0) return false;

            // Opening subtraction (IfcRelVoidsElement chain). Only IfcElement
            // (not all IfcProduct) carries openings.
            if (product is IIfcElement element)
            {
                var openings = element.HasOpenings;
                if (openings != null)
                {
                    foreach (var rel in openings)
                    {
                        var opening = rel.RelatedOpeningElement;
                        if (opening == null) continue;
                        var openingBreps = new List<Brep>();
                        if (TryConvertProduct(opening, ctx, out var ob))
                            openingBreps.AddRange(ob);
                        if (openingBreps.Count == 0) continue;

                        var newLocal = new List<Brep>();
                        foreach (var host in localBreps)
                        {
                            var diff = Brep.CreateBooleanDifference(
                                new[] { host }, openingBreps, ctx.Tolerance);
                            if (diff != null && diff.Length > 0)
                                newLocal.AddRange(diff);
                            else
                                newLocal.Add(host); // best-effort: keep uncut
                        }
                        localBreps.Clear();
                        localBreps.AddRange(newLocal);
                    }
                }
            }

            // Apply product object placement (world transform) and unit scale.
            var placement = PlacementConverter.ConvertObjectPlacement(product.ObjectPlacement);
            var unitScale = Transform.Scale(Point3d.Origin, ctx.UnitScale);
            var combined = unitScale * placement;

            foreach (var b in localBreps)
            {
                if (!combined.Equals(Transform.Identity))
                    b.Transform(combined);
                if (b.IsValid) breps.Add(b);
            }

            return breps.Count > 0;
        }

        /// <summary>
        /// Per-item dispatcher. Returns null on any unsupported type or
        /// conversion failure (callers must handle null).
        /// </summary>
        public static Brep ConvertItem(IIfcRepresentationItem item, ConversionContext ctx)
        {
            if (item == null) return null;
            try
            {
                switch (item)
                {
                    case IIfcExtrudedAreaSolid eas:        return ExtrudedSolidConverter.Convert(eas, ctx);
                    case IIfcBooleanClippingResult bcr:    return BooleanEvaluator.Evaluate(bcr, ctx);
                    case IIfcBooleanResult br:             return BooleanEvaluator.Evaluate(br, ctx);
                    case IIfcFacetedBrep fb:               return FacetedBrepConverter.Convert(fb, ctx);
                    case IIfcPolygonalFaceSet pfs:         return TessellatedConverter.Convert(pfs, ctx);
                    case IIfcTriangulatedFaceSet tfs:      return TessellatedConverter.Convert(tfs, ctx);
                    case IIfcMappedItem mi:                return MappedItemResolver.Resolve(mi, ctx);
                }
            }
            catch
            {
                return null;
            }
            return null; // unsupported type
        }

        /// <summary>
        /// Operand dispatcher used by BooleanEvaluator. Same as ConvertItem,
        /// plus support for IfcHalfSpaceSolid / IfcPolygonalBoundedHalfSpace
        /// (which are only valid as boolean operands, not as standalone Body
        /// items). The hostBox is passed through to the half-space converter
        /// so it can size the resulting Brep large enough to fully cut the
        /// host operand.
        /// </summary>
        public static Brep ConvertOperand(IIfcBooleanOperand operand, ConversionContext ctx, BoundingBox hostBox)
        {
            if (operand == null) return null;

            try
            {
                if (operand is IIfcHalfSpaceSolid hs)
                    return HalfSpaceConverter.Convert(hs, ctx, hostBox);

                if (operand is IIfcRepresentationItem item)
                    return ConvertItem(item, ctx);
            }
            catch
            {
                return null;
            }
            return null;
        }
    }
}
