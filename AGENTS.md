# Agent Guide — EasyRhinoIFC

> **Minimal front door (2026-08-17).** IFC4 export plugin for Rhino 8 (Windows) with
> a Grasshopper export component. Rhino layer suffixes drive IFC classification and
> spatial hierarchy; colors, metadata, and nested Block geometry are exported via xBIM.
> This repo has no AGENTS-DETAIL.md — README.md and the key dirs below are the depth.
> **Editing this file: keep it under 7,900 chars** — a size guard alarms via
> `MultiVerse/GOVERNANCE-ALERTS.md` on breach.

## Hard rules
- Do not commit or push without the operator asking.
- Cross-Repo Change Protocol applies — see [`C:\Repos\AGENTS.md`](../AGENTS.md) (umbrella).

## Where to look
- `README.md` — install (PlugInManager) and build-from-source (.NET Framework 4.8, `dotnet build`).
- `RhinoIfc/` — plugin source; `GH_RhinoIfc/` — Grasshopper components; `RhinoIfc.Tests/` — tests (`RhinoIfc.sln`).
- `build.bat` — build entry point; `manifest.yml` — Yak package manifest.
- `spec/` — design notes; `samplefiles/` — IFC fixtures; `docs/` is currently untracked.
