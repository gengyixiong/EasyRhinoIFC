# Agent Guide — RhinoIfc

> **Minimal front door (2026-07-10).** IFC import/export plugin for Rhino 8 (Windows) with
> Grasshopper components — parses IFC2x3/IFC4 via xBIM; imports as meshes with full
> spatial-hierarchy layers, colors, and IFC metadata as user strings. Status: dormant
> (initial release + one sync commit).
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
