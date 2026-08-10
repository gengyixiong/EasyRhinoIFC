using System.Collections.Generic;
using Rhino.Geometry;
using Xbim.Common;

namespace RhinoIfc.Import.Geometry
{
    /// <summary>
    /// Shared state passed through every parametric Brep converter call.
    /// Holds the source IFC model, the metres -> Rhino unit scale factor,
    /// the document tolerance, and a memoization cache for IfcRepresentationMap
    /// (so a family used 1000 times only converts its source geometry once).
    /// </summary>
    internal sealed class ConversionContext
    {
        public IModel Model { get; }

        /// <summary>
        /// Multiplier to convert the IFC model's declared length unit into the
        /// active Rhino document's unit system.
        /// </summary>
        public double UnitScale { get; }

        /// <summary>
        /// Absolute model tolerance to use for Brep construction
        /// (joining, intersections, etc.).
        /// </summary>
        public double Tolerance { get; }

        /// <summary>
        /// Cache of converted IfcRepresentationMap source geometry,
        /// keyed by EntityLabel. The cached Breps are in the map's
        /// own local coordinate system; MappingOrigin and MappingTarget
        /// are applied per instance after lookup.
        /// </summary>
        public Dictionary<int, List<Brep>> RepresentationMapCache { get; }
            = new Dictionary<int, List<Brep>>();

        public ConversionContext(IModel model, double unitScale, double tolerance)
        {
            Model = model;
            UnitScale = unitScale;
            Tolerance = tolerance;
        }
    }
}
