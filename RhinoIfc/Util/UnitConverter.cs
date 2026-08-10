using System;
using Rhino;
using Xbim.Common;

namespace RhinoIfc.Util
{
    /// <summary>
    /// Converts between Rhino document units, metres, and the declared units of an IFC model.
    /// </summary>
    public static class UnitConverter
    {
        /// <summary>
        /// Returns the scale factor to multiply Rhino coordinates by to get metres.
        /// Example: if doc is in millimetres, returns 0.001.
        /// </summary>
        public static double RhinoToMetres(UnitSystem unit)
        {
            return unit switch
            {
                UnitSystem.Millimeters  => 0.001,
                UnitSystem.Centimeters  => 0.01,
                UnitSystem.Meters       => 1.0,
                UnitSystem.Kilometers   => 1000.0,
                UnitSystem.Inches       => 0.0254,
                UnitSystem.Feet         => 0.3048,
                UnitSystem.Yards        => 0.9144,
                UnitSystem.Miles        => 1609.344,
                UnitSystem.Microns      => 1e-6,
                _                       => 1.0  // assume metres if unknown
            };
        }

        /// <summary>
        /// Returns the scale factor to multiply IFC metres by to get Rhino doc units.
        /// Example: if doc is in millimetres, returns 1000.
        /// </summary>
        public static double MetresToRhino(UnitSystem unit)
        {
            return 1.0 / RhinoToMetres(unit);
        }

        /// <summary>
        /// Returns the scale factor from the IFC model's declared length unit to
        /// the active Rhino document unit. xBIM geometry and raw IFC measures are
        /// expressed in model units, which are not necessarily metres.
        /// </summary>
        public static double IfcModelToRhino(IModel model, UnitSystem rhinoUnit)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            return UnitScale.ModelToTarget(
                model.ModelFactors.LengthToMetresConversionFactor,
                MetresToRhino(rhinoUnit));
        }
    }
}
