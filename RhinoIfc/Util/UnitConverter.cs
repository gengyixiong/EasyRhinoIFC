using Rhino;

namespace RhinoIfc.Util
{
    public static class UnitConverter
    {
        public static double RhinoToMetres(UnitSystem unit)
        {
            return unit switch
            {
                UnitSystem.Millimeters => 0.001,
                UnitSystem.Centimeters => 0.01,
                UnitSystem.Meters => 1.0,
                UnitSystem.Kilometers => 1000.0,
                UnitSystem.Inches => 0.0254,
                UnitSystem.Feet => 0.3048,
                UnitSystem.Yards => 0.9144,
                UnitSystem.Miles => 1609.344,
                UnitSystem.Microns => 1e-6,
                _ => 1.0
            };
        }
    }
}
