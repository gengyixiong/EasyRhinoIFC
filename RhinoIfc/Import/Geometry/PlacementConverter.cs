using System.Collections.Generic;
using Rhino.Geometry;
using Xbim.Ifc4.Interfaces;

namespace RhinoIfc.Import.Geometry
{
    /// <summary>
    /// Converts IFC placement entities into Rhino <see cref="Transform"/> matrices.
    /// All output is in IFC-native units (metres). The unit scale is applied once,
    /// at the very end of the product import, by <see cref="IfcGeometryConverter"/>.
    /// </summary>
    internal static class PlacementConverter
    {
        /// <summary>
        /// Build a Rhino Transform from an IfcAxis2Placement3D
        /// (origin + Z axis + X axis). Defaults follow IFC convention:
        /// Z = (0,0,1), X = (1,0,0), Y = Z x X.
        /// </summary>
        public static Transform ConvertAxis2Placement3D(IIfcAxis2Placement3D placement)
        {
            if (placement == null) return Transform.Identity;

            var origin = placement.Location != null
                ? ToPoint(placement.Location)
                : Point3d.Origin;

            var z = placement.Axis != null
                ? ToVector(placement.Axis)
                : Vector3d.ZAxis;

            var x = placement.RefDirection != null
                ? ToVector(placement.RefDirection)
                : Vector3d.XAxis;

            if (!z.Unitize()) z = Vector3d.ZAxis;

            // Project X onto the plane perpendicular to Z, then unitize.
            x = x - (x * z) * z;
            if (!x.Unitize()) x = PerpendicularTo(z);

            var y = Vector3d.CrossProduct(z, x);
            if (!y.Unitize()) y = Vector3d.CrossProduct(z, x);

            return TransformFromBasis(origin, x, y, z);
        }

        /// <summary>
        /// Build a Rhino Transform from an IfcAxis2Placement2D
        /// (origin + X axis on the XY plane). Z is implicitly +Z.
        /// </summary>
        public static Transform ConvertAxis2Placement2D(IIfcAxis2Placement2D placement)
        {
            if (placement == null) return Transform.Identity;

            var origin = placement.Location != null
                ? new Point3d(SafeDouble(placement.Location.X), SafeDouble(placement.Location.Y), 0.0)
                : Point3d.Origin;

            Vector3d x;
            if (placement.RefDirection != null)
            {
                var d = placement.RefDirection;
                x = new Vector3d(SafeDouble(d.X), SafeDouble(d.Y), 0.0);
            }
            else
            {
                x = Vector3d.XAxis;
            }

            if (!x.Unitize()) x = Vector3d.XAxis;

            var z = Vector3d.ZAxis;
            var y = Vector3d.CrossProduct(z, x);
            y.Unitize();

            return TransformFromBasis(origin, x, y, z);
        }

        /// <summary>
        /// Generic dispatcher: handles either IfcAxis2Placement3D or
        /// IfcAxis2Placement2D wrapped in the abstract IIfcAxis2Placement.
        /// </summary>
        public static Transform ConvertAxis2Placement(IIfcAxis2Placement placement)
        {
            if (placement is IIfcAxis2Placement3D p3) return ConvertAxis2Placement3D(p3);
            if (placement is IIfcAxis2Placement2D p2) return ConvertAxis2Placement2D(p2);
            return Transform.Identity;
        }

        /// <summary>
        /// Walk the IfcLocalPlacement.PlacementRelTo chain from the leaf
        /// up to the root, accumulating transforms. The result is the
        /// world-space transform for that placement.
        /// </summary>
        public static Transform ConvertObjectPlacement(IIfcObjectPlacement placement)
        {
            if (placement == null) return Transform.Identity;

            // Collect chain leaf -> root.
            var chain = new List<IIfcLocalPlacement>();
            var current = placement as IIfcLocalPlacement;
            int safety = 0;
            while (current != null && safety++ < 1024)
            {
                chain.Add(current);
                current = current.PlacementRelTo as IIfcLocalPlacement;
            }

            // Multiply root -> leaf so the leaf transform applies last.
            var result = Transform.Identity;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var local = ConvertAxis2Placement(chain[i].RelativePlacement);
                result = result * local;
            }
            return result;
        }

