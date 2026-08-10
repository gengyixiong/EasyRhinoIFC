using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhinoIfc.Export
{
    /// <summary>
    /// Extracts exportable meshes from ordinary Rhino objects and block instances.
    /// Each top-level instance is flattened to world coordinates while preserving
    /// the existing one-Rhino-object-to-one-IFC-element export model.
    /// </summary>
    internal static class InstanceMeshExtractor
    {
        public static Mesh[] Extract(RhinoObject obj)
        {
            if (obj == null) return null;

            var meshes = new System.Collections.Generic.List<Mesh>();
            InstanceGraphTraversal.Traverse<RhinoObject, Transform, int>(
                obj,
                Transform.Identity,
                node => node is InstanceObject,
                node => ((InstanceObject)node).InstanceDefinition.Index,
                node =>
                {
                    var definition = ((InstanceObject)node).InstanceDefinition;
                    return definition == null || definition.IsDeleted
                        ? null
                        : definition.GetObjects();
                },
                node => ((InstanceObject)node).InstanceXform,
                CombineTransforms,
                (leaf, transform) => AppendMeshes(leaf, transform, meshes));

            return meshes.Count == 0 ? null : meshes.ToArray();
        }

        internal static Transform CombineTransforms(Transform parentTransform, Transform instanceTransform)
        {
            // Rhino applies the right-hand transform first. Definition geometry is
            // transformed by the nested instance, then by every enclosing instance.
            return parentTransform * instanceTransform;
        }

        internal static bool ReversesOrientation(Transform transform)
        {
            return transform.Determinant < 0;
        }

        private static void AppendMeshes(
            RhinoObject obj,
            Transform accumulatedTransform,
            System.Collections.Generic.List<Mesh> result)
        {
            if (obj == null) return;

            var meshes = CreateMeshes(obj.Geometry);
            if (meshes == null) return;

            foreach (var mesh in meshes)
            {
                if (mesh == null) continue;

                if (!accumulatedTransform.IsIdentity)
                {
                    if (!mesh.Transform(accumulatedTransform))
                        continue;

                    // A reflected instance reverses face winding. Restore outward
                    // orientation before writing the IFC closed shell.
                    if (ReversesOrientation(accumulatedTransform))
                        mesh.Flip(true, true, true);
                }

                CleanMesh(mesh);
                if (mesh.Vertices.Count > 0 && mesh.Faces.Count > 0)
                    result.Add(mesh);
            }
        }

        private static Mesh[] CreateMeshes(GeometryBase geometry)
        {
            if (geometry == null || geometry is Point || geometry is TextDot ||
                geometry is AnnotationBase || geometry is Light)
                return null;

            if (geometry is Mesh mesh)
                return new[] { mesh.DuplicateMesh() };

            if (geometry is Brep brep)
                return Mesh.CreateFromBrep(brep, MeshingParameters.Default);

            if (geometry is Extrusion extrusion)
            {
                var extrusionBrep = extrusion.ToBrep();
                return extrusionBrep == null
                    ? null
                    : Mesh.CreateFromBrep(extrusionBrep, MeshingParameters.Default);
            }

            if (geometry is SubD subd)
            {
                var subdBrep = subd.ToBrep(SubDToBrepOptions.Default);
                return subdBrep == null
                    ? null
                    : Mesh.CreateFromBrep(subdBrep, MeshingParameters.Default);
            }

            return null;
        }

        private static void CleanMesh(Mesh mesh)
        {
            mesh.Vertices.CombineIdentical(true, true);
            mesh.Faces.CullDegenerateFaces();
        }
    }
}
