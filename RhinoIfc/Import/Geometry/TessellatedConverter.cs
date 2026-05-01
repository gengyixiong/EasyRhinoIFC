using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.MeasureResource;

namespace RhinoIfc.Import.Geometry
{
    /// <summary>
    /// Converts indexed face-set tessellations (IfcPolygonalFaceSet and
    /// IfcTriangulatedFaceSet) into Rhino polysurfaces. Each IFC face is
    /// turned into a single planar Brep face; Brep.JoinBreps welds them.
    /// </summary>
    internal static class TessellatedConverter
    {
        public static Brep Convert(IIfcPolygonalFaceSet pfs, ConversionContext ctx)
        {
            if (pfs?.Coordinates == null) return null;

            try
            {
                var coords = ExtractPoints(pfs.Coordinates);
                if (coords.Count == 0) return null;

                var faces = new List<Brep>();
                foreach (var face in pfs.Faces)
                {
                    var faceBrep = ConvertPolygonalFace(face, coords, ctx.Tolerance);
                    if (faceBrep != null) faces.Add(faceBrep);
                }
                if (faces.Count == 0) return null;

                var joined = Brep.JoinBreps(faces, ctx.Tolerance);
                if (joined == null || joined.Length == 0) return null;
                return joined[0];
            }
            catch
            {
                return null;
            }
        }

        public static Brep Convert(IIfcTriangulatedFaceSet tfs, ConversionContext ctx)
        {
            if (tfs?.Coordinates == null) return null;

            try
            {
                var coords = ExtractPoints(tfs.Coordinates);
                if (coords.Count == 0) return null;

                var faces = new List<Brep>();
                foreach (var idxList in tfs.CoordIndex)
                {
                    var indices = idxList.Select(v => (int)(long)v).ToList();
                    if (indices.Count < 3) continue;
                    var pts = new List<Point3d>(4);
                    bool ok = true;
                    foreach (var i in indices)
                    {
                        int zero = i - 1;
                        if (zero < 0 || zero >= coords.Count) { ok = false; break; }
                        pts.Add(coords[zero]);
                    }
                    if (!ok) continue;
                    pts.Add(pts[0]);
                    var loop = new PolylineCurve(pts);
                    var planar = Brep.CreatePlanarBreps(loop, ctx.Tolerance);
                    if (planar != null && planar.Length > 0) faces.Add(planar[0]);
                }
                if (faces.Count == 0) return null;

                var joined = Brep.JoinBreps(faces, ctx.Tolerance);
                if (joined == null || joined.Length == 0) return null;
                return joined[0];
            }
            catch
            {
                return null;
            }
        }

        // ---- helpers --------------------------------------------------------

        private static Brep ConvertPolygonalFace(IIfcIndexedPolygonalFace face, List<Point3d> coords, double tol)
        {
            var outer = BuildLoop(face.CoordIndex, coords);
            if (outer == null) return null;

            var input = new List<Curve> { outer };

            if (face is IIfcIndexedPolygonalFaceWithVoids voids)
            {
                foreach (var inner in voids.InnerCoordIndices)
                {
                    var holeLoop = BuildLoop(inner, coords);
                    if (holeLoop != null) input.Add(holeLoop);
                }
            }

            var planar = Brep.CreatePlanarBreps(input, tol);
            if (planar == null || planar.Length == 0) return null;
            return planar[0];
        }

        private static PolylineCurve BuildLoop(IEnumerable<IfcPositiveInteger> indexList, List<Point3d> coords)
        {
            var pts = new List<Point3d>();
            foreach (var idx in indexList)
            {
                int i = (int)(long)idx - 1;
                if (i < 0 || i >= coords.Count) return null;
                pts.Add(coords[i]);
            }
            if (pts.Count < 3) return null;
            if (pts[0].DistanceTo(pts[pts.Count - 1]) > 1e-9)
                pts.Add(pts[0]);
            return new PolylineCurve(pts);
        }

        private static List<Point3d> ExtractPoints(IIfcCartesianPointList3D list)
        {
            var result = new List<Point3d>();
            if (list == null) return result;
            foreach (var coord in list.CoordList)
            {
                var arr = coord.Select(v => (double)v).ToArray();
                double x = arr.Length > 0 ? arr[0] : 0;
                double y = arr.Length > 1 ? arr[1] : 0;
                double z = arr.Length > 2 ? arr[2] : 0;
                result.Add(new Point3d(x, y, z));
            }
            return result;
        }
    }
}
