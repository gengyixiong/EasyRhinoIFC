using System;
using System.IO;
using System.Linq;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Common.Step21;
using Xbim.Common.XbimExtensions;
using Xbim.Ifc;
using Xbim.Ifc4.GeometricConstraintResource;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.ProductExtension;
using Xbim.Ifc4.RepresentationResource;
using Xbim.Ifc4.SharedBldgElements;
using Xbim.Ifc4.TopologyResource;
using Xbim.IO;
using Xbim.ModelGeometry.Scene;

namespace RhinoIfc.Tests
{
    class Program
    {
        static void Main(string[] args)
        {
            string testFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_wall_slab.ifc");

            Console.WriteLine("=== Phase 1: Create minimal IFC file ===");
            CreateMinimalIfc(testFile);

            Console.WriteLine();
            Console.WriteLine("=== Phase 2: Re-open and tessellate ===");
            ReadAndTessellate(testFile);

            Console.WriteLine();
            Console.WriteLine("=== Phase 3: Validate spatial hierarchy ===");
            ValidateSpatialHierarchy(testFile);

            Console.WriteLine();
            Console.WriteLine("All tests passed.");
        }

        static void CreateMinimalIfc(string outputPath)
        {
            var creds = new XbimEditorCredentials
            {
                ApplicationDevelopersName = "RhinoIfc",
                ApplicationFullName = "RhinoIfc Test",
                ApplicationIdentifier = "RhinoIfc",
                ApplicationVersion = "0.1.0",
                EditorsFamilyName = "Test",
                EditorsGivenName = "",
                EditorsOrganisationName = ""
            };

            using (var model = IfcStore.Create(creds, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel))
            {
                using (var txn = model.BeginTransaction("Init"))
                {
                    var project = model.Instances.New<IfcProject>(p =>
                    {
                        p.Name = "Test Project";
                        p.Initialize(ProjectUnits.SIUnitsUK);
                    });

                    var site = model.Instances.New<IfcSite>(s =>
                    {
                        s.Name = "Test Site";
                        s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                    });

                    var building = model.Instances.New<IfcBuilding>(b =>
                    {
                        b.Name = "Test Building";
                        b.CompositionType = IfcElementCompositionEnum.ELEMENT;
                    });

                    var storey = model.Instances.New<IfcBuildingStorey>(s =>
                    {
                        s.Name = "Ground Floor";
                        s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                        s.Elevation = 0;
                    });

                    // Aggregate: Project -> Site -> Building -> Storey
                    Aggregate(model, project, site);
                    Aggregate(model, site, building);
                    Aggregate(model, building, storey);

                    // Geometry context
                    var ctx = model.Instances.New<IfcGeometricRepresentationContext>(c =>
                    {
                        c.ContextType = "Model";
                        c.CoordinateSpaceDimension = 3;
                        c.Precision = 1e-5;
                        c.WorldCoordinateSystem = model.Instances.New<IfcAxis2Placement3D>(a =>
                        {
                            a.Location = model.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(0, 0, 0));
                        });
                    });

                    // Wall: simple box 4m x 0.2m x 3m
                    var wall = model.Instances.New<IfcWall>(w => w.Name = "Test Wall");
                    wall.Representation = CreateBoxRepresentation(model, ctx, 4.0, 0.2, 3.0);
                    wall.ObjectPlacement = CreateLocalPlacement(model, 0, 0, 0);
                    Contain(model, storey, wall);

                    // Slab: 6m x 4m x 0.3m
                    var slab = model.Instances.New<IfcSlab>(s => s.Name = "Test Slab");
                    slab.Representation = CreateBoxRepresentation(model, ctx, 6.0, 4.0, 0.3);
                    slab.ObjectPlacement = CreateLocalPlacement(model, 0, 0, -0.3);
                    Contain(model, storey, slab);

                    txn.Commit();
                }

                model.SaveAs(outputPath, StorageType.Ifc);
            }

            Console.WriteLine($"  Created IFC file: {outputPath}");
            Console.WriteLine($"  File size: {new FileInfo(outputPath).Length} bytes");
        }

        static void ReadAndTessellate(string filePath)
        {
            using (var model = IfcStore.Open(filePath))
            {
                Console.WriteLine($"  Schema: {model.SchemaVersion}");

                var context = new Xbim3DModelContext(model);
                context.CreateContext();

                int totalShapes = 0;
                foreach (var si in context.ShapeInstances())
                {
                    if (si.RepresentationType == XbimGeometryRepresentationType.OpeningsAndAdditionsExcluded)
                        continue;

                    var product = model.Instances[si.IfcProductLabel] as IIfcProduct;
                    if (product == null) continue;
                    if (product is IIfcSpatialStructureElement) continue;

                    var shapeGeom = context.ShapeGeometry(si);
                    var shapeData = ((IXbimShapeGeometryData)shapeGeom).ShapeData;
                    if (shapeData == null || shapeData.Length == 0) continue;

                    Console.WriteLine($"  Shape data format: {shapeGeom.Format} (length: {shapeData.Length} bytes)");

                    XbimShapeTriangulation triangulation;
                    using (var ms = new MemoryStream(shapeData))
                    using (var reader = new BinaryReader(ms))
                    {
                        triangulation = reader.ReadShapeTriangulation();
                    }

                    if (triangulation == null) continue;

                    int vertCount = triangulation.Vertices?.Count ?? 0;
                    int faceCount = 0;
                    if (triangulation.Faces != null)
                    {
                        foreach (var fg in triangulation.Faces)
                        {
                            faceCount += fg.Indices.Count / 3;
                        }
                    }

                    string name = product.Name?.ToString() ?? "unnamed";
                    string ifcClass = product.ExpressType.ExpressName;
                    Console.WriteLine($"  Product: {name} ({ifcClass}) -- {vertCount} vertices, {faceCount} triangles");

                    totalShapes++;
                }

                Console.WriteLine($"  Total shapes processed: {totalShapes}");
                if (totalShapes < 2)
                    throw new Exception($"Expected at least 2 shapes (wall+slab), got {totalShapes}");
            }
        }

