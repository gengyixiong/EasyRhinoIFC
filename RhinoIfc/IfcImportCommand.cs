using System;
using Rhino;
using Rhino.Commands;
using RhinoIfc.Import;

namespace RhinoIfc
{
    public class IfcImportCommand : Command
    {
        public override string EnglishName => "IfcImport";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var fd = new Rhino.UI.OpenFileDialog
            {
                Filter = "IFC files (*.ifc)|*.ifc|All files (*.*)|*.*",
                Title = "Import IFC"
            };

            if (!fd.ShowOpenDialog())
                return Result.Cancel;

            var filePath = fd.FileName;
            RhinoApp.WriteLine($"Importing: {filePath}");

            try
            {
                // Show detected schema version
                using (var probe = Xbim.Ifc.IfcStore.Open(filePath))
                {
                    RhinoApp.WriteLine($"Detected IFC schema: {probe.SchemaVersion}");
                }

                var reader = new IfcModelReader();
                int count = reader.Import(doc, filePath);

                doc.Views.Redraw();
                RhinoApp.WriteLine($"Imported {count} elements from IFC.");
                return Result.Success;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"IFC import failed: {ex.Message}");
                RhinoApp.WriteLine(ex.StackTrace);
                return Result.Failure;
            }
        }
    }
}
