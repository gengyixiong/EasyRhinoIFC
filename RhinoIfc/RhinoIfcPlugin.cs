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
            RhinoApp.WriteLine("EasyRhinoIFC plugin loaded — command: IfcExport");
            return LoadReturnCode.Success;
        }
    }
}