        static void ValidateSpatialHierarchy(string filePath)
        {
            using (var model = IfcStore.Open(filePath))
            {
                var products = model.Instances.OfType<IIfcProduct>()
                    .Where(p => !(p is IIfcSpatialStructureElement));

                foreach (var product in products)
                {
                    string name = product.Name?.ToString() ?? "unnamed";

                    // IsContainedIn returns the spatial element directly
                    var container = product.IsContainedIn;
                    if (container != null)
                    {
                        Console.Write($"  {name}: contained in {container.Name}");

                        // Walk up via Decomposes
                        var current = container as IIfcObjectDefinition;
                        while (current != null)
                        {
                            var parent = current.Decomposes
                                .OfType<IIfcRelAggregates>()
                                .FirstOrDefault()?.RelatingObject;

                            if (parent != null && !(parent is IIfcProject))
                                Console.Write($" -> {parent.Name}");

                            current = parent;
                        }
                        Console.WriteLine();
                    }
                    else
                    {
                        Console.WriteLine($"  {name}: NOT contained in any spatial element");
                    }
                }
            }
        }

        // --- Helper methods ---

        static IfcProductDefinitionShape CreateBoxRepresentation(
            IfcStore model, IIfcGeometricRepresentationContext ctx,
            double dx, double dy, double dz)
        {
            // 8 vertices of a box at origin
            double[][] verts =
            {
                new[] {0.0, 0.0, 0.0}, new[] {dx, 0.0, 0.0},
                new[] {dx, dy, 0.0},    new[] {0.0, dy, 0.0},
                new[] {0.0, 0.0, dz},   new[] {dx, 0.0, dz},
                new[] {dx, dy, dz},     new[] {0.0, dy, dz}
            };

            var pts = new IfcCartesianPoint[8];
            for (int i = 0; i < 8; i++)
            {
                var v = verts[i];
                pts[i] = model.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(v[0], v[1], v[2]));
            }

            // 12 triangular faces (2 per box face)
            int[][] tris =
            {
                new[] {0,3,2}, new[] {0,2,1},  // bottom
                new[] {4,5,6}, new[] {4,6,7},  // top
                new[] {0,1,5}, new[] {0,5,4},  // front
                new[] {2,3,7}, new[] {2,7,6},  // back
                new[] {1,2,6}, new[] {1,6,5},  // right
                new[] {0,4,7}, new[] {0,7,3},  // left
            };

            var faces = new IfcFace[12];
            for (int i = 0; i < 12; i++)
            {
                var t = tris[i];
                faces[i] = model.Instances.New<IfcFace>(f =>
                {
                    f.Bounds.Add(model.Instances.New<IfcFaceOuterBound>(b =>
                    {
                        b.Bound = model.Instances.New<IfcPolyLoop>(pl =>
                        {
                            pl.Polygon.Add(pts[t[0]]);
                            pl.Polygon.Add(pts[t[1]]);
                            pl.Polygon.Add(pts[t[2]]);
                        });
                        b.Orientation = true;
                    }));
                });
            }

            var shell = model.Instances.New<IfcClosedShell>(s =>
            {
                foreach (var f in faces) s.CfsFaces.Add(f);
            });

            var brep = model.Instances.New<IfcFacetedBrep>(b => b.Outer = shell);

            var shapeRep = model.Instances.New<IfcShapeRepresentation>(sr =>
            {
                sr.ContextOfItems = (IfcRepresentationContext)ctx;
                sr.RepresentationIdentifier = "Body";
                sr.RepresentationType = "Brep";
                sr.Items.Add(brep);
            });

            return model.Instances.New<IfcProductDefinitionShape>(pds =>
            {
                pds.Representations.Add(shapeRep);
            });
        }

        static IfcLocalPlacement CreateLocalPlacement(IfcStore model, double x, double y, double z)
        {
            return model.Instances.New<IfcLocalPlacement>(lp =>
            {
                lp.RelativePlacement = model.Instances.New<IfcAxis2Placement3D>(a =>
                {
                    a.Location = model.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(x, y, z));
                });
            });
        }

        static void Aggregate(IfcStore model, IfcObjectDefinition parent, IfcObjectDefinition child)
        {
            model.Instances.New<IfcRelAggregates>(r =>
            {
                r.RelatingObject = parent;
                r.RelatedObjects.Add(child);
            });
        }

        static void Contain(IfcStore model, IfcSpatialStructureElement container, IfcProduct product)
        {
            var rel = model.Instances.OfType<IfcRelContainedInSpatialStructure>()
                .FirstOrDefault(r => r.RelatingStructure == container);

            if (rel != null)
            {
                rel.RelatedElements.Add(product);
            }
            else
            {
                model.Instances.New<IfcRelContainedInSpatialStructure>(r =>
                {
                    r.RelatingStructure = container;
                    r.RelatedElements.Add(product);
                });
            }
        }
    }
}
