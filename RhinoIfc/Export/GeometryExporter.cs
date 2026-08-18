using System;
using System.Linq;
using Rhino.Geometry;
using Xbim.Ifc;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.RepresentationResource;

namespace RhinoIfc.Export
{
    /// <summary>
    /// Converts Rhino meshes into indexed IFC triangulated face sets.
    /// </summary>
    public static class GeometryExporter
    {
        public static IfcShapeRepresentation CreatePlanarBrepRepresentation(
            IfcStore model,
            IIfcGeometricRepresentationContext context,
            Brep brep,
            double unitScale,
            double tolerance)
        {
            if (brep == null || !brep.IsValid || brep.Vertices.Count < 3 ||
                brep.Faces.Count == 0 ||
                brep.Faces.Any(face => !face.IsPlanar(tolerance)) ||
                brep.Edges.Any(edge => !edge.IsLinear(tolerance)))
                return null;

            foreach (var face in brep.Faces)
            {
                if (face.OuterLoop == null || face.Loops.Any(loop =>
                    (loop.LoopType != BrepLoopType.Outer && loop.LoopType != BrepLoopType.Inner) ||
                    loop.Trims.Count < 3 || loop.Trims.Any(trim => trim.Edge == null || trim.StartVertex == null)))
                    return null;
            }

            var coordinates = model.Instances.New<IfcCartesianPointList3D>();
            foreach (var vertex in brep.Vertices)
            {
                var point = coordinates.CoordList.GetAt(coordinates.CoordList.Count);
                point.Add(Math.Round(vertex.Location.X * unitScale, 5));
                point.Add(Math.Round(vertex.Location.Y * unitScale, 5));
                point.Add(Math.Round(vertex.Location.Z * unitScale, 5));
            }

            var faceSet = model.Instances.New<IfcPolygonalFaceSet>(fs =>
            {
                fs.Coordinates = coordinates;
                fs.Closed = brep.IsSolid;
            });

            foreach (var brepFace in brep.Faces)
            {
                var outer = GetLoopIndices(brepFace.OuterLoop, brepFace.OrientationIsReversed);
                var innerLoops = brepFace.Loops
                    .Where(loop => loop.LoopType == BrepLoopType.Inner)
                    .ToArray();

                IfcIndexedPolygonalFace face;
                if (innerLoops.Length == 0)
                {
                    face = model.Instances.New<IfcIndexedPolygonalFace>(f =>
                    {
                        foreach (var index in outer) f.CoordIndex.Add(index);
                    });
                }
                else
                {
                    face = model.Instances.New<IfcIndexedPolygonalFaceWithVoids>(f =>
                    {
                        foreach (var index in outer) f.CoordIndex.Add(index);
                        foreach (var loop in innerLoops)
                        {
                            var inner = f.InnerCoordIndices.GetAt(f.InnerCoordIndices.Count);
                            foreach (var index in GetLoopIndices(loop, brepFace.OrientationIsReversed))
                                inner.Add(index);
                        }
                    });
                }

                faceSet.Faces.Add(face);
            }

            return CreateShapeRepresentation(model, context, faceSet);
        }

        /// <summary>
        /// Create an IfcShapeRepresentation containing one indexed triangulated face set.
        /// </summary>
        /// <param name="model">xBIM model instance</param>
        /// <param name="context">The 3D geometric representation context</param>
        /// <param name="meshes">One or more Rhino meshes to combine</param>
        /// <param name="unitScale">Factor to convert from Rhino units to IFC metres</param>
        public static IfcShapeRepresentation CreateRepresentation(
            IfcStore model,
            IIfcGeometricRepresentationContext context,
            Mesh[] meshes,
            double unitScale)
        {
            var faceSet = CreateTriangulatedFaceSet(model, meshes, unitScale);
            return faceSet == null ? null : CreateShapeRepresentation(model, context, faceSet);
        }

        internal static IfcShapeRepresentation CreateRepresentation(
            IfcStore model,
            IIfcGeometricRepresentationContext context,
            ExportGeometry[] geometry,
            double unitScale)
        {
            if (geometry == null) return null;

            var items = geometry
                .Select(g => CreateTriangulatedFaceSet(model, g.Meshes, unitScale))
                .Where(item => item != null)
                .Cast<IfcTessellatedItem>()
                .ToArray();

            return items.Length == 0 ? null : CreateShapeRepresentation(model, context, items);
        }

        private static IfcTriangulatedFaceSet CreateTriangulatedFaceSet(
            IfcStore model,
            Mesh[] meshes,
            double unitScale)
        {
            if (meshes == null) return null;

            var validMeshes = meshes
                .Where(m => m != null && m.Vertices.Count > 0 && m.Faces.Count > 0)
                .ToArray();
            if (validMeshes.Length == 0) return null;

            var coordinates = model.Instances.New<IfcCartesianPointList3D>();
            var faceSet = model.Instances.New<IfcTriangulatedFaceSet>(fs =>
            {
                fs.Coordinates = coordinates;
                fs.Closed = validMeshes.All(m => m.IsClosed);
            });

            int vertexOffset = 0;

            foreach (var mesh in validMeshes)
            {
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    var v = mesh.Vertices[i];
                    var point = coordinates.CoordList.GetAt(coordinates.CoordList.Count);
                    point.Add(Math.Round(v.X * unitScale, 5));
                    point.Add(Math.Round(v.Y * unitScale, 5));
                    point.Add(Math.Round(v.Z * unitScale, 5));
                }

                for (int i = 0; i < mesh.Faces.Count; i++)
                {
                    var mf = mesh.Faces[i];
                    AddTriangle(faceSet, vertexOffset + mf.A + 1,
                        vertexOffset + mf.B + 1, vertexOffset + mf.C + 1);
                    if (mf.IsQuad)
                        AddTriangle(faceSet, vertexOffset + mf.A + 1,
                            vertexOffset + mf.C + 1, vertexOffset + mf.D + 1);
                }

                vertexOffset += mesh.Vertices.Count;
            }

            return faceSet;
        }

        private static IfcShapeRepresentation CreateShapeRepresentation(
            IfcStore model,
            IIfcGeometricRepresentationContext context,
            params IfcTessellatedItem[] items)
        {
            return model.Instances.New<IfcShapeRepresentation>(sr =>
            {
                sr.ContextOfItems = (IfcRepresentationContext)context;
                sr.RepresentationIdentifier = "Body";
                sr.RepresentationType = "Tessellation";
                foreach (var item in items) sr.Items.Add(item);
            });
        }

        private static int[] GetLoopIndices(BrepLoop loop, bool reverse)
        {
            var indices = loop.Trims.Select(trim => trim.StartVertex.VertexIndex + 1).ToArray();
            if (reverse) Array.Reverse(indices);
            return indices;
        }

        private static void AddTriangle(IfcTriangulatedFaceSet faceSet, int a, int b, int c)
        {
            var triangle = faceSet.CoordIndex.GetAt(faceSet.CoordIndex.Count);
            triangle.Add(a);
            triangle.Add(b);
            triangle.Add(c);
        }
    }
}
