using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace GH_RhinoIfc
{
    public class GH_RhinoIfcInfo : GH_AssemblyInfo
    {
        public override string Name => "RhinoIfc";
        public override string Description => "IFC4 export component for Grasshopper";
        public override Guid Id => new Guid("C9160010-5CE1-4E00-A000-C6A1A19BA001");
        public override string AuthorName => "RhinoIfc";
        public override string AuthorContact => "";
        public override Bitmap Icon => null;
    }
}
