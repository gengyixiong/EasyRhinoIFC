using System.Linq;
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

            var meshes = CreateMeshes(obj);
            if (meshes == null) return;

            foreach (var mesh in meshes)
            {
                if (mesh == null) continue;

                if (!accumulatedTransform.IsIdentity)
                {
                    if (!mesh.Transform(accumulatedTransform))
                    {
                        mesh.Dispose();
                        continue;
                    }

                    // A reflected instance reverses face winding. Restore outward
                    // orientation before writing the IFC closed shell.
                    if (ReversesOrientation(accumulatedTransform))
                        mesh.Flip(true, true, true);
                }

                CleanMesh(mesh);
                if (mesh.Vertices.Count > 0 && mesh.Faces.Count > 0)
                    result.Add(mesh);
                else
                    mesh.Dispose();
            }
        }

        private static Mesh[] CreateMeshes(RhinoObject obj)
        {
            var geometry = obj?.Geometry;
            if (geometry == null || geometry is Point || geometry is TextDot ||
                geometry is AnnotationBase || geometry is Light)
                return null;

            if (geometry is Mesh mesh)
                return new[] { mesh.DuplicateMesh() };

            var parameters = obj.GetRenderMeshParameters() ?? MeshingParameters.FastRenderMesh;
            var renderMeshes = obj.GetMeshes(MeshType.Render);
            if ((renderMeshes == null || renderMeshes.Length == 0) &&
                obj.IsMeshable(MeshType.Render))
            {
                obj.CreateMeshes(MeshType.Render, parameters, false);
                renderMeshes = obj.GetMeshes(MeshType.Render);
            }

            if (renderMeshes != null && renderMeshes.Length > 0)
                return renderMeshes.Where(m => m != null).Select(m => m.DuplicateMesh()).ToArray();

            if (geometry is Brep brep)
                return Mesh.CreateFromBrep(brep, parameters);

            if (geometry is Extrusion extrusion)
            {
                using (var extrusionBrep = extrusion.ToBrep())
                    return extrusionBrep == null
                        ? null
                        : Mesh.CreateFromBrep(extrusionBrep, parameters);
            }

            if (geometry is SubD subd)
            {
                using (var subdBrep = subd.ToBrep(SubDToBrepOptions.Default))
                    return subdBrep == null
                        ? null
                        : Mesh.CreateFromBrep(subdBrep, parameters);
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
