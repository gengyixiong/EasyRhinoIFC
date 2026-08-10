using System;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace RhinoIfc.Export
{
    /// <summary>
    /// Initializes IFC project units to match the metre values written by the exporters.
    /// </summary>
    public static class IfcProjectUnits
    {
        public static void InitializeMetres(IIfcProject project)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));

            project.Initialize(ProjectUnits.SIUnitsUK);
            project.UnitsInContext.SetSiLengthUnits(IfcSIUnitName.METRE, null);
        }
    }
}
