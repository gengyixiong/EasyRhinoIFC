# RhinoIfc

RhinoIfc v0.2.0 is an IFC4 export-only plugin for Rhino 8 on Windows, with a Grasshopper export component.

Rhino geometry and nested Block instances are exported through xBIM as IFC4 or IFCZIP. Rhino layer suffixes provide a lightweight IFC classification system, while colors, object user strings, and the source layer path are preserved where supported.

## What's new in v0.2.0

- Removed the `IfcImport` and `IfcImportMulti` commands, the Grasshopper import component, and import-only geometry dependencies.
- Added automatic IFC4 classification from the final suffix in a Rhino layer name.
- Added all requested common IFC4 element mappings, including stairs, ramps, coverings, structural members, furniture, openings, and proxies.
- Built `IfcProject` → `IfcSite` → `IfcBuilding` → `IfcBuildingStorey` from recognized spatial layer suffixes, while allowing organizational layers in between.
- Preserved every exported object's full Rhino layer path as `RhinoProperties.SourceLayer`.
- The release package is export-only and no longer ships the xBIM import geometry engine.

## Install the release package

Download and extract [RhinoIfc-v0.2.0.zip](https://github.com/gengyixiong/RhinoIfc/releases/latest) to a permanent folder. The ZIP contains `RhinoIfc.rhp` and its managed dependencies in one flat folder.

1. In Rhino, run `_PlugInManager`.
2. Click **Install** and select the extracted `RhinoIfc.rhp`.
3. Restart Rhino.
4. Run `_IfcExport`, choose `All` or `Selected`, and save an `.ifc` or `.ifczip` file.

The Grasshopper assembly is available as `GH_RhinoIfc.dll` in the build output. Copy it to `%APPDATA%\Grasshopper\Libraries\` and rename it to `GH_RhinoIfc.gha` if desired. The Grasshopper component accepts explicit IFC class names; automatic layer classification applies to the Rhino `_IfcExport` command.

## Build from source

Requirements: .NET Framework 4.8 Developer Pack and .NET SDK 6.0 or newer.

```text
dotnet restore
dotnet build RhinoIfc.sln -c Release
```

The plugin is written to `RhinoIfc\bin\Release\RhinoIfc.rhp`; the Grasshopper assembly is written to `GH_RhinoIfc\bin\Release\GH_RhinoIfc.dll`.

To build a Yak package, install the Yak CLI and run `build.bat`.

## Rhino command

| Command | Description |
|---|---|
| `IfcExport` | Export all or selected Rhino objects to IFC4/IFCZIP |

## Layer naming rule for IFC classes

Use this exact form for a BIM element layer:

```text
<descriptive layer name>-<IFC keyword>
```

The exporter applies four rules:

1. It uses the **object's direct Rhino layer**, not a parent layer.
2. It takes the text after the **last hyphen** (`-`) only.
3. Matching is case-insensitive, so `-Wall`, `-wall`, and `-WALL` are equivalent.
4. A missing or unknown keyword is not guessed; the existing `IfcBuildingElementProxy` fallback is used.

Examples:

```text
Exterior Wall-Wall                         -> IfcWall
Existing-Building-Exterior-Wall            -> IfcWall
Ground Floor-Slab                          -> IfcSlab
Main Roof-roof                             -> IfcRoof
Entrance Doors-DOOR                        -> IfcDoor
Level 01-Storey::Exterior::Windows-Window  -> IfcWindow
Mechanical Equipment-Equipment             -> fallback IfcBuildingElementProxy
Reference                                   -> fallback IfcBuildingElementProxy
```

The earlier hyphens are part of the descriptive name. For `Existing-Building-Exterior-Wall`, the keyword is `Wall`, not `Building` or `Exterior`.

### Supported IFC4 keywords

| Keyword | IFC4 class | Keyword | IFC4 class |
|---|---|---|---|
| `Project` | `IfcProject` | `Site` | `IfcSite` |
| `Building` | `IfcBuilding` | `BuildingStorey` | `IfcBuildingStorey` |
| `Storey` | `IfcBuildingStorey` | `Space` | `IfcSpace` |
| `Wall` | `IfcWall` | `Slab` | `IfcSlab` |
| `Roof` | `IfcRoof` | `Beam` | `IfcBeam` |
| `Column` | `IfcColumn` | `Door` | `IfcDoor` |
| `Window` | `IfcWindow` | `CurtainWall` | `IfcCurtainWall` |
| `Stair` | `IfcStair` | `StairFlight` | `IfcStairFlight` |
| `Railing` | `IfcRailing` | `Ramp` | `IfcRamp` |
| `Covering` | `IfcCovering` | `ShadingDevice` | `IfcShadingDevice` |
| `Footing` | `IfcFooting` | `Pile` | `IfcPile` |
| `Member` | `IfcMember` | `Plate` | `IfcPlate` |
| `Opening` | `IfcOpeningElement` | `Furniture` | `IfcFurniture` |
| `FurnishingElement` | `IfcFurnishingElement` | `BuildingElementPart` | `IfcBuildingElementPart` |
| `Proxy` | `IfcBuildingElementProxy` | `BuildingElementProxy` | `IfcBuildingElementProxy` |

## Spatial hierarchy from layers

The suffixes `-Project`, `-Site`, `-Building`, `-Storey`, and `-BuildingStorey` create the IFC spatial hierarchy from the Rhino parent/child layer path. Layers without a recognized suffix are organizational only and may appear anywhere in between.

```text
Office Project-Project
  Main Site-Site
    Office Building-Building
      Level 01-Storey
        Walls
          Exterior Walls-Wall
          Interior Walls-Wall
        Structure
          Columns-Column
          Beams-Beam
      Level 02-Storey
        Exterior Walls-Wall
```

This creates one project, a site, a building, two storeys, and the element objects under the storey that contains their direct layer. The `Walls` and `Structure` layers do not become IFC classes. If a model omits part of the spatial path, the exporter creates a default site, building, or storey so elements remain contained.

Every exported element receives `RhinoProperties.SourceLayer` containing its full Rhino layer path. An object name is preserved when present; otherwise the exporter uses the direct layer name with a sequence number.

## Export behavior

- Planar Breps use `IfcPolygonalFaceSet`; other supported geometry uses indexed `IfcTriangulatedFaceSet`.
- Nested Block instances are flattened to world coordinates.
- Rhino document units are converted to IFC metres.
- IFC2x3 and IFC import are intentionally out of scope for v0.2.0.

## Dependencies

- [xBIM Essentials](https://github.com/xBimTeam/XbimEssentials) (CDDL)
- [RhinoCommon](https://developer.rhino3d.com/guides/rhinocommon/) (McNeel SDK)

## License

MIT
