using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using Xbim.Common;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.MeasureResource;

namespace RhinoIfc.Import.Geometry
{
    /// <summary>
    /// Converts IFC bounded-curve entities to Rhino curves.
    /// All output is in IFC-native units; the unit scale is applied
    /// once, at the very end of product import.
    /// </summary>
    internal static class CurveConverter
    {
        /// <summary>
        /// Top-level dispatcher. Returns null if the curve type is not
        /// supported or if construction fails — callers must handle null.
        /// </summary>
        public static Curve Convert(IIfcCurve curve, ConversionContext ctx)
        {
            if (curve == null) return null;

            try
            {
                switch (curve)
                {
                    case IIfcPolyline pl:                       return ConvertPolyline(pl);
                    case IIfcTrimmedCurve tc:                   return ConvertTrimmedCurve(tc, ctx);
                    case IIfcCircle c:                          return ConvertCircleFull(c);
                    case IIfcEllipse e:                         return ConvertEllipseFull(e);
                    case IIfcCompositeCurve cc:                 return ConvertCompositeCurve(cc, ctx);
                    case IIfcIndexedPolyCurve ipc:              return ConvertIndexedPolyCurve(ipc);
                    case IIfcRationalBSplineCurveWithKnots rbs: return ConvertBSpline(rbs, rbs.WeightsData);
                    case IIfcBSplineCurveWithKnots bs:          return ConvertBSpline(bs, null);
                    case IIfcLine _:                            return null; // unbounded; only meaningful inside a trim
                }
            }
            catch
            {
                return null;
            }
            return null;
        }

        // ---- IIfcPolyline ---------------------------------------------------

        private static PolylineCurve ConvertPolyline(IIfcPolyline polyline)
        {
            var pts = polyline.Points;
            if (pts == null || pts.Count < 2) return null;

            var rhinoPts = new List<Point3d>(pts.Count);
            foreach (var p in pts) rhinoPts.Add(ToPoint3d(p));

            var pc = new PolylineCurve(rhinoPts);
            return pc;
        }

        // ---- IIfcCircle / IIfcEllipse (full, untrimmed) --------------------

        private static NurbsCurve ConvertCircleFull(IIfcCircle ifcCircle)
        {
            var plane = PlaneFromAxis2(ifcCircle.Position);
            double r = (double)ifcCircle.Radius;
            if (r <= 0) return null;
            return new Circle(plane, r).ToNurbsCurve();
        }

        private static NurbsCurve ConvertEllipseFull(IIfcEllipse ifcEllipse)
        {
            var plane = PlaneFromAxis2(ifcEllipse.Position);
            double a = (double)ifcEllipse.SemiAxis1;
            double b = (double)ifcEllipse.SemiAxis2;
            if (a <= 0 || b <= 0) return null;
            return new Ellipse(plane, a, b).ToNurbsCurve();
        }

        // ---- IIfcTrimmedCurve ----------------------------------------------

        private static Curve ConvertTrimmedCurve(IIfcTrimmedCurve tc, ConversionContext ctx)
        {
            if (tc.BasisCurve == null) return null;

            // The basis curve is one of: IfcLine, IfcCircle, IfcEllipse, IfcPolyline,
            // IfcCompositeCurve, IfcBSplineCurve, etc. We need both parameter and
            // 3D-point trim representations to be able to evaluate the trim.
            bool preferParam =
                tc.MasterRepresentation == IfcTrimmingPreference.PARAMETER ||
                tc.MasterRepresentation == IfcTrimmingPreference.UNSPECIFIED;

            // Most common case: line trimmed by two points -> a line segment.
            if (tc.BasisCurve is IIfcLine line)
            {
                if (TryGetTrimPoints(tc, line, out var p1, out var p2))
                {
                    if (!tc.SenseAgreement) { var t = p1; p1 = p2; p2 = t; }
                    return new LineCurve(p1, p2);
                }
                return null;
            }

            if (tc.BasisCurve is IIfcCircle circle)
            {
                var plane = PlaneFromAxis2(circle.Position);
                double r = (double)circle.Radius;
                if (r <= 0) return null;
                if (!TryGetTrimAngles(tc, plane, r, preferParam, out double a1, out double a2))
                    return null;
                return ArcOnPlane(plane, r, a1, a2, tc.SenseAgreement);
            }

            if (tc.BasisCurve is IIfcEllipse ellipse)
            {
                var plane = PlaneFromAxis2(ellipse.Position);
                double a = (double)ellipse.SemiAxis1;
                double b = (double)ellipse.SemiAxis2;
                if (a <= 0 || b <= 0) return null;
                if (!TryGetTrimEllipseAngles(tc, plane, a, b, preferParam, out double t1, out double t2))
                    return null;
                return EllipticalArcOnPlane(plane, a, b, t1, t2, tc.SenseAgreement);
            }

            // Generic fallback: convert the basis curve and trim by parameter.
            var basis = Convert(tc.BasisCurve, ctx);
            if (basis == null) return null;
            if (TryGetParameterTrim(tc, out double pt1, out double pt2))
            {
                Curve trimmed = basis.Trim(pt1, pt2);
                if (trimmed != null && !tc.SenseAgreement) trimmed.Reverse();
                return trimmed ?? basis;
            }
            return basis;
        }

