# EasyRhinoIFC

[English](#english) | [简体中文](#简体中文)

## English

EasyRhinoIFC helps architects turn Rhino design models into structured, lightweight IFC files that are easy to share and coordinate—without requiring advanced BIM or IFC knowledge.

### Key Benefits

- **Keep designing in Rhino** — Continue using your familiar Rhino workflow without learning a separate BIM authoring tool.
- **Classify with layer names** — Add IFC keywords to Rhino layer names for automatic object classification.
- **Preserve model structure** — Carry building, storey, and layer hierarchy into the exported IFC file.
- **Preserve design intent** — Retain object colors, layer colors, and colors inside nested Blocks.
- **Export quickly and share easily** — Create compact IFC files from large Rhino models.
- **Improve collaboration** — Give engineers a structured IFC model that is easier to review and use.

## 简体中文

EasyRhinoIFC 帮助建筑师将 Rhino 设计模型转换为结构清晰、轻量易分享的 IFC 文件，无需掌握复杂的 BIM 或 IFC 知识。

### 核心优势

- **继续使用 Rhino 设计** —— 保留熟悉的 Rhino 工作流程，无需学习额外的 BIM 建模软件。
- **通过图层名称完成分类** —— 在 Rhino 图层名称中添加 IFC 关键字，即可自动识别对象类别。
- **保留模型结构** —— 导出时保留建筑、楼层和图层之间的层级关系。
- **保留设计意图** —— 保留对象颜色、图层颜色以及嵌套 Block 内部的颜色。
- **导出更快，文件更轻** —— 将大型 Rhino 模型转换为更轻量的 IFC 文件。
- **让工程协作更顺畅** —— 为工程团队提供结构清晰、易于理解和使用的 IFC 模型。

---

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

---

## 中文文档

EasyRhinoIFC 是一个适用于 Windows Rhino 8 的 IFC4 导出插件。它通过 xBIM 将 Rhino 几何体和嵌套的 Block 实例导出为 `.ifc` 或 `.ifczip` 文件，并提供一个基于网格的 Grasshopper 导出组件。

v0.2.2 不包含 IFC 导入功能。

## 安装发布版

1. 下载最新的 [EasyRhinoIFC 发布版](https://github.com/gengyixiong/EasyRhinoIFC/releases/latest)。
2. 将完整 ZIP 解压到一个固定文件夹中。请保持 `EasyRhinoIFC.rhp` 与所有 DLL 文件位于同一目录。
3. 在 Rhino 中运行 `_PlugInManager`，选择 **Install**，然后选择解压出的 `EasyRhinoIFC.rhp`。
4. 重启 Rhino。
5. 运行 `_IfcExport`。

可选的 Grasshopper 组件文件是 `GH_EasyRhinoIFC.dll`。将它复制到 `%APPDATA%\Grasshopper\Libraries\`，然后重启 Grasshopper。如果你的 Grasshopper 安装要求使用 `.gha` 扩展名，可在复制后将文件改名为 `GH_EasyRhinoIFC.gha`。

## 使用 Rhino 导出器

1. 将每个对象放到 Rhino 图层中，并使用图层名称的最后后缀标识 IFC 类别（见下文）。
2. 运行 `_IfcExport`。
3. 选择 **All**（全部）或 **Selected**（选中对象）。
4. 选择以 `.ifc` 或 `.ifczip` 结尾的输出路径。

导出器使用对象的直接图层进行分类，将 Rhino 单位转换为 IFC 米制单位，以世界坐标保留嵌套 Block 几何体，并在支持的情况下保留 Rhino 对象颜色和用户字符串。

## 图层名称与 IFC 类别

元素图层名称格式如下：

```text
<描述性名称>-<IFC 关键字>
```

规则如下：

- 使用对象的**直接图层**；父图层不会对其子对象进行分类。
- 只读取图层名称中**最后一个 `-`** 之后的文本。
- 关键字不区分大小写（`-Wall`、`-wall` 和 `-WALL` 等价）。
- 缺失或未知的关键字会回退到 `IfcBuildingElementProxy`，不会进行猜测。

示例：

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

在 `Existing-Building-Exterior-Wall` 中，`Wall` 是关键字；前面的连字符只是描述性名称的一部分。

### 支持的 IFC4 关键字

| 关键字 | IFC4 类别 | 关键字 | IFC4 类别 |
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

## 推荐的 Rhino 图层结构

使用父子图层组织空间层级。将 `-Project`、`-Site`、`-Building` 和 `-Storey` 添加到应当成为 IFC 空间节点的图层上。没有这些已识别后缀的图层仅用于组织，可以位于这些空间图层之间。

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

这会生成一个包含 `IfcSite`、`IfcBuilding` 和两个 `IfcBuildingStorey` 对象的 `IfcProject`。`Architecture`、`Structure` 和 `Openings` 图层仅用于组织；元素自身的图层后缀决定其 IFC 类别。

如果空间路径中缺少某一部分，导出器会创建默认的 site、building 和 storey 容器，使元素仍处于有效的 IFC 层级中。每个导出的元素还会将完整的 Rhino 图层路径写入 `RhinoProperties.SourceLayer`。

## Grasshopper 导出器

`IFC Export` 组件接受网格、可选的元素名称、可选的 IFC 类别名称和输出路径。它始终写入 IFC4；如果没有提供类别，则使用 `IfcBuildingElementProxy`。基于图层名称的分类适用于 Rhino 的 `_IfcExport` 命令；Grasshopper 的类别名称通过组件输入显式提供。

## 从源代码构建

要求：.NET Framework 4.8 Developer Pack，以及 .NET SDK 6.0 或更高版本。

```text
dotnet restore
dotnet build RhinoIfc.sln -c Release
```

构建输出：

- `RhinoIfc\bin\Release\EasyRhinoIFC.rhp`
- `GH_RhinoIfc\bin\Release\GH_EasyRhinoIFC.dll`

安装 Yak CLI 后运行 `build.bat`，即可创建 Yak 软件包。

## 依赖与许可证

- [xBIM Essentials](https://github.com/xBimTeam/XbimEssentials)（CDDL）
- [RhinoCommon](https://developer.rhino3d.com/guides/rhinocommon/)（McNeel SDK）

[MIT](LICENSE)

[返回顶部](#easyrhinoifc)
