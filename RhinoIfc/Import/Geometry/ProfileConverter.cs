using System.Collections.Generic;
using Rhino.Geometry;
using Xbim.Ifc4.Interfaces;

namespace RhinoIfc.Import.Geometry
{
    /// <summary>
    /// Result of converting an IfcProfileDef. <see cref="Outer"/> is the
    /// outer boundary curve in 3D (lying on the XY plane after the profile's
    /// own 2D placement is applied). <see cref="Voids"/> is the list of
    /// inner-boundary curves (also closed).
    /// </summary>
    internal sealed class ProfileResult
    {
        public Curve Outer { get; }
        public IReadOnlyList<Curve> Voids { get; }

        public ProfileResult(Curve outer, IReadOnlyList<Curve> voids)
        {
            Outer = outer;
            Voids = voids ?? System.Array.Empty<Curve>();
        }
    }

    /// <summary>
    /// Converts IFC swept-area profile definitions into 3D Rhino curves
    /// lying on the XY plane (with the profile's local Position transform
    /// already applied). The caller (the extruded-solid converter) is
    /// responsible for any further 3D placement.
    /// </summary>
    internal static class ProfileConverter
    {
        public static ProfileResult Convert(IIfcProfileDef profile, ConversionContext ctx)
        {
            if (profile == null) return null;

            try
            {
                switch (profile)
                {
                    case IIfcRectangleProfileDef rect:           return ConvertRectangle(rect);
                    case IIfcCircleHollowProfileDef hollow:      return ConvertCircleHollow(hollow);
                    case IIfcCircleProfileDef circle:            return ConvertCircle(circle);
                    case IIfcIShapeProfileDef ishape:            return ConvertIShape(ishape);
                    case IIfcArbitraryProfileDefWithVoids voids: return ConvertArbitraryWithVoids(voids, ctx);
                    case IIfcArbitraryClosedProfileDef closed:   return ConvertArbitraryClosed(closed, ctx);
                    case IIfcArbitraryOpenProfileDef _:          return null; // not a solid profile
                }
            }
            catch
            {
                return null;
            }
            return null;
        }

        // ---- Rectangle -----------------------------------------------------

        private static ProfileResult ConvertRectangle(IIfcRectangleProfileDef rect)
        {
            double xDim = (double)rect.XDim;
            double yDim = (double)rect.YDim;
            if (xDim <= 0 || yDim <= 0) return null;

            double hx = xDim * 0.5;
            double hy = yDim * 0.5;
            var pts = new[]
            {
                new Point3d(-hx, -hy, 0),
                new Point3d( hx, -hy, 0),
                new Point3d( hx,  hy, 0),
                new Point3d(-hx,  hy, 0),
                new Point3d(-hx, -hy, 0)
            };
            var pl = new PolylineCurve(pts);
            ApplyPosition(pl, rect.Position);
            return new ProfileResult(pl, null);
        }

        // ---- Circle (full and hollow) --------------------------------------

        private static ProfileResult ConvertCircle(IIfcCircleProfileDef circle)
        {
            double r = (double)circle.Radius;
            if (r <= 0) return null;
            var plane = ProfilePlane(circle.Position);
            var c = new Circle(plane, r).ToNurbsCurve();
            return new ProfileResult(c, null);
        }

        private static ProfileResult ConvertCircleHollow(IIfcCircleHollowProfileDef hollow)
        {
            double rOuter = (double)hollow.Radius;
            double t = (double)hollow.WallThickness;
            if (rOuter <= 0 || t <= 0 || t >= rOuter) return null;
            var plane = ProfilePlane(hollow.Position);
            var outer = new Circle(plane, rOuter).ToNurbsCurve();
            var inner = new Circle(plane, rOuter - t).ToNurbsCurve();
            return new ProfileResult(outer, new Curve[] { inner });
        }

        // ---- I-Shape -------------------------------------------------------

        private static ProfileResult ConvertIShape(IIfcIShapeProfileDef ishape)
        {
            double w = (double)ishape.OverallWidth;
            double d = (double)ishape.OverallDepth;
            double tw = (double)ishape.WebThickness;
            double tf = (double)ishape.FlangeThickness;
            if (w <= 0 || d <= 0 || tw <= 0 || tf <= 0) return null;
            if (tw >= w || 2 * tf >= d) return null;

            double hx = w * 0.5;
            double hy = d * 0.5;
            double hwt = tw * 0.5;
            double yFlangeInner = hy - tf;

            // 12-vertex polyline, CCW starting at lower-right outer corner.
            var pts = new[]
            {
                new Point3d( hx, -hy, 0),
                new Point3d( hx, -yFlangeInner, 0),
                new Point3d( hwt, -yFlangeInner, 0),
                new Point3d( hwt,  yFlangeInner, 0),
                new Point3d( hx,  yFlangeInner, 0),
                new Point3d( hx,  hy, 0),
                new Point3d(-hx,  hy, 0),
                new Point3d(-hx,  yFlangeInner, 0),
                new Point3d(-hwt, yFlangeInner, 0),
                new Point3d(-hwt, -yFlangeInner, 0),
                new Point3d(-hx, -yFlangeInner, 0),
                new Point3d(-hx, -hy, 0),
                new Point3d( hx, -hy, 0)
            };
            var pl = new PolylineCurve(pts);
            ApplyPosition(pl, ishape.Position);
            return new ProfileResult(pl, null);
        }

        // ---- Arbitrary closed (and with voids) -----------------------------

        private static ProfileResult ConvertArbitraryClosed(IIfcArbitraryClosedProfileDef profile, ConversionContext ctx)
        {
            var outer = CurveConverter.Convert(profile.OuterCurve, ctx);
            if (outer == null) return null;
            EnsureClosed(ref outer);
            return new ProfileResult(outer, null);
        }

        private static ProfileResult ConvertArbitraryWithVoids(IIfcArbitraryProfileDefWithVoids profile, ConversionContext ctx)
        {
            var outer = CurveConverter.Convert(profile.OuterCurve, ctx);
            if (outer == null) return null;
            EnsureClosed(ref outer);

            var voids = new List<Curve>();
            foreach (var inner in profile.InnerCurves)
            {
                var c = CurveConverter.Convert(inner, ctx);
                if (c == null) continue;
                EnsureClosed(ref c);
                voids.Add(c);
            }
            return new ProfileResult(outer, voids);
        }

        // ---- helpers --------------------------------------------------------

        private static void ApplyPosition(Curve curve, IIfcAxis2Placement2D position)
        {
            if (position == null || curve == null) return;
            var t = PlacementConverter.ConvertAxis2Placement2D(position);
            curve.Transform(t);
        }

        private static Plane ProfilePlane(IIfcAxis2Placement2D position)
        {
            if (position == null) return Plane.WorldXY;
            var t = PlacementConverter.ConvertAxis2Placement2D(position);
            var origin = new Point3d(t.M03, t.M13, t.M23);
            var x = new Vector3d(t.M00, t.M10, t.M20);
            var y = new Vector3d(t.M01, t.M11, t.M21);
            return new Plane(origin, x, y);
        }

        private static void EnsureClosed(ref Curve c)
        {
            if (c == null) return;
            if (!c.IsClosed)
            {
                if (!c.MakeClosed(1e-4))
                {
                    // Fallback: append a closing line.
                    var poly = new PolyCurve();
                    poly.Append(c);
                    poly.Append(new LineCurve(c.PointAtEnd, c.PointAtStart));
                    c = poly;
                }
            }
        }
    }
}