        // ---- IIfcCompositeCurve --------------------------------------------

        private static Curve ConvertCompositeCurve(IIfcCompositeCurve composite, ConversionContext ctx)
        {
            var segments = composite.Segments;
            if (segments == null || segments.Count == 0) return null;

            var pieces = new List<Curve>();
            foreach (var seg in segments)
            {
                if (seg.ParentCurve == null) continue;
                var c = Convert(seg.ParentCurve, ctx);
                if (c == null) continue;
                if (!seg.SameSense) c.Reverse();
                pieces.Add(c);
            }

            if (pieces.Count == 0) return null;
            if (pieces.Count == 1) return pieces[0];

            var joined = Curve.JoinCurves(pieces, 1e-4);
            if (joined != null && joined.Length > 0) return joined[0];

            // Fallback: build a PolyCurve manually.
            var poly = new PolyCurve();
            foreach (var c in pieces) poly.Append(c);
            return poly;
        }

        // ---- IIfcIndexedPolyCurve ------------------------------------------

        private static Curve ConvertIndexedPolyCurve(IIfcIndexedPolyCurve curve)
        {
            var coordinates = ExtractCoordinates(curve.Points);
            if (coordinates == null || coordinates.Count < 2) return null;

            var segments = curve.Segments;
            if (segments == null || segments.Count == 0)
            {
                // No segments: treat the points as a single polyline.
                return new PolylineCurve(coordinates);
            }

            var pieces = new List<Curve>();
            foreach (IIfcSegmentIndexSelect seg in segments)
            {
                // The .Value is a List<IfcPositiveInteger>; the indices are 1-based.
                if (!(seg is IExpressValueType vt)) continue;
                var raw = vt.Value as System.Collections.IList;
                if (raw == null || raw.Count == 0) continue;

                if (seg is IfcArcIndex)
                {
                    if (raw.Count < 3) continue;
                    int i0 = ToIntIndex(raw[0]) - 1;
                    int i1 = ToIntIndex(raw[1]) - 1;
                    int i2 = ToIntIndex(raw[2]) - 1;
                    if (!ValidIdx(i0, coordinates.Count) || !ValidIdx(i1, coordinates.Count) || !ValidIdx(i2, coordinates.Count))
                        continue;
                    var arc = new Arc(coordinates[i0], coordinates[i1], coordinates[i2]);
                    if (arc.IsValid) pieces.Add(new ArcCurve(arc));
                    else pieces.Add(new PolylineCurve(new[] { coordinates[i0], coordinates[i1], coordinates[i2] }));
                }
                else // IfcLineIndex (or other linear)
                {
                    var line = new List<Point3d>(raw.Count);
                    for (int k = 0; k < raw.Count; k++)
                    {
                        int idx = ToIntIndex(raw[k]) - 1;
                        if (!ValidIdx(idx, coordinates.Count)) { line.Clear(); break; }
                        line.Add(coordinates[idx]);
                    }
                    if (line.Count >= 2) pieces.Add(new PolylineCurve(line));
                }
            }

            if (pieces.Count == 0) return null;
            if (pieces.Count == 1) return pieces[0];

            var joined = Curve.JoinCurves(pieces, 1e-4);
            if (joined != null && joined.Length > 0) return joined[0];

            var poly = new PolyCurve();
            foreach (var c in pieces) poly.Append(c);
            return poly;
        }

        // ---- B-spline curves -----------------------------------------------