        /// <summary>
        /// Convert an IfcCartesianTransformationOperator3D into a Rhino Transform,
        /// honouring uniform Scale and the per-axis Scale2/Scale3 fields when
        /// the operator is the non-uniform variant. Used by IfcMappedItem.MappingTarget.
        /// </summary>
        public static Transform ConvertCartesianTransformationOperator3D(
            IIfcCartesianTransformationOperator3D op)
        {
            if (op == null) return Transform.Identity;

            var origin = op.LocalOrigin != null
                ? ToPoint(op.LocalOrigin)
                : Point3d.Origin;

            var x = op.Axis1 != null ? ToVector(op.Axis1) : Vector3d.XAxis;
            var y = op.Axis2 != null ? ToVector(op.Axis2) : Vector3d.YAxis;
            var z = op.Axis3 != null ? ToVector(op.Axis3) : Vector3d.ZAxis;

            if (!x.Unitize()) x = Vector3d.XAxis;
            if (!y.Unitize()) y = Vector3d.YAxis;
            if (!z.Unitize()) z = Vector3d.CrossProduct(x, y);
            if (!z.Unitize()) z = Vector3d.ZAxis;

            // Per-axis scales. The base operator has only Scale (uniform);
            // the *NonUniform variant additionally has Scale2 and Scale3.
            // Scl/Scl2/Scl3 are derived doubles (IfcReal); cast explicitly.
            double s1 = (double)op.Scl;
            double s2 = s1;
            double s3 = s1;

            if (op is IIfcCartesianTransformationOperator3DnonUniform nu)
            {
                s2 = (double)nu.Scl2;
                s3 = (double)nu.Scl3;
            }

            var sx = x * s1;
            var sy = y * s2;
            var sz = z * s3;

            return TransformFromBasis(origin, sx, sy, sz);
        }

        // ---- internal helpers ----------------------------------------------

        private static Point3d ToPoint(IIfcCartesianPoint p)
        {
            // IIfcCartesianPoint.X/Y/Z return double.NaN if the underlying
            // Coordinates list has fewer entries (e.g. 2D points have Z=NaN).
            double x = SafeDouble(p.X);
            double y = SafeDouble(p.Y);
            double z = SafeDouble(p.Z);
            return new Point3d(x, y, z);
        }

        private static Vector3d ToVector(IIfcDirection d)
        {
            double x = SafeDouble(d.X);
            double y = SafeDouble(d.Y);
            double z = SafeDouble(d.Z);
            return new Vector3d(x, y, z);
        }

        private static double SafeDouble(double v)
        {
            return double.IsNaN(v) || double.IsInfinity(v) ? 0.0 : v;
        }

        private static Vector3d PerpendicularTo(Vector3d v)
        {
            // Pick the basis axis least parallel to v, cross with it.
            Vector3d candidate;
            double ax = System.Math.Abs(v.X);
            double ay = System.Math.Abs(v.Y);
            double az = System.Math.Abs(v.Z);
            if (ax <= ay && ax <= az) candidate = Vector3d.XAxis;
            else if (ay <= az) candidate = Vector3d.YAxis;
            else candidate = Vector3d.ZAxis;
            var perp = Vector3d.CrossProduct(v, candidate);
            perp.Unitize();
            return perp;
        }

        private static Transform TransformFromBasis(Point3d origin, Vector3d x, Vector3d y, Vector3d z)
        {
            // Column vectors are the basis; translation in the rightmost column.
            var t = Transform.Identity;
            t.M00 = x.X; t.M01 = y.X; t.M02 = z.X; t.M03 = origin.X;
            t.M10 = x.Y; t.M11 = y.Y; t.M12 = z.Y; t.M13 = origin.Y;
            t.M20 = x.Z; t.M21 = y.Z; t.M22 = z.Z; t.M23 = origin.Z;
            t.M30 = 0;   t.M31 = 0;   t.M32 = 0;   t.M33 = 1;
            return t;
        }
    }
}
