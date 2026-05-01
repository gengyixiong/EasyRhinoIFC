using System;
using System.Collections.Generic;
using Rhino.Geometry;
using Xbim.Ifc4.Interfaces;

namespace RhinoIfc.Import.Geometry
{
    /// <summary>
    /// Builds a finite Rhino Brep approximating an IFC half-space solid,
    /// large enough to fully contain (and protrude beyond) the operand it
    /// will be subtracted from. The "fallback box" parameter is the world-
    /// space bounding box of that operand; the resulting Brep is sized to
    /// be 10x larger so the boolean cut succeeds cleanly.
    /// </summary>
    internal static class HalfSpaceConverter
    {
        public static Brep Convert(IIfcHalfSpaceSolid hs, ConversionContext ctx, BoundingBox fallback)
        {
            if (hs == null) return null;

            try
            {
                if (hs is IIfcPolygonalBoundedHalfSpace pbhs)
                    return ConvertPolygonalBounded(pbhs, ctx, fallback);
                return ConvertSimple(hs, fallback);
            }
            catch
            {
                return null;
            }
        }

        // ---- IfcHalfSpaceSolid ---------------------------------------------

        private static Brep ConvertSimple(IIfcHalfSpaceSolid hs, BoundingBox fallback)
        {
            var plane = ExtractBaseSurfacePlane(hs.BaseSurface);
            if (!plane.IsValid) return null;

            // The half-space lies on the side OPPOSITE to the surface normal
            // when AgreementFlag is true. (IFC: "If TRUE, then the subset is
            // the one the normal points away from".)
            bool flip = hs.AgreementFlag;

            return BuildHalfSpaceBox(plane, flip, fallback);
        }

        // ---- IfcPolygonalBoundedHalfSpace ---------------------------------

        private static Brep ConvertPolygonalBounded(IIfcPolygonalBoundedHalfSpace pbhs, ConversionContext ctx, BoundingBox fallback)
        {
            // 1. Plane of the polygon (Position).
            var polyPlaneTransform = PlacementConverter.ConvertAxis2Placement3D(pbhs.Position);
            var polyPlane = PlaneFromTransform(polyPlaneTransform);
            if (!polyPlane.IsValid) return null;

            // 2. Convert the boundary polygon (in 2D Position-local coords -> 3D after transform).
            var boundary = CurveConverter.Convert(pbhs.PolygonalBoundary as IIfcCurve, ctx);
            if (boundary == null) return null;
            boundary.Transform(polyPlaneTransform);
            if (!boundary.IsClosed) boundary.MakeClosed(1e-4);
            if (!boundary.IsClosed) return null;

            // 3. Extrude the boundary along the polygon plane normal in BOTH directions
            // by a large amount, so the resulting prism contains everything we'd want to clip.
            double extent = ComputeExtent(fallback);
            var normal = polyPlane.ZAxis;
            var startTransform = Transform.Translation(-normal * extent);
            var movedDown = boundary.DuplicateCurve();
            movedDown.Transform(startTransform);

            var ext = Extrusion.CreateExtrusion(movedDown, normal * (2 * extent));
            if (ext == null) return null;
            var prism = ext.ToBrep();
            if (prism == null) return null;
            // Cap the prism into a closed solid.
            var capped = prism.CapPlanarHoles(1e-4);
            if (capped != null && capped.IsSolid) prism = capped;

            // 4. Build the base half-space box and intersect.
            var basePlane = ExtractBaseSurfacePlane(pbhs.BaseSurface);
            if (!basePlane.IsValid)
            {
                // No valid base plane: just return the prism.
                return prism;
            }
            bool flip = pbhs.AgreementFlag;
            var halfSpaceBox = BuildHalfSpaceBox(basePlane, flip, fallback);
            if (halfSpaceBox == null) return prism;

            var intersected = Brep.CreateBooleanIntersection(prism, halfSpaceBox, 1e-4);
            if (intersected != null && intersected.Length > 0)
                return intersected[0];
            return prism;
        }

        // ---- helpers --------------------------------------------------------

        private static Plane ExtractBaseSurfacePlane(IIfcSurface surface)
        {
            if (surface is IIfcPlane plane)
            {
                var t = PlacementConverter.ConvertAxis2Placement3D(plane.Position);
                return PlaneFromTransform(t);
            }
            return Plane.Unset;
        }

        private static Plane PlaneFromTransform(Transform t)
        {
            var origin = new Point3d(t.M03, t.M13, t.M23);
            var x = new Vector3d(t.M00, t.M10, t.M20);
            var y = new Vector3d(t.M01, t.M11, t.M21);
            var p = new Plane(origin, x, y);
            return p.IsValid ? p : Plane.Unset;
        }

        private static Brep BuildHalfSpaceBox(Plane plane, bool flip, BoundingBox fallback)
        {
            double extent = ComputeExtent(fallback);

            // Box centred on the plane origin, extending +/-extent along X and Y,
            // and either 0..+extent or -extent..0 along Z (the half-space side).
            var interval = new Interval(-extent, extent);
            var zInterval = flip ? new Interval(-2 * extent, 0) : new Interval(0, 2 * extent);

            var box = new Box(plane, interval, interval, zInterval);
            return box.ToBrep();
        }

        private static double ComputeExtent(BoundingBox box)
        {
            // Use the operand's diagonal scaled up; falls back to a generous
            // default if the box is empty/invalid (e.g. when no fallback is supplied).
            const double minExtent = 100.0;
            if (box.IsValid)
            {
                double diag = box.Diagonal.Length;
                if (diag > 0) return Math.Max(minExtent, diag * 10.0);
            }
            return 1000.0;
        }
    }
}