        private static NurbsCurve ConvertBSpline(IIfcBSplineCurveWithKnots bs, IItemSet<IfcReal> weights)
        {
            int degree = (int)(long)bs.Degree;
            var ctrlPts = bs.ControlPointsList;
            if (ctrlPts == null || ctrlPts.Count <= degree) return null;

            int n = ctrlPts.Count;
            bool rational = weights != null && weights.Count == n;

            var nurbs = new NurbsCurve(3, rational, degree + 1, n);

            var weightList = rational ? weights.Select(w => (double)w).ToList() : null;
            int i = 0;
            foreach (var cp in ctrlPts)
            {
                var p = ToPoint3d(cp);
                if (rational)
                    nurbs.Points.SetPoint(i, p.X, p.Y, p.Z, weightList[i]);
                else
                    nurbs.Points.SetPoint(i, p);
                i++;
            }

            // Build the full Rhino knot vector by expanding multiplicities.
            // IFC stores M+1 distinct knots and a parallel multiplicity list;
            // Rhino expects a flat clamped knot vector of length n + degree - 1
            // for non-periodic curves.
            var ifcKnots = bs.Knots.Select(k => (double)k).ToList();
            var mults = bs.KnotMultiplicities.Select(m => (int)(long)m).ToList();
            var expanded = new List<double>();
            for (int k = 0; k < ifcKnots.Count; k++)
            {
                int mu = k < mults.Count ? mults[k] : 1;
                for (int j = 0; j < mu; j++) expanded.Add(ifcKnots[k]);
            }

            // Rhino's internal knot vector excludes the first and last
            // multiplicity-1 knots (length = n + degree - 1).
            int expectedRhino = n + degree - 1;
            if (expanded.Count == expectedRhino + 2)
                expanded = expanded.GetRange(1, expectedRhino);
            if (expanded.Count != expectedRhino) return null;

            for (int k = 0; k < expanded.Count; k++)
                nurbs.Knots[k] = expanded[k];

            return nurbs.IsValid ? nurbs : null;
        }

        // ---- helpers --------------------------------------------------------

        private static List<Point3d> ExtractCoordinates(IIfcCartesianPointList list)
        {
            var result = new List<Point3d>();
            if (list is IIfcCartesianPointList3D list3)
            {
                foreach (var coord in list3.CoordList)
                {
                    var arr = coord.Select(v => (double)v).ToArray();
                    double x = arr.Length > 0 ? arr[0] : 0;
                    double y = arr.Length > 1 ? arr[1] : 0;
                    double z = arr.Length > 2 ? arr[2] : 0;
                    result.Add(new Point3d(x, y, z));
                }
            }
            else if (list is IIfcCartesianPointList2D list2)
            {
                foreach (var coord in list2.CoordList)
                {
                    var arr = coord.Select(v => (double)v).ToArray();
                    double x = arr.Length > 0 ? arr[0] : 0;
                    double y = arr.Length > 1 ? arr[1] : 0;
                    result.Add(new Point3d(x, y, 0));
                }
            }
            return result;
        }

        private static int ToIntIndex(object o)
        {
            if (o == null) return 0;
            if (o is IfcPositiveInteger pi) return (int)(long)pi;
            if (o is IExpressValueType vt && vt.Value != null) return ToIntIndex(vt.Value);
            if (o is long l) return (int)l;
            if (o is int ii) return ii;
            try { return System.Convert.ToInt32(o); } catch { return 0; }
        }

        private static bool ValidIdx(int idx, int count) => idx >= 0 && idx < count;

        internal static Point3d ToPoint3d(IIfcCartesianPoint p)
        {
            double x = SafeDouble(p.X);
            double y = SafeDouble(p.Y);
            double z = SafeDouble(p.Z);
            return new Point3d(x, y, z);
        }

        private static double SafeDouble(double v) =>
            double.IsNaN(v) || double.IsInfinity(v) ? 0.0 : v;

        private static Plane PlaneFromAxis2(IIfcAxis2Placement placement)
        {
            var t = PlacementConverter.ConvertAxis2Placement(placement);
            var origin = new Point3d(t.M03, t.M13, t.M23);
            var x = new Vector3d(t.M00, t.M10, t.M20);
            var y = new Vector3d(t.M01, t.M11, t.M21);
            return new Plane(origin, x, y);
        }

        private static bool TryGetTrimPoints(IIfcTrimmedCurve tc, IIfcLine line, out Point3d p1, out Point3d p2)
        {
            p1 = Point3d.Origin; p2 = Point3d.Origin;
            var ptA = TrimAsPoint(tc.Trim1);
            var ptB = TrimAsPoint(tc.Trim2);
            if (ptA.HasValue && ptB.HasValue) { p1 = ptA.Value; p2 = ptB.Value; return true; }

            // Parameter form: t -> Pnt + t * (Magnitude * Orientation)
            var origin = ToPoint3d(line.Pnt);
            var dir = new Vector3d(
                SafeDouble(line.Dir.Orientation.X),
                SafeDouble(line.Dir.Orientation.Y),
                SafeDouble(line.Dir.Orientation.Z));
            double mag = (double)line.Dir.Magnitude;
            if (mag == 0) mag = 1;

            double? a = TrimAsParameter(tc.Trim1);
            double? b = TrimAsParameter(tc.Trim2);
            if (!a.HasValue || !b.HasValue) return false;
            p1 = origin + dir * (mag * a.Value);
            p2 = origin + dir * (mag * b.Value);
            return true;
        }

