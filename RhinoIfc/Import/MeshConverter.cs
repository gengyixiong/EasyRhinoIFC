using System.IO;
using Rhino.Geometry;
using Xbim.Common.Geometry;
using Xbim.Common.XbimExtensions;

namespace RhinoIfc.Import
{
    /// <summary>
    /// Converts xBIM's binary tessellated shape data into a Rhino Mesh,
    /// applying the instance's 4×4 placement transform and unit scaling.
    /// </summary>
    public static class MeshConverter
    {
        /// <summary>
        /// Decode binary shape data from xBIM geometry store into a Rhino Mesh.
        /// </summary>
        /// <param name="shapeData">Raw binary mesh blob from IXbimShapeGeometryData.ShapeData</param>
        /// <param name="transform">4×4 placement matrix from the ShapeInstance</param>
        /// <param name="scaleFactor">Multiplier from IFC metres to Rhino doc units</param>
        public static Mesh Convert(byte[] shapeData, XbimMatrix3D transform, double scaleFactor)
        {
            // Parse the binary mesh format
            XbimShapeTriangulation triangulation;
            using (var ms = new MemoryStream(shapeData))
            using (var reader = new BinaryReader(ms))
            {
                triangulation = reader.ReadShapeTriangulation();
            }

            if (triangulation == null || triangulation.Vertices == null || triangulation.Vertices.Count == 0)
                return null;

            var mesh = new Mesh();

            // Add vertices — transform to world space and scale to doc units
            foreach (var v in triangulation.Vertices)
            {
                // Apply the instance placement
                var wp = transform.Transform(v);
                mesh.Vertices.Add(
                    wp.X * scaleFactor,
                    wp.Y * scaleFactor,
                    wp.Z * scaleFactor);
            }

            // Add triangle faces from each face group
            foreach (var faceGroup in triangulation.Faces)
            {
                var indices = faceGroup.Indices;
                // Indices come in triplets: every 3 ints define one triangle
                for (int i = 0; i + 2 < indices.Count; i += 3)
                {
                    mesh.Faces.AddFace(indices[i], indices[i + 1], indices[i + 2]);
                }
            }

            // Finalize
            mesh.Normals.ComputeNormals();
            mesh.Compact();

            if (!mesh.IsValid)
                mesh.RebuildNormals();

            return mesh;
        }

        /// <summary>
        /// Transform an XbimPoint3D by an XbimMatrix3D.
        /// xBIM's matrix is column-major with translation in elements [3], [7], [11].
        /// </summary>
        private static XbimPoint3D Transform(this XbimMatrix3D m, XbimPoint3D p)
        {
            // XbimMatrix3D stores as flat array — access via OffsetX/Y/Z and M** properties
            return new XbimPoint3D(
                m.M11 * p.X + m.M12 * p.Y + m.M13 * p.Z + m.OffsetX,
                m.M21 * p.X + m.M22 * p.Y + m.M23 * p.Z + m.OffsetY,
                m.M31 * p.X + m.M32 * p.Y + m.M33 * p.Z + m.OffsetZ);
        }
    }
}
