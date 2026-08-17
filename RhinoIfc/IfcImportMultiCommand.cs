// Test Scenarios:
//
// 1. ByFile mode (default):
//    Run IfcImportMulti, select 2+ IFC files, press Enter at grouping prompt.
//    Expected: Each file gets a top-level parent layer (filename without ext),
//    spatial hierarchy nests underneath. Parent layers cycle through palette colors.
//    Ctrl+Z undoes all files at once.
//
// 2. ByGroup mode:
//    Run IfcImportMulti, select 2+ IFC files, type "ByGroup" at grouping prompt.
//    Expected: No parent layers — spatial hierarchy is shared across files.
//    Each file's objects are added to a named Rhino group (filename without ext).
//    Group index is printed. SelGroup can isolate one discipline.
//
// 3. Flat mode:
//    Run IfcImportMulti, select 2+ IFC files, type "Flat" at grouping prompt.
//    Expected: All files import into same layer hierarchy, identical layer
//    paths merge. No groups, no parent layers. Behaves like N separate imports.
//
// 4. Single file:
//    Select just 1 file. Should still work correctly in any mode.
//
// 5. Scripted:
//    _-IfcImportMulti then enter file paths one per line, empty line to finish,
//    then grouping mode. Verify it works without dialogs.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;
using RhinoIfc.Import;

namespace RhinoIfc
{
    public class IfcImportMultiCommand : Command
    {
        public override string EnglishName => "IfcImportMulti";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // --- 1. Collect file paths ---
            string[] filePaths;

            if (mode == RunMode.Interactive)
            {
                var fd = new Rhino.UI.OpenFileDialog
                {
                    Filter = "IFC files (*.ifc;*.ifczip)|*.ifc;*.ifczip|All files (*.*)|*.*",
                    Title = "Import Multiple IFC Files",
                    MultiSelect = true
                };

                if (!fd.ShowOpenDialog())
                    return Result.Cancel;

                filePaths = fd.FileNames;
            }
            else
            {
                // Scripted mode: prompt for file paths one at a time
                var paths = new List<string>();
                while (true)
                {
                    string path = string.Empty;
                    var rc = RhinoGet.GetString("IFC file path (Enter to finish)", true, ref path);
                    if (rc != Result.Success || string.IsNullOrWhiteSpace(path))
                        break;
                    if (!File.Exists(path))
                    {
                        RhinoApp.WriteLine($"File not found: {path}");
                        continue;
                    }
                    paths.Add(path);
                }

                if (paths.Count == 0)
                    return Result.Cancel;

                filePaths = paths.ToArray();
            }

            if (filePaths == null || filePaths.Length == 0)
                return Result.Cancel;

            // --- 2. Prompt for grouping mode ---
            var getOption = new GetOption();
            getOption.SetCommandPrompt("Grouping mode");
            int byFileIdx = getOption.AddOption("ByFile");
            int byGroupIdx = getOption.AddOption("ByGroup");
            int flatIdx = getOption.AddOption("Flat");
            getOption.SetDefaultString("ByFile");

            var grouping = GroupingMode.ByFile;
            var getResult = getOption.Get();

            if (getResult == GetResult.Option)
            {
                int optIdx = getOption.OptionIndex();
                if (optIdx == byGroupIdx) grouping = GroupingMode.ByGroup;
                else if (optIdx == flatIdx) grouping = GroupingMode.Flat;
            }
            else if (getResult == GetResult.String)
            {
                string input = getOption.StringResult().Trim();
                if (input.Equals("ByGroup", StringComparison.OrdinalIgnoreCase))
                    grouping = GroupingMode.ByGroup;
                else if (input.Equals("Flat", StringComparison.OrdinalIgnoreCase))
                    grouping = GroupingMode.Flat;
            }
            else if (getResult == GetResult.Cancel)
            {
                return Result.Cancel;
            }

            RhinoApp.WriteLine($"Grouping mode: {grouping}");
            RhinoApp.WriteLine($"Importing {filePaths.Length} file(s)...");

            // --- 3. Import all files in a single undo record ---
            var results = new List<ImportResult>();
            uint undoRecord = doc.BeginUndoRecord("IFC Multi-Import");

            try
            {
                var reader = new IfcModelReader();

                for (int i = 0; i < filePaths.Length; i++)
                {
                    string filePath = filePaths[i];
                    string fileName = Path.GetFileNameWithoutExtension(filePath);

                    RhinoApp.WriteLine($"Importing file {i + 1} of {filePaths.Length}: {Path.GetFileName(filePath)}...");

                    var options = new ImportOptions
                    {
                        Grouping = grouping,
                        FileIndex = i,
                        ParentLayerName = grouping == GroupingMode.ByFile ? fileName : null
                    };

                    var result = reader.Import(doc, filePath, options);
                    results.Add(result);

                    RhinoApp.WriteLine($"  {result.ElementCount} elements in {result.Duration.TotalSeconds:F1}s");

                    // ByGroup: add all objects from this file to a named group
                    if (grouping == GroupingMode.ByGroup && result.ObjectIds.Count > 0)
                    {
                        int groupIndex = doc.Groups.Add(fileName, result.ObjectIds);
                        RhinoApp.WriteLine($"  Group '{fileName}' created (index {groupIndex})");
                    }
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"IFC multi-import failed: {ex.Message}");
                RhinoApp.WriteLine(ex.StackTrace);
                doc.EndUndoRecord(undoRecord);
                return Result.Failure;
            }

            doc.EndUndoRecord(undoRecord);

            // --- 4. Print summary table ---
            PrintSummary(results);

            doc.Views.Redraw();
            return Result.Success;
        }

        private static void PrintSummary(List<ImportResult> results)
        {
            int nameWidth = Math.Max(20, results.Max(r => r.FileName.Length));
            string fmt = "{0}  {1,10}  {2,6}";

            RhinoApp.WriteLine();
            RhinoApp.WriteLine(string.Format(fmt,
                "File".PadRight(nameWidth), "Elements", "Time"));

            int totalElements = 0;
            double totalSeconds = 0;

            foreach (var r in results)
            {
                totalElements += r.ElementCount;
                totalSeconds += r.Duration.TotalSeconds;
                RhinoApp.WriteLine(string.Format(fmt,
                    r.FileName.PadRight(nameWidth),
                    r.ElementCount.ToString("N0"),
                    $"{r.Duration.TotalSeconds:F1}s"));
            }

            RhinoApp.WriteLine(new string('\u2500', nameWidth + 20));
            RhinoApp.WriteLine(string.Format(fmt,
                "Total".PadRight(nameWidth),
                totalElements.ToString("N0"),
                $"{totalSeconds:F1}s"));
            RhinoApp.WriteLine();
        }
    }
}
