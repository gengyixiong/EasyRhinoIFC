using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Common.XbimExtensions;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;
using RhinoIfc.Util;

namespace GH_RhinoIfc
{
    public class IfcImportComponent : GH_Component
    {
        public IfcImportComponent()
            : base("IFC Import", "IfcIn",
                "Import geometry and metadata from an IFC file",
                "RhinoIfc", "IFC")
        { }

        public override Guid ComponentGuid => new Guid("C9160020-5CE1-4E00-A000-C6A1A19BA101");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("File Path", "F", "Path to IFC file", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Meshes", "M", "Imported meshes grouped by storey", GH_ParamAccess.tree);
            pManager.AddTextParameter("Names", "N", "Element names", GH_ParamAccess.tree);
            pManager.AddTextParameter("IFC Classes", "C", "IFC class names", GH_ParamAccess.tree);
            pManager.AddTextParameter("Properties", "P", "Property dictionaries as key=value lines", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = null;
            if (!DA.GetData(0, ref filePath)) return;
            if (!File.Exists(filePath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"File not found: {filePath}");
                return;
            }

            var meshTree = new GH_Structure<GH_Mesh>();
            var nameTree = new GH_Structure<GH_String>();
            var classTree = new GH_Structure<GH_String>();
            var propTree = new GH_Structure<GH_String>();

            try
            {
                using (var model = IfcStore.Open(filePath))
                {
                    var context = new Xbim3DModelContext(model);
                    context.CreateContext();

                    double scaleFactor = UnitConverter.MetresToRhino(
                        Rhino.RhinoDoc.ActiveDoc?.ModelUnitSystem ?? Rhino.UnitSystem.Meters);

                    // Group elements by storey for DataTree branches
                    var storeyMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    int nextBranch = 0;

                    foreach (var si in context.ShapeInstances())
                    {
                        if (si.RepresentationType == XbimGeometryRepresentationType.OpeningsAndAdditionsExcluded)
                            continue;

                        var product = model.Instances[si.IfcProductLabel] as IIfcProduct;
                        if (product == null) continue;
                        if (product is IIfcSpatialStructureElement) continue;
                        if (product is IIfcSpace) continue;

                        var shapeGeom = context.ShapeGeometry(si);
                        if (shapeGeom.Format != XbimGeometryType.PolyhedronBinary) continue;

                        var shapeData = ((IXbimShapeGeometryData)shapeGeom).ShapeData;
                        if (shapeData == null || shapeData.Length == 0) continue;

                        Mesh rhinoMesh;
                        try
                        {
                            rhinoMesh = ConvertMesh(shapeData, si.Transformation, scaleFactor);
                        }
                        catch
                        {
                            continue;
                        }
                        if (rhinoMesh == null || rhinoMesh.Vertices.Count == 0) continue;

                        // Determine storey for DataTree branch
                        string storeyName = GetStoreyName(product) ?? "Default";
                        if (!storeyMap.TryGetValue(storeyName, out int branchIdx))
                        {
                            branchIdx = nextBranch++;
                            storeyMap[storeyName] = branchIdx;
                        }
                        var path = new GH_Path(branchIdx);

                        meshTree.Append(new GH_Mesh(rhinoMesh), path);
                        nameTree.Append(new GH_String(product.Name?.ToString() ?? ""), path);
                        classTree.Append(new GH_String(product.ExpressType.ExpressName), path);

                        // Collect properties as key=value lines
                        var props = new List<string>();
                        props.Add($"IFC_GlobalId={product.GlobalId}");
                        props.Add($"IFC_Name={product.Name}");

                        var relDefines = product.IsDefinedBy?.OfType<IIfcRelDefinesByProperties>()
                            ?? Enumerable.Empty<IIfcRelDefinesByProperties>();
                        foreach (var relDef in relDefines)
                        {
                            if (relDef.RelatingPropertyDefinition is IIfcPropertySet pset)
                            {
                                string psetName = pset.Name?.ToString() ?? "Properties";
                                foreach (var prop in pset.HasProperties.OfType<IIfcPropertySingleValue>())
                                {
                                    props.Add($"{psetName}.{prop.Name}={prop.NominalValue}");
                                }
                            }
                        }

                        propTree.Append(new GH_String(string.Join("\n", props)), path);
                    }
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            DA.SetDataTree(0, meshTree);
            DA.SetDataTree(1, nameTree);
            DA.SetDataTree(2, classTree);
            DA.SetDataTree(3, propTree);
        }

        private static string GetStoreyName(IIfcProduct product)
        {
            var container = product.IsContainedIn;
            if (container is IIfcBuildingStorey storey)
                return storey.Name?.ToString();
            return null;
        }

        private static Mesh ConvertMesh(byte[] shapeData, XbimMatrix3D transform, double scaleFactor)
        {
            XbimShapeTriangulation triangulation;
            using (var ms = new MemoryStream(shapeData))
            using (var reader = new BinaryReader(ms))
            {
                triangulation = reader.ReadShapeTriangulation();
            }

            if (triangulation?.Vertices == null || triangulation.Vertices.Count == 0)
                return null;

            var mesh = new Mesh();

            foreach (var v in triangulation.Vertices)
            {
                var wp = transform.Transform(v);
                mesh.Vertices.Add(wp.X * scaleFactor, wp.Y * scaleFactor, wp.Z * scaleFactor);
            }

            foreach (var faceGroup in triangulation.Faces)
            {
                var indices = faceGroup.Indices;
                for (int i = 0; i + 2 < indices.Count; i += 3)
                {
                    mesh.Faces.AddFace(indices[i], indices[i + 1], indices[i + 2]);
                }
            }

            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }
    }
}
