using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Xbim.Common;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc4.GeometricConstraintResource;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.ProductExtension;
using Xbim.Ifc4.RepresentationResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.SharedBldgElements;
using Xbim.Ifc4.TopologyResource;
using Xbim.IO;
using RhinoIfc.Export;
using RhinoIfc.Util;

namespace GH_RhinoIfc
{
    public class IfcExportComponent : GH_Component
    {
        public IfcExportComponent()
            : base("IFC Export", "IfcOut",
                "Export meshes to an IFC file",
                "EasyRhinoIFC", "IFC")
        { }

        public override Guid ComponentGuid => new Guid("C9160020-5CE1-4E00-A000-C6A1A19BA102");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Meshes", "M", "Meshes to export", GH_ParamAccess.list);
            pManager.AddTextParameter("Names", "N", "Element names (one per mesh)", GH_ParamAccess.list);
            pManager.AddTextParameter("IFC Classes", "C", "IFC class names (optional, default IfcBuildingElementProxy)",
                GH_ParamAccess.list);
            pManager.AddTextParameter("File Path", "F", "Output IFC file path", GH_ParamAccess.item);

            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Result", "R", "Export result message", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var meshes = new List<GH_Mesh>();
            var names = new List<GH_String>();
            var classes = new List<GH_String>();
            string filePath = null;

            if (!DA.GetDataList(0, meshes)) return;
            DA.GetDataList(1, names);
            DA.GetDataList(2, classes);
            if (!DA.GetData(3, ref filePath)) return;

            if (meshes.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No meshes to export");
                return;
            }

            try
            {
                double unitScale = UnitConverter.RhinoToMetres(
                    Rhino.RhinoDoc.ActiveDoc?.ModelUnitSystem ?? Rhino.UnitSystem.Meters);

                var creds = new XbimEditorCredentials
                {
                    ApplicationDevelopersName = "EasyRhinoIFC",
                    ApplicationFullName = "EasyRhinoIFC GH",
                    ApplicationIdentifier = "EasyRhinoIFC",
                    ApplicationVersion = "0.2.2",
                    EditorsFamilyName = System.Environment.UserName,
                    EditorsGivenName = "",
                    EditorsOrganisationName = ""
                };

                int count = 0;
                using (var model = IfcStore.Create(creds, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel))
                {
                    IfcBuildingStorey storey;
                    IfcGeometricRepresentationContext geomContext;
                    using (var txn = model.BeginTransaction("Init"))
                    {
                        var project = model.Instances.New<IfcProject>(p =>
                        {
                            p.Name = "Grasshopper Export";
                            IfcProjectUnits.InitializeMetres(p);
                        });

                        var site = model.Instances.New<IfcSite>(s =>
                        {
                            s.Name = "Default Site";
                            s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                        });

                        var building = model.Instances.New<IfcBuilding>(b =>
                        {
                            b.Name = "Default Building";
                            b.CompositionType = IfcElementCompositionEnum.ELEMENT;
                        });

                        storey = model.Instances.New<IfcBuildingStorey>(s =>
                        {
                            s.Name = "Default Storey";
                            s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                            s.Elevation = 0;
                        });

                        model.Instances.New<IfcRelAggregates>(r =>
                        {
                            r.RelatingObject = project;
                            r.RelatedObjects.Add(site);
                        });
                        model.Instances.New<IfcRelAggregates>(r =>
                        {
                            r.RelatingObject = site;
                            r.RelatedObjects.Add(building);
                        });
                        model.Instances.New<IfcRelAggregates>(r =>
                        {
                            r.RelatingObject = building;
                            r.RelatedObjects.Add(storey);
                        });

                        geomContext = model.Instances.New<IfcGeometricRepresentationContext>(c =>
                        {
                            c.ContextType = "Model";
                            c.CoordinateSpaceDimension = 3;
                            c.Precision = 1e-5;
                            c.WorldCoordinateSystem = model.Instances.New<IfcAxis2Placement3D>(a =>
                            {
                                a.Location = model.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(0, 0, 0));
                            });
                        });

                        txn.Commit();
                    }

                    using (var txn2 = model.BeginTransaction("Elements"))
                    {
                        var containRel = model.Instances.New<IfcRelContainedInSpatialStructure>(r =>
                        {
                            r.RelatingStructure = storey;
                        });

                        for (int i = 0; i < meshes.Count; i++)
                        {
                            var ghMesh = meshes[i];
                            if (ghMesh?.Value == null) continue;

                            string name = (i < names.Count && names[i]?.Value != null)
                                ? names[i].Value : $"Element {i + 1}";

                            string ifcClass = (i < classes.Count && classes[i]?.Value != null)
                                ? classes[i].Value : "IfcBuildingElementProxy";

                            var element = IfcModelWriter.CreateElement(model, ifcClass, name);

                            var representation = GeometryExporter.CreateRepresentation(
                                model, geomContext, new[] { ghMesh.Value }, unitScale);

                            if (representation != null)
                            {
                                element.Representation = model.Instances.New<IfcProductDefinitionShape>(pds =>
                                {
                                    pds.Representations.Add(representation);
                                });
                            }

                            element.ObjectPlacement = model.Instances.New<IfcLocalPlacement>(lp =>
                            {
                                lp.RelativePlacement = model.Instances.New<IfcAxis2Placement3D>(a =>
                                {
                                    a.Location = model.Instances.New<IfcCartesianPoint>(p =>
                                        p.SetXYZ(0, 0, 0));
                                });
                            });

                            containRel.RelatedElements.Add(element);
                            count++;
                        }

                        txn2.Commit();
                    }

                    model.SaveAs(filePath, filePath.EndsWith(".ifczip", StringComparison.OrdinalIgnoreCase)
                        ? StorageType.IfcZip
                        : StorageType.Ifc);
                }

                DA.SetData(0, $"Exported {count} elements to {filePath}");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(0, $"Export failed: {ex.Message}");
            }
        }

    }
}
