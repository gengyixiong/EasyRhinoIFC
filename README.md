# RhinoIfc

IFC4-only import/export plugin for Rhino 8 (Windows) with Grasshopper components.

Uses [xBIM](https://docs.xbim.net/) to import and export IFC4 files. IFC2x3 is not supported. Supported parametric geometry is imported as Rhino Breps, with mesh fallback for other geometry. IFC export supports ordinary Rhino geometry and nested Block instances. Spatial-hierarchy layers, colors, and IFC metadata are preserved where supported.

## Download and Install

### Option A: From Releases (recommended)

1. Go to the [Releases](https://github.com/gengyixiong/RhinoIfc/releases) page
2. Download the latest `.zip` file
3. Extract to a permanent folder (e.g. `C:\Users\YourName\AppData\Roaming\McNeel\Rhinoceros\8.0\Plug-ins\RhinoIfc\`)
4. In Rhino, run `_PlugInManager`, click **Install**, and browse to `RhinoIfc.rhp`
5. Restart Rhino

### Option B: Build from source

**Prerequisites:**

- [.NET Framework 4.8 targeting pack](https://dotnet.microsoft.com/download/dotnet-framework/net48) (included with Visual Studio 2022 .NET desktop workload)
- .NET SDK 6.0+ (for `dotnet build`)

**Steps:**

```
git clone https://github.com/gengyixiong/RhinoIfc.git
cd RhinoIfc
dotnet restore
dotnet build -c Release
```

Then load the plugin:
1. Copy all files from `RhinoIfc\bin\Release\` to a permanent folder
2. Rename `RhinoIfc.dll` to `RhinoIfc.rhp`
3. Drag `RhinoIfc.rhp` into the Rhino viewport, or use `_PlugInManager` to install

> **Important:** The xBIM native geometry engine DLLs (`Xbim.Geometry.Engine64.dll`, etc.) must be in the same directory as the `.rhp`. The build copies them automatically.

### Option C: Yak package

If you have the [Yak CLI](https://developer.rhino3d.com/guides/yak/) on your PATH:

```
build.bat
```

This builds, stages all DLLs (including xBIM native engines), and runs `yak build`. Install the resulting `.yak` via `_PackageManager` in Rhino.

### Grasshopper components

The Grasshopper plugin (`GH_RhinoIfc.dll` / `.gha`) is built alongside the Rhino plugin. To install:

1. Copy `GH_RhinoIfc.dll` from `GH_RhinoIfc\bin\Release\` to your Grasshopper Libraries folder
   - Typically: `%APPDATA%\Grasshopper\Libraries\`
2. Rename to `GH_RhinoIfc.gha` (optional but conventional)
3. Restart Rhino/Grasshopper

## Commands

| Command | Description |
|---------|-------------|
| `IfcImport` | Import a single IFC4 or IFCZIP file as Rhino meshes with layers, colors, and metadata |
| `IfcImportMulti` | Import multiple IFC4/IFCZIP files at once with configurable grouping (see below) |
| `IfcExport` | Export Rhino objects, including nested Block instances, to IFC4 or IFCZIP with automatic class mapping from layer names |

## IFC4 export optimizations

Version 0.1.11 focuses on reducing export size and peak memory use:

- Planar Breps use `IfcPolygonalFaceSet` directly from Brep topology instead of repeating faceted geometry.
- Mesh fallback uses indexed `IfcTriangulatedFaceSet` coordinates, sharing vertices across triangles.
- Rhino cached Render Meshes are reused; fallback meshing follows the document/object render settings.
- Coordinates are rounded to the declared model precision (1e-5 m), and temporary meshes are disposed deterministically.
- Export and import support compressed `.ifczip` files as well as plain `.ifc` files.
- Export transactions are sequential, avoiding nested xBIM transactions during large-file export.

IFC2x3 is intentionally out of scope for this release. Nested Block instances are supported, but are currently flattened rather than emitted as reusable `IfcMappedItem` representations.

### IfcImportMulti

Designed for multi-discipline workflows where architecture, structure, and MEP are delivered as separate IFC files. Select multiple files and choose a grouping mode:

- **ByFile** (default) — Each file gets a color-coded parent layer. Spatial hierarchy nests underneath.
  ```
  Arch           (red)
  +-- Default Site :: Building A :: Ground Floor :: Wall
  +-- Default Site :: Building A :: Ground Floor :: Slab
  Struct         (blue)
  +-- Default Site :: Building A :: Ground Floor :: Column
  ```

- **ByGroup** — Files share the same layer hierarchy. Each file's objects are placed in a named Rhino group so you can isolate disciplines with `SelGroup`.

- **Flat** — All files merge into one shared layer hierarchy. No grouping. Identical layer paths reuse existing layers.

The entire multi-import is wrapped in a single undo record (`Ctrl+Z` undoes all files at once). A summary table is printed after import showing element counts and timing per file.

## Layer to IFC class mapping (export)

Name your Rhino layers to match IFC types:

| Layer name (case-insensitive) | IFC class |
|------------------------------|-----------|
| Wall, Walls | IfcWall |
| Slab, Slabs, Floor | IfcSlab |
| Column, Columns | IfcColumn |
| Beam, Beams | IfcBeam |
| Door, Doors | IfcDoor |
| Window, Windows | IfcWindow |
| Roof | IfcRoof |
| Stair, Stairs | IfcStair |
| Railing | IfcRailing |
| Furniture | IfcFurnishingElement |
| *(anything else)* | IfcBuildingElementProxy |

Nested layers work — `Architecture :: Ground Floor :: Walls` will match "Walls" as IfcWall.

## IFC metadata (import)

Imported objects store IFC data as user strings:

- `IFC_GlobalId` — unique IFC identifier
- `IFC_Name` — element name
- `IFC_Class` — e.g. IfcWall, IfcSlab
- `PsetName.PropertyName` — all property set values

View with: select object, then Properties panel, Attribute User Text section.

## Dependencies

- [xBIM Essentials](https://github.com/xBimTeam/XbimEssentials) (CDDL license)
- [xBIM Geometry](https://github.com/xBimTeam/XbimGeometry) (CDDL license)
- [RhinoCommon](https://developer.rhino3d.com/guides/rhinocommon/) (McNeel SDK)

## License

MIT
