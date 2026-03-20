using System;
using Rhino;
using Rhino.PlugIns;

namespace RhinoIfc
{
    public class RhinoIfcPlugin : PlugIn
    {
        public static RhinoIfcPlugin Instance { get; private set; }

        public RhinoIfcPlugin()
        {
            Instance = this;
        }

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            // Eagerly load the xBIM geometry engine so that Xbim3DModelContext
            // can find it later during IFC import.  This also validates that the
            // native Engine DLLs (Xbim.Geometry.Engine64.dll etc.) are deployed
            // alongside the plugin.
            try
            {
                var engine = new Xbim.Geometry.Engine.Interop.XbimGeometryEngine();
                RhinoApp.WriteLine("RhinoIfc: xBIM Geometry engine loaded successfully.");
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"RhinoIfc: WARNING — xBIM Geometry engine failed to load: {ex.Message}");
                RhinoApp.WriteLine("IFC import may not work. Ensure native DLLs are present.");
            }

            RhinoApp.WriteLine("RhinoIfc plugin loaded — commands: IfcImport, IfcExport");
            return LoadReturnCode.Success;
        }
    }
}