        private static bool TryGetTrimAngles(IIfcTrimmedCurve tc, Plane plane, double radius, bool preferParam, out double a1, out double a2)
        {
            a1 = 0; a2 = 0;
            double? p1 = TrimAsParameter(tc.Trim1);
            double? p2 = TrimAsParameter(tc.Trim2);
            var pt1 = TrimAsPoint(tc.Trim1);
            var pt2 = TrimAsPoint(tc.Trim2);

            if (preferParam && p1.HasValue && p2.HasValue) { a1 = ToRad(p1.Value); a2 = ToRad(p2.Value); return true; }
            if (pt1.HasValue && pt2.HasValue)
            {
                a1 = AngleOnCircle(plane, pt1.Value);
                a2 = AngleOnCircle(plane, pt2.Value);
                return true;
            }
            if (p1.HasValue && p2.HasValue) { a1 = ToRad(p1.Value); a2 = ToRad(p2.Value); return true; }
            return false;
        }

        private static bool TryGetTrimEllipseAngles(IIfcTrimmedCurve tc, Plane plane, double a, double b, bool preferParam, out double t1, out double t2)
        {
            // For ellipses we approximate parameter as the same angle convention.
            return TryGetTrimAngles(tc, plane, a, preferParam, out t1, out t2);
        }

        private static bool TryGetParameterTrim(IIfcTrimmedCurve tc, out double a, out double b)
        {
            a = 0; b = 0;
            double? p1 = TrimAsParameter(tc.Trim1);
            double? p2 = TrimAsParameter(tc.Trim2);
            if (p1.HasValue && p2.HasValue) { a = p1.Value; b = p2.Value; return true; }
            return false;
        }

        private static double? TrimAsParameter(IItemSet<IIfcTrimmingSelect> trim)
        {
            if (trim == null) return null;
            foreach (var item in trim)
            {
                if (item is Xbim.Ifc4.MeasureResource.IfcParameterValue pv) return (double)pv;
                if (item is IExpressValueType vt && vt.Value is double d) return d;
            }
            return null;
        }

        private static Point3d? TrimAsPoint(IItemSet<IIfcTrimmingSelect> trim)
        {
            if (trim == null) return null;
            foreach (var item in trim)
            {
                if (item is IIfcCartesianPoint cp) return ToPoint3d(cp);
            }
            return null;
        }

        private static double ToRad(double degrees) => degrees * Math.PI / 180.0;

        private static double AngleOnCircle(Plane plane, Point3d pt)
        {
            plane.ClosestParameter(pt, out double u, out double v);
            return Math.Atan2(v, u);
        }

        private static Curve ArcOnPlane(Plane plane, double radius, double a1, double a2, bool sense)
        {
            const double twoPi = 2.0 * Math.PI;
            double sweep = a2 - a1;
            while (sweep <= 0) sweep += twoPi;
            if (sweep > twoPi) sweep -= twoPi;
            if (Math.Abs(sweep - twoPi) < 1e-9 || sweep == 0)
                return new Circle(plane, radius).ToNurbsCurve();

            var startPt = plane.PointAt(radius * Math.Cos(a1), radius * Math.Sin(a1));
            double mid = a1 + sweep * 0.5;
            var midPt = plane.PointAt(radius * Math.Cos(mid), radius * Math.Sin(mid));
            var endPt = plane.PointAt(radius * Math.Cos(a1 + sweep), radius * Math.Sin(a1 + sweep));
            var arc = new Arc(startPt, midPt, endPt);
            var ac = new ArcCurve(arc);
            if (!sense) ac.Reverse();
            return ac;
        }

        private static Curve EllipticalArcOnPlane(Plane plane, double a, double b, double t1, double t2, bool sense)
        {
            var ellipse = new Ellipse(plane, a, b).ToNurbsCurve();
            if (ellipse == null) return null;
            // Map parameter angles to NURBS parameters and trim.
            // The Rhino NURBS parameter for an ellipse is in [0, 2*PI] for default
            // construction; we trim by parameter values matching the IFC angles.
            if (!sense) { var t = t1; t1 = t2; t2 = t; }
            const double twoPi = 2.0 * Math.PI;
            while (t1 < 0) t1 += twoPi;
            while (t2 < t1) t2 += twoPi;
            return ellipse.Trim(t1, t2) ?? ellipse;
        }
    }
}
