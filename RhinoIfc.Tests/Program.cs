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
using Xbim.Ifc4.ProfileResource;
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
            Console.WriteLine("=== Phase 4: Build parametric Brep fixture ===");
            string parametricFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_parametric.ifc");
            CreateParametricFixture(parametricFile);
            ValidateParametricFixture(parametricFile);

            Console.WriteLine();
            Console.WriteLine("All tests passed.");
            Console.WriteLine();
            Console.WriteLine("Manual verification:");
            Console.WriteLine($"  Open '{parametricFile}' with the IfcImport command in Rhino");
            Console.WriteLine("  and confirm the parametric elements come in as Breps (not meshes).");
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

        // ----- Phase 4: parametric fixture -----

        static void CreateParametricFixture(string outputPath)
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
                        p.Name = "Parametric Fixture";
                        p.Initialize(ProjectUnits.SIUnitsUK);
                    });

                    var site = model.Instances.New<IfcSite>(s =>
                    {
                        s.Name = "Site";
                        s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                    });
                    var building = model.Instances.New<IfcBuilding>(b =>
                    {
                        b.Name = "Building";
                        b.CompositionType = IfcElementCompositionEnum.ELEMENT;
                    });
                    var storey = model.Instances.New<IfcBuildingStorey>(s =>
                    {
                        s.Name = "Ground";
                        s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                        s.Elevation = 0;
                    });
                    Aggregate(model, project, site);
                    Aggregate(model, site, building);
                    Aggregate(model, building, storey);

                    var ctx = model.Instances.New<IfcGeometricRepresentationContext>(c =>
                    {
                        c.ContextType = "Model";
                        c.CoordinateSpaceDimension = 3;
                        c.Precision = 1e-5;
                        c.WorldCoordinateSystem = MakeAxis2Placement3D(model, 0, 0, 0);
                    });

                    // 1. Wall: IfcExtrudedAreaSolid + IfcRectangleProfileDef (4 x 0.2 x 3 m)
                    var wall = model.Instances.New<IfcWall>(w => w.Name = "Param Wall");
                    wall.Representation = MakeExtrudedRectRepresentation(model, ctx, 4.0, 0.2, 3.0);
                    wall.ObjectPlacement = CreateLocalPlacement(model, 0, 0, 0);
                    Contain(model, storey, wall);

                    // 2. Column: IfcExtrudedAreaSolid + IfcCircleProfileDef (r=0.25, h=4)
                    var column = model.Instances.New<IfcColumn>(c => c.Name = "Param Column");
                    column.Representation = MakeExtrudedCircleRepresentation(model, ctx, 0.25, 4.0);
                    column.ObjectPlacement = CreateLocalPlacement(model, 6, 0, 0);
                    Contain(model, storey, column);

                    // 3. Slab: IfcExtrudedAreaSolid + IfcArbitraryClosedProfileDef (triangle)
                    var slab = model.Instances.New<IfcSlab>(s => s.Name = "Param Slab");
                    slab.Representation = MakeExtrudedTriangleRepresentation(
                        model, ctx,
                        new[] { (0.0, 0.0), (5.0, 0.0), (2.5, 4.0) },
                        0.3);
                    slab.ObjectPlacement = CreateLocalPlacement(model, 10, 0, 0);
                    Contain(model, storey, slab);

                    // 4. Wall with opening (IfcRelVoidsElement)
                    var hostWall = model.Instances.New<IfcWall>(w => w.Name = "Wall With Opening");
                    hostWall.Representation = MakeExtrudedRectRepresentation(model, ctx, 4.0, 0.2, 3.0);
                    hostWall.ObjectPlacement = CreateLocalPlacement(model, 0, 5, 0);
                    Contain(model, storey, hostWall);

                    var opening = model.Instances.New<IfcOpeningElement>(o => o.Name = "Door Opening");
                    opening.Representation = MakeExtrudedRectRepresentation(model, ctx, 1.0, 0.4, 2.1);
                    opening.ObjectPlacement = CreateLocalPlacement(model, 1.0, -0.1, 0);
                    model.Instances.New<IfcRelVoidsElement>(rel =>
                    {
                        rel.RelatingBuildingElement = hostWall;
                        rel.RelatedOpeningElement = opening;
                    });

                    // 5. Family instance via IfcMappedItem (2 instances of one source)
                    // Source representation: a small extruded rectangle
                    var sourceRep = model.Instances.New<IfcShapeRepresentation>(sr =>
                    {
                        sr.ContextOfItems = ctx;
                        sr.RepresentationIdentifier = "Body";
                        sr.RepresentationType = "SweptSolid";
                        sr.Items.Add(MakeExtrudedRectSolid(model, 0.5, 0.5, 1.5));
                    });
                    var repMap = model.Instances.New<IfcRepresentationMap>(rm =>
                    {
                        rm.MappingOrigin = MakeAxis2Placement3D(model, 0, 0, 0);
                        rm.MappedRepresentation = sourceRep;
                    });

                    var furn1 = model.Instances.New<IfcFurnishingElement>(f => f.Name = "Furn Instance 1");
                    furn1.Representation = MakeMappedRepresentation(model, ctx, repMap, 0, 0, 0);
                    furn1.ObjectPlacement = CreateLocalPlacement(model, 0, 10, 0);
                    Contain(model, storey, furn1);

                    var furn2 = model.Instances.New<IfcFurnishingElement>(f => f.Name = "Furn Instance 2");
                    furn2.Representation = MakeMappedRepresentation(model, ctx, repMap, 2, 0, 0);
                    furn2.ObjectPlacement = CreateLocalPlacement(model, 0, 10, 0);
                    Contain(model, storey, furn2);

                    txn.Commit();
                }

                model.SaveAs(outputPath, StorageType.Ifc);
            }

            Console.WriteLine($"  Created parametric fixture: {outputPath}");
            Console.WriteLine($"  File size: {new FileInfo(outputPath).Length} bytes");
        }

        static void ValidateParametricFixture(string filePath)
        {
            using (var model = IfcStore.Open(filePath))
            {
                int extrudedCount = 0;
                int rectProfileCount = 0;
                int circleProfileCount = 0;
                int arbitraryProfileCount = 0;
                int mappedItemCount = 0;
                int openingCount = 0;

                foreach (var product in model.Instances.OfType<IIfcProduct>())
                {
                    if (product.Representation == null) continue;
                    foreach (var rep in product.Representation.Representations)
                    {
                        foreach (var item in rep.Items)
                        {
                            if (item is IIfcExtrudedAreaSolid eas)
                            {
                                extrudedCount++;
                                if (eas.SweptArea is IIfcRectangleProfileDef) rectProfileCount++;
                                else if (eas.SweptArea is IIfcCircleProfileDef) circleProfileCount++;
                                else if (eas.SweptArea is IIfcArbitraryClosedProfileDef) arbitraryProfileCount++;
                            }
                            else if (item is IIfcMappedItem)
                            {
                                mappedItemCount++;
                            }
                        }
                    }
                }

                openingCount = model.Instances.OfType<IIfcRelVoidsElement>().Count();

                Console.WriteLine($"  Extruded solids: {extrudedCount}");
                Console.WriteLine($"    rectangle profiles: {rectProfileCount}");
                Console.WriteLine($"    circle profiles:    {circleProfileCount}");
                Console.WriteLine($"    arbitrary profiles: {arbitraryProfileCount}");
                Console.WriteLine($"  Mapped items:    {mappedItemCount}");
                Console.WriteLine($"  Openings (RelVoidsElement): {openingCount}");

                if (extrudedCount < 5)
                    throw new Exception($"Expected >=5 extruded solids (wall, column, slab, host wall, opening), got {extrudedCount}");
                if (rectProfileCount < 3)
                    throw new Exception($"Expected >=3 rectangle profiles, got {rectProfileCount}");
                if (circleProfileCount < 1)
                    throw new Exception($"Expected >=1 circle profile, got {circleProfileCount}");
                if (arbitraryProfileCount < 1)
                    throw new Exception($"Expected >=1 arbitrary profile, got {arbitraryProfileCount}");
                if (mappedItemCount < 2)
                    throw new Exception($"Expected >=2 mapped items, got {mappedItemCount}");
                if (openingCount < 1)
                    throw new Exception($"Expected >=1 opening relation, got {openingCount}");

                // Spot-check exact dimensions on the first rectangle wall
                var paramWall = model.Instances.OfType<IIfcWall>()
                    .FirstOrDefault(w => w.Name == "Param Wall");
                if (paramWall == null)
                    throw new Exception("Could not find 'Param Wall' in fixture.");

                var wallSolid = paramWall.Representation.Representations
                    .SelectMany(r => r.Items)
                    .OfType<IIfcExtrudedAreaSolid>()
                    .FirstOrDefault();
                if (wallSolid == null)
                    throw new Exception("'Param Wall' has no IfcExtrudedAreaSolid item.");

                var rectProfile = wallSolid.SweptArea as IIfcRectangleProfileDef;
                if (rectProfile == null)
                    throw new Exception("'Param Wall' swept area is not IfcRectangleProfileDef.");

                double xDim = rectProfile.XDim;
                double yDim = rectProfile.YDim;
                double depth = wallSolid.Depth;
                Console.WriteLine($"  Param Wall: profile {xDim} x {yDim}, depth {depth}");
                if (Math.Abs(xDim - 4.0) > 1e-6 || Math.Abs(yDim - 0.2) > 1e-6 || Math.Abs(depth - 3.0) > 1e-6)
                    throw new Exception($"Param Wall dimensions wrong: expected 4 x 0.2 x 3, got {xDim} x {yDim} x {depth}");
            }

            Console.WriteLine("  Parametric fixture validated.");
        }

        // --- Phase 4 fixture builders ---

        static IfcProductDefinitionShape MakeExtrudedRectRepresentation(
            IfcStore model, IIfcGeometricRepresentationContext ctx,
            double dx, double dy, double dz)
        {
            var solid = MakeExtrudedRectSolid(model, dx, dy, dz);
            var shapeRep = model.Instances.New<IfcShapeRepresentation>(sr =>
            {
                sr.ContextOfItems = (IfcRepresentationContext)ctx;
                sr.RepresentationIdentifier = "Body";
                sr.RepresentationType = "SweptSolid";
                sr.Items.Add(solid);
            });
            return model.Instances.New<IfcProductDefinitionShape>(pds => pds.Representations.Add(shapeRep));
        }

        static IfcExtrudedAreaSolid MakeExtrudedRectSolid(IfcStore model, double dx, double dy, double dz)
        {
            var profile = model.Instances.New<IfcRectangleProfileDef>(p =>
            {
                p.ProfileType = IfcProfileTypeEnum.AREA;
                p.XDim = dx;
                p.YDim = dy;
                p.Position = MakeAxis2Placement2D(model, 0, 0);
            });
            return model.Instances.New<IfcExtrudedAreaSolid>(e =>
            {
                e.SweptArea = profile;
                e.Position = MakeAxis2Placement3D(model, 0, 0, 0);
                e.ExtrudedDirection = model.Instances.New<IfcDirection>(d => d.SetXYZ(0, 0, 1));
                e.Depth = dz;
            });
        }

        static IfcProductDefinitionShape MakeExtrudedCircleRepresentation(
            IfcStore model, IIfcGeometricRepresentationContext ctx,
            double radius, double height)
        {
            var profile = model.Instances.New<IfcCircleProfileDef>(p =>
            {
                p.ProfileType = IfcProfileTypeEnum.AREA;
                p.Radius = radius;
                p.Position = MakeAxis2Placement2D(model, 0, 0);
            });
            var solid = model.Instances.New<IfcExtrudedAreaSolid>(e =>
            {
                e.SweptArea = profile;
                e.Position = MakeAxis2Placement3D(model, 0, 0, 0);
                e.ExtrudedDirection = model.Instances.New<IfcDirection>(d => d.SetXYZ(0, 0, 1));
                e.Depth = height;
            });
            var shapeRep = model.Instances.New<IfcShapeRepresentation>(sr =>
            {
                sr.ContextOfItems = (IfcRepresentationContext)ctx;
                sr.RepresentationIdentifier = "Body";
                sr.RepresentationType = "SweptSolid";
                sr.Items.Add(solid);
            });
            return model.Instances.New<IfcProductDefinitionShape>(pds => pds.Representations.Add(shapeRep));
        }

        static IfcProductDefinitionShape MakeExtrudedTriangleRepresentation(
            IfcStore model, IIfcGeometricRepresentationContext ctx,
            (double x, double y)[] points, double height)
        {
            var poly = model.Instances.New<IfcPolyline>(pl =>
            {
                foreach (var (x, y) in points)
                    pl.Points.Add(model.Instances.New<IfcCartesianPoint>(p => p.SetXY(x, y)));
                // Close the polyline by repeating the first point
                pl.Points.Add(model.Instances.New<IfcCartesianPoint>(p => p.SetXY(points[0].x, points[0].y)));
            });
            var profile = model.Instances.New<IfcArbitraryClosedProfileDef>(p =>
            {
                p.ProfileType = IfcProfileTypeEnum.AREA;
                p.OuterCurve = poly;
            });
            var solid = model.Instances.New<IfcExtrudedAreaSolid>(e =>
            {
                e.SweptArea = profile;
                e.Position = MakeAxis2Placement3D(model, 0, 0, 0);
                e.ExtrudedDirection = model.Instances.New<IfcDirection>(d => d.SetXYZ(0, 0, 1));
                e.Depth = height;
            });
            var shapeRep = model.Instances.New<IfcShapeRepresentation>(sr =>
            {
                sr.ContextOfItems = (IfcRepresentationContext)ctx;
                sr.RepresentationIdentifier = "Body";
                sr.RepresentationType = "SweptSolid";
                sr.Items.Add(solid);
            });
            return model.Instances.New<IfcProductDefinitionShape>(pds => pds.Representations.Add(shapeRep));
        }

        static IfcProductDefinitionShape MakeMappedRepresentation(
            IfcStore model, IIfcGeometricRepresentationContext ctx,
            IfcRepresentationMap source, double tx, double ty, double tz)
        {
            var target = model.Instances.New<IfcCartesianTransformationOperator3D>(op =>
            {
                op.LocalOrigin = model.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(tx, ty, tz));
                op.Axis1 = model.Instances.New<IfcDirection>(d => d.SetXYZ(1, 0, 0));
                op.Axis2 = model.Instances.New<IfcDirection>(d => d.SetXYZ(0, 1, 0));
                op.Axis3 = model.Instances.New<IfcDirection>(d => d.SetXYZ(0, 0, 1));
                op.Scale = 1.0;
            });
            var mapped = model.Instances.New<IfcMappedItem>(mi =>
            {
                mi.MappingSource = source;
                mi.MappingTarget = target;
            });
            var shapeRep = model.Instances.New<IfcShapeRepresentation>(sr =>
            {
                sr.ContextOfItems = (IfcRepresentationContext)ctx;
                sr.RepresentationIdentifier = "Body";
                sr.RepresentationType = "MappedRepresentation";
                sr.Items.Add(mapped);
            });
            return model.Instances.New<IfcProductDefinitionShape>(pds => pds.Representations.Add(shapeRep));
        }

        static IfcAxis2Placement3D MakeAxis2Placement3D(IfcStore model, double x, double y, double z)
        {
            return model.Instances.New<IfcAxis2Placement3D>(p =>
            {
                p.Location = model.Instances.New<IfcCartesianPoint>(c => c.SetXYZ(x, y, z));
                p.Axis = model.Instances.New<IfcDirection>(d => d.SetXYZ(0, 0, 1));
                p.RefDirection = model.Instances.New<IfcDirection>(d => d.SetXYZ(1, 0, 0));
            });
        }

        static IfcAxis2Placement2D MakeAxis2Placement2D(IfcStore model, double x, double y)
        {
            return model.Instances.New<IfcAxis2Placement2D>(p =>
            {
                p.Location = model.Instances.New<IfcCartesianPoint>(c => c.SetXY(x, y));
                p.RefDirection = model.Instances.New<IfcDirection>(d => d.SetXY(1, 0));
            });
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
