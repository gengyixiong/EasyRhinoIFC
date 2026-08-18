using System.Linq;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhinoIfc.Export
{
    internal sealed class ExportGeometry
    {
        public ExportGeometry(RhinoObject sourceObject, Mesh[] meshes)
        {
            SourceObject = sourceObject;
            Meshes = meshes;
        }

        public RhinoObject SourceObject { get; }
        public Mesh[] Meshes { get; }
    }

    /// <summary>
    /// Extracts exportable meshes from ordinary Rhino objects and block instances.
    /// Each top-level instance is flattened to world coordinates while retaining
    /// the leaf object that supplies each geometry group's appearance.
    /// </summary>
    internal static class InstanceMeshExtractor
    {
        public static ExportGeometry[] Extract(RhinoObject obj)
        {
            if (obj == null) return null;

            var geometry = new System.Collections.Generic.List<ExportGeometry>();
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
                (leaf, transform) => AppendGeometry(leaf, transform, geometry));

            return geometry.Count == 0 ? null : geometry.ToArray();
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

        private static void AppendGeometry(
            RhinoObject obj,
            Transform accumulatedTransform,
            System.Collections.Generic.List<ExportGeometry> result)
        {
            if (obj == null) return;

            var meshes = CreateMeshes(obj);
            if (meshes == null) return;

            var validMeshes = new System.Collections.Generic.List<Mesh>();

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
                    validMeshes.Add(mesh);
                else
                    mesh.Dispose();
            }

            if (validMeshes.Count > 0)
                result.Add(new ExportGeometry(obj, validMeshes.ToArray()));
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
