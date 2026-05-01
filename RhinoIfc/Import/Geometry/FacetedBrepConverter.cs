using System;
using System.Collections.Generic;
using Rhino.Geometry;
using Xbim.Ifc4.Interfaces;

namespace RhinoIfc.Import.Geometry
{
    /// <summary>
    /// Converts an IfcFacetedBrep into a Rhino polysurface by building one
    /// planar single-face Brep per IfcFace and joining them. The Outer shell
    /// is conventionally an IfcClosedShell but xBIM also exposes the more
    /// permissive IfcConnectedFaceSet via the same hierarchy (Revit 2025+
    /// emits the latter for some elements).
    /// </summary>
    internal static class FacetedBrepConverter
    {
        public static Brep Convert(IIfcFacetedBrep brep, ConversionContext ctx)
        {
            if (brep?.Outer == null) return null;

            try
            {
                var faces = new List<Brep>();
                CollectFaces(brep.Outer, ctx, faces);
                if (faces.Count == 0) return null;

                var joined = Brep.JoinBreps(faces, ctx.Tolerance);
                if (joined == null || joined.Length == 0) return null;

                // If multiple shells came back, pick the largest by surface area.
                Brep best = joined[0];
                double bestArea = SafeArea(best);
                for (int i = 1; i < joined.Length; i++)
                {
                    double a = SafeArea(joined[i]);
                    if (a > bestArea) { best = joined[i]; bestArea = a; }
                }
                return best;
            }
            catch
            {
                return null;
            }
        }

        public static void CollectFaces(IIfcConnectedFaceSet faceSet, ConversionContext ctx, List<Brep> faces)
        {
            if (faceSet == null) return;
            foreach (var face in faceSet.CfsFaces)
            {
                var faceBrep = ConvertFace(face, ctx);
                if (faceBrep != null) faces.Add(faceBrep);
            }
        }

        private static Brep ConvertFace(IIfcFace face, ConversionContext ctx)
        {
            if (face == null) return null;

            Curve outer = null;
            var holes = new List<Curve>();
            foreach (var bound in face.Bounds)
            {
                var loop = bound.Bound;
                Curve curve = LoopToCurve(loop);
                if (curve == null) continue;
                if (!bound.Orientation) curve.Reverse();

                if (bound is IIfcFaceOuterBound) outer = curve;
                else if (outer == null) outer = curve; // first bound seen, will swap if outer arrives later
                else holes.Add(curve);
            }
            if (outer == null) return null;

            var input = new List<Curve> { outer };
            input.AddRange(holes);
            var planar = Brep.CreatePlanarBreps(input, ctx.Tolerance);
            if (planar == null || planar.Length == 0) return null;
            return planar[0];
        }

        private static Curve LoopToCurve(IIfcLoop loop)
        {
            if (loop is IIfcPolyLoop poly)
            {
                var pts = new List<Point3d>();
                foreach (var p in poly.Polygon)
                    pts.Add(CurveConverter.ToPoint3d(p));
                if (pts.Count < 3) return null;
                if (pts[0].DistanceTo(pts[pts.Count - 1]) > 1e-9)
                    pts.Add(pts[0]);
                return new PolylineCurve(pts);
            }
            return null;
        }

        private static double SafeArea(Brep b)
        {
            try
            {
                if (b == null || !b.IsValid) return 0;
                var amp = AreaMassProperties.Compute(b);
                return amp != null ? amp.Area : 0;
            }
            catch { return 0; }
        }
    }
}
