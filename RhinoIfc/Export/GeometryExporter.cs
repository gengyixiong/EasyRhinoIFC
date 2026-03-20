using System.Collections.Generic;
using Rhino.Geometry;
using Xbim.Ifc;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.RepresentationResource;
using Xbim.Ifc4.TopologyResource;

namespace RhinoIfc.Export
{
    /// <summary>
    /// Converts Rhino mesh geometry into IFC IfcFacetedBrep representations.
    ///
    /// IfcFacetedBrep is the simplest solid representation in IFC and is universally
    /// supported by all IFC viewers. It consists of:
    ///   IfcFacetedBrep → IfcClosedShell → [IfcFace → IfcFaceOuterBound → IfcPolyLoop → [IfcCartesianPoint]]
    /// </summary>
    public static class GeometryExporter
    {
        /// <summary>
        /// Create an IfcShapeRepresentation containing IfcFacetedBrep from Rhino meshes.
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
            var allFaces = new List<IfcFace>();

            foreach (var mesh in meshes)
            {
                if (mesh == null || mesh.Vertices.Count == 0) continue;

                // Build IFC cartesian points for all vertices in this mesh
                var points = new List<IfcCartesianPoint>(mesh.Vertices.Count);
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    var v = mesh.Vertices[i];
                    var pt = model.Instances.New<IfcCartesianPoint>(p =>
                    {
                        p.SetXYZ(v.X * unitScale, v.Y * unitScale, v.Z * unitScale);
                    });
                    points.Add(pt);
                }

                // Build IFC faces from mesh face list
                for (int i = 0; i < mesh.Faces.Count; i++)
                {
                    var mf = mesh.Faces[i];

                    // Create the polygon loop
                    var polyLoop = model.Instances.New<IfcPolyLoop>(pl =>
                    {
                        pl.Polygon.Add(points[mf.A]);
                        pl.Polygon.Add(points[mf.B]);
                        pl.Polygon.Add(points[mf.C]);
                        if (mf.IsQuad)
                            pl.Polygon.Add(points[mf.D]);
                    });

                    var outerBound = model.Instances.New<IfcFaceOuterBound>(b =>
                    {
                        b.Bound = polyLoop;
                        b.Orientation = true;
                    });

                    allFaces.Add(model.Instances.New<IfcFace>(f =>
                    {
                        f.Bounds.Add(outerBound);
                    }));
                }
            }

            if (allFaces.Count == 0) return null;

            // Closed shell from all faces
            var shell = model.Instances.New<IfcClosedShell>(s =>
            {
                foreach (var f in allFaces)
                    s.CfsFaces.Add(f);
            });

            // Faceted BRep
            var brep = model.Instances.New<IfcFacetedBrep>(b =>
            {
                b.Outer = shell;
            });

            // Wrap in shape representation
            return model.Instances.New<IfcShapeRepresentation>(sr =>
            {
                sr.ContextOfItems = (IfcRepresentationContext)context;
                sr.RepresentationIdentifier = "Body";
                sr.RepresentationType = "Brep";
                sr.Items.Add(brep);
            });
        }
    }
}
