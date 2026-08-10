using System;
using Rhino;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;
using RhinoIfc.Export;

namespace RhinoIfc
{
    public class IfcExportCommand : Command
    {
        public override string EnglishName => "IfcExport";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // Ask: export all or selection?
            var go = new GetOption();
            go.SetCommandPrompt("Export scope");
            int allIdx = go.AddOption("All");
            int selIdx = go.AddOption("Selected");
            go.Get();

            if (go.CommandResult() != Result.Success)
                return go.CommandResult();

            bool exportAll = (go.OptionIndex() == allIdx);

            // If exporting selection, make sure something is selected
            Rhino.DocObjects.RhinoObject[] objects;
            if (exportAll)
            {
                objects = doc.Objects.FindByFilter(
                    new Rhino.DocObjects.ObjectEnumeratorSettings
                    {
                        NormalObjects = true,
                        LockedObjects = false,
                        HiddenObjects = false,
                        ActiveObjects = true,
                        ReferenceObjects = false
                    });
            }
            else
            {
                var selected = doc.Objects.GetSelectedObjects(false, false);
                objects = System.Linq.Enumerable.ToArray(selected);

                if (objects.Length == 0)
                {
                    RhinoApp.WriteLine("No objects selected.");
                    return Result.Nothing;
                }
            }

            var fd = new Rhino.UI.SaveFileDialog
            {
                Filter = "IFC files (*.ifc)|*.ifc",
                Title = "Export IFC"
            };

            if (!fd.ShowSaveDialog())
                return Result.Cancel;

            try
            {
                var writer = new IfcModelWriter();
                int count = writer.Export(doc, objects, fd.FileName);
                RhinoApp.WriteLine($"Exported {count} elements to {fd.FileName} (IFC4)");
                return Result.Success;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"IFC export failed: {ex.Message}");
                RhinoApp.WriteLine(ex.StackTrace);
                return Result.Failure;
            }
        }
    }
}
