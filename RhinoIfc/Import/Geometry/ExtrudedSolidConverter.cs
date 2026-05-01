using System;
using System.Collections.Generic;
using Rhino.Geometry;
using Xbim.Ifc4.Interfaces;

namespace RhinoIfc.Import.Geometry
{
    /// <summary>
    /// Converts an IfcExtrudedAreaSolid into a capped Rhino Brep, in
    /// IFC-native units. The solid's own Position transform is baked in
    /// (so the result lives in the parent product's local frame). Returns
    /// null if conversion fails — callers must handle null and fall through
    /// to the mesh path.
    /// </summary>
    internal static class ExtrudedSolidConverter
    {
        public static Brep Convert(IIfcExtrudedAreaSolid solid, ConversionContext ctx)
        {
            if (solid == null) return null;

            var profile = ProfileConverter.Convert(solid.SweptArea, ctx);
            if (profile?.Outer == null) return null;

            double depth = (double)solid.Depth;
            if (depth <= 0) return null;

            // ExtrudedDirection is given in the solid's local Position frame.
            var dirLocal = ToVector(solid.ExtrudedDirection);
            if (dirLocal.IsZero) dirLocal = Vector3d.ZAxis;
            dirLocal.Unitize();

            Brep result = null;

            // Fast path: extrusion along profile-local Z (the IFC default).
            // The profile curves are already on the XY plane (with Position2D
            // baked in by ProfileConverter), so Extrusion.Create works directly.
            if (Math.Abs(dirLocal.Z) > 1.0 - 1e-6 &&
                Math.Abs(dirLocal.X) < 1e-6 &&
                Math.Abs(dirLocal.Y) < 1e-6)
            {
                double signedDepth = dirLocal.Z >= 0 ? depth : -depth;
                try
                {
                    var ext = Extrusion.Create(profile.Outer, signedDepth, true);
                    if (ext != null)
                    {
                        foreach (var v in profile.Voids)
                            ext.AddInnerProfile(v);
                        result = ext.ToBrep(true);
                    }
                }
                catch
                {
                    result = null;
                }
            }

            // Generic / oblique path: build planar caps and side walls explicitly.
            if (result == null || !result.IsValid)
            {
                result = BuildObliqueExtrusion(profile, dirLocal, depth, ctx.Tolerance);
            }

            if (result == null) return null;

            // Apply the solid's 3D placement transform.
            var solidTransform = PlacementConverter.ConvertAxis2Placement3D(solid.Position);
            if (!solidTransform.Equals(Transform.Identity))
                result.Transform(solidTransform);

            return result.IsValid ? result : null;
        }

        // ---- oblique fallback ---------------------------------------------

        private static Brep BuildObliqueExtrusion(ProfileResult profile, Vector3d dirLocal, double depth, double tol)
        {
            try
            {
                // Sweep vector in profile-local coordinates.
                var sweep = dirLocal * depth;

                // Bottom planar face from outer + inner profiles.
                var profileCurves = new List<Curve>();
                profileCurves.Add(profile.Outer);
                foreach (var v in profile.Voids) profileCurves.Add(v);

                var bottomBreps = Brep.CreatePlanarBreps(profileCurves, tol);
                if (bottomBreps == null || bottomBreps.Length == 0) return null;
                var bottom = bottomBreps[0];

                // Top is bottom translated by sweep.
                var top = bottom.DuplicateBrep();
                top.Transform(Transform.Translation(sweep));

                // Side walls: extrude each boundary edge of the bottom along sweep.
                var sides = new List<Brep>();
                foreach (var edge in bottom.Edges)
                {
                    var edgeCurve = edge.DuplicateCurve();
                    if (edgeCurve == null) continue;
                    var surface = Surface.CreateExtrusion(edgeCurve, sweep);
                    if (surface == null) continue;
                    var sideBrep = surface.ToBrep();
                    if (sideBrep != null) sides.Add(sideBrep);
                }

                var all = new List<Brep> { bottom, top };
                all.AddRange(sides);
                var joined = Brep.JoinBreps(all, tol);
                if (joined == null || joined.Length == 0) return null;

                // Take the largest joined piece (in case of multiple).
                Brep best = joined[0];
                double bestVol = SafeVolume(best);
                for (int i = 1; i < joined.Length; i++)
                {
                    double v = SafeVolume(joined[i]);
                    if (v > bestVol) { best = joined[i]; bestVol = v; }
                }
                return best;
            }
            catch
            {
                return null;
            }
        }

        private static double SafeVolume(Brep b)
        {
            try
            {
                if (b == null || !b.IsValid) return 0;
                var vmp = VolumeMassProperties.Compute(b);
                return vmp != null ? Math.Abs(vmp.Volume) : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static Vector3d ToVector(IIfcDirection d)
        {
            if (d == null) return Vector3d.ZAxis;
            return new Vector3d(SafeDouble(d.X), SafeDouble(d.Y), SafeDouble(d.Z));
        }

        private static double SafeDouble(double v) =>
            double.IsNaN(v) || double.IsInfinity(v) ? 0.0 : v;
    }
}
