using System;

namespace RhinoIfc.Util
{
    /// <summary>
    /// Unit-scale composition that does not depend on Rhino or xBIM types.
    /// Kept separate so the conversion formula can be tested in isolation.
    /// </summary>
    public static class UnitScale
    {
        /// <summary>
        /// Compose a scale from source model units to target units.
        /// </summary>
        /// <param name="modelUnitsToMetres">Metres represented by one source model unit.</param>
        /// <param name="targetUnitsPerMetre">Target units represented by one metre.</param>
        public static double ModelToTarget(double modelUnitsToMetres, double targetUnitsPerMetre)
        {
            if (double.IsNaN(modelUnitsToMetres) || double.IsInfinity(modelUnitsToMetres) ||
                modelUnitsToMetres <= 0)
                throw new ArgumentOutOfRangeException(nameof(modelUnitsToMetres));

            if (double.IsNaN(targetUnitsPerMetre) || double.IsInfinity(targetUnitsPerMetre) ||
                targetUnitsPerMetre <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetUnitsPerMetre));

            return modelUnitsToMetres * targetUnitsPerMetre;
        }
    }
}
