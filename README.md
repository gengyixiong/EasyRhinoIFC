# EasyRhinoIFC

EasyRhinoIFC is an export-only IFC4 plugin for Rhino 8 on Windows. It exports Rhino geometry and nested Block instances through xBIM to `.ifc` or `.ifczip` files. A Grasshopper component is included for mesh-based IFC export.

IFC import is not included in v0.2.2.

## Install the release

1. Download the latest [EasyRhinoIFC release](https://github.com/gengyixiong/EasyRhinoIFC/releases/latest).
2. Extract the complete ZIP to a permanent folder. Keep `EasyRhinoIFC.rhp` and all DLLs together.
3. In Rhino, run `_PlugInManager`, choose **Install**, and select the extracted `EasyRhinoIFC.rhp`.
4. Restart Rhino.
5. Run `_IfcExport`.

The optional Grasshopper component is `GH_EasyRhinoIFC.dll`. Copy it to `%APPDATA%\Grasshopper\Libraries\` and restart Grasshopper. If your Grasshopper installation requires it, rename the file to `GH_EasyRhinoIFC.gha` after copying.

## Use the Rhino exporter

1. Put each object on a Rhino layer whose final suffix identifies its IFC class (see below).
2. Run `_IfcExport`.
3. Choose **All** or **Selected**.
4. Choose an output path ending in `.ifc` or `.ifczip`.

The exporter uses the object's direct layer for classification, converts Rhino units to IFC metres, keeps nested Block geometry in world coordinates, and preserves Rhino object colors and user strings where supported.

## Layer names and IFC classes

Name an element layer as:

```text
<descriptive name>-<IFC keyword>
```

The rules are:

- The object's **direct layer** is used; a parent layer does not classify its children.
- Only the text after the **last `-`** in that layer name is read.
- Keywords are case-insensitive (`-Wall`, `-wall`, and `-WALL` are equivalent).
- A missing or unknown keyword falls back to `IfcBuildingElementProxy`; it is never guessed.

Examples:

```text
Exterior Wall-Wall                         -> IfcWall
Existing-Building-Exterior-Wall            -> IfcWall
Ground Floor-Slab                          -> IfcSlab
Main Roof-roof                             -> IfcRoof
Entrance Doors-DOOR                        -> IfcDoor
Level 01-Storey::Exterior::Windows-Window  -> IfcWindow
Mechanical Equipment-Equipment             -> IfcBuildingElementProxy
Reference                                   -> IfcBuildingElementProxy
```

In `Existing-Building-Exterior-Wall`, `Wall` is the keyword. The earlier hyphens are simply part of the descriptive name.

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

## Recommended Rhino layer structure

Use parent/child layers for spatial organization. Add `-Project`, `-Site`, `-Building`, and `-Storey` to the layers that should become IFC spatial nodes. Any layer without one of these recognized suffixes is an organizational layer and may sit between them.

```text
Office Project-Project
  Main Site-Site
    Office Building-Building
      Level 01-Storey
        Architecture
          Exterior Walls-Wall
          Interior Walls-Wall
        Structure
          Columns-Column
          Beams-Beam
          Floor-Slab
        Openings
          Doors-Door
          Windows-Window
      Level 02-Storey
        Exterior Walls-Wall
```

This produces an `IfcProject` containing an `IfcSite`, an `IfcBuilding`, and two `IfcBuildingStorey` objects. The `Architecture`, `Structure`, and `Openings` layers are only organizational; the element's own layer suffix determines its IFC class.

If part of the spatial path is omitted, the exporter creates default site, building, and storey containers so elements remain in a valid IFC hierarchy. Each exported element also receives its full Rhino layer path as `RhinoProperties.SourceLayer`.

## Grasshopper exporter

The `IFC Export` component accepts meshes, optional element names, optional IFC class names, and an output path. It always writes IFC4 and uses `IfcBuildingElementProxy` when a class is not supplied. Layer-name classification applies to the Rhino `_IfcExport` command; Grasshopper class names are supplied explicitly through the component input.

## Build from source

Requirements: the .NET Framework 4.8 Developer Pack and .NET SDK 6.0 or newer.

```text
dotnet restore
dotnet build RhinoIfc.sln -c Release
```

Build outputs:

- `RhinoIfc\bin\Release\EasyRhinoIFC.rhp`
- `GH_RhinoIfc\bin\Release\GH_EasyRhinoIFC.dll`

Run `build.bat` after installing the Yak CLI to create the Yak package.

## Dependencies and license

- [xBIM Essentials](https://github.com/xBimTeam/XbimEssentials) (CDDL)
- [RhinoCommon](https://developer.rhino3d.com/guides/rhinocommon/) (McNeel SDK)

[MIT](LICENSE)
