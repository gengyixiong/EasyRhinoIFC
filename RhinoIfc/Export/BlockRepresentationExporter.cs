using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Xbim.Ifc;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.PresentationOrganizationResource;
using Xbim.Ifc4.RepresentationResource;

namespace RhinoIfc.Export
{
    /// <summary>Creates cached IFC representation maps for safe, repeated blocks.</summary>
    internal sealed class BlockRepresentationExporter
    {
        private const double Tolerance = 1e-8;
        private readonly IfcStore _model;
        private readonly RhinoDoc _document;
        private readonly IfcGeometricRepresentationContext _context;
        private readonly double _unitScale;
        private readonly IReadOnlyDictionary<int, int> _useCounts;
        private readonly IDictionary<Guid, IfcPresentationLayerAssignment> _presentationLayers;
        private readonly Dictionary<int, IfcRepresentationMap> _maps = new Dictionary<int, IfcRepresentationMap>();

        public BlockRepresentationExporter(
            IfcStore model,
            RhinoDoc document,
            IfcGeometricRepresentationContext context,
            double unitScale,
            IReadOnlyDictionary<int, int> useCounts,
            IDictionary<Guid, IfcPresentationLayerAssignment> presentationLayers)
        {
            _model = model;
            _document = document;
            _context = context;
            _unitScale = unitScale;
            _useCounts = useCounts;
            _presentationLayers = presentationLayers;
        }

        public bool TryCreateMappedRepresentation(InstanceObject instance, out IfcShapeRepresentation representation)
        {
            representation = null;
            var definition = instance?.InstanceDefinition;
            if (definition == null || definition.IsDeleted ||
                !_useCounts.TryGetValue(definition.Index, out var useCount) || useCount < 2 ||
                !TryGetTransform(instance.InstanceXform, out var transform))
                return false;

            if (!_maps.TryGetValue(definition.Index, out var map))
            {
                var geometry = InstanceMeshExtractor.ExtractDefinition(instance);
                if (geometry == null) return false;

                try
                {
                    var definitionRepresentation = GeometryExporter.CreateRepresentation(
                        _model, _context, geometry, _unitScale);
                    if (definitionRepresentation == null) return false;

                    var items = definitionRepresentation.Items.OfType<IfcRepresentationItem>().ToArray();
                    for (var i = 0; i < geometry.Length && i < items.Length; i++)
                    {
                        var sourceObject = geometry[i].SourceObject;
                        if (sourceObject == null) continue;

                        ColorExporter.ApplyColor(_model, _document, sourceObject, items[i]);
                        int layerIndex = sourceObject.Attributes.LayerIndex;
                        if (layerIndex < 0 || layerIndex >= _document.Layers.Count) continue;

                        var layer = _document.Layers[layerIndex];
                        if (layer != null)
                            PresentationLayerExporter.Assign(
                                _model, _presentationLayers, layer.Id, layer.Name, items[i]);
                    }

                    map = MappedRepresentationFactory.CreateMap(_model, definitionRepresentation);
                    _maps.Add(definition.Index, map);
                }
                finally
                {
                    Dispose(geometry);
                }
            }

            representation = MappedRepresentationFactory.CreateOccurrence(
                _model, _context, map, CreateTransform(transform));
            return true;
        }

        private static bool TryGetTransform(Transform source, out MappingTransform result)
        {
            result = default;
            if (Math.Abs(source.M30) > Tolerance || Math.Abs(source.M31) > Tolerance ||
                Math.Abs(source.M32) > Tolerance || Math.Abs(source.M33 - 1) > Tolerance ||
                source.Determinant <= Tolerance)
                return false;

            var x = new Vector3d(source.M00, source.M10, source.M20);
            var y = new Vector3d(source.M01, source.M11, source.M21);
            var z = new Vector3d(source.M02, source.M12, source.M22);
            var scale = x.Length;
            if (scale <= Tolerance || Math.Abs(y.Length - scale) > Tolerance ||
                Math.Abs(z.Length - scale) > Tolerance ||
                Math.Abs(x * y) > Tolerance * scale * scale ||
                Math.Abs(x * z) > Tolerance * scale * scale ||
                Math.Abs(y * z) > Tolerance * scale * scale)
                return false;

            x /= scale;
            y /= scale;
            z /= scale;
            if (Vector3d.CrossProduct(x, y) * z < 1 - Tolerance) return false;

            result = new MappingTransform
            {
                Origin = new Point3d(source.M03, source.M13, source.M23),
                X = x,
                Y = y,
                Z = z,
                Scale = scale
            };
            return true;
        }

        private IfcCartesianTransformationOperator3D CreateTransform(MappingTransform source)
        {
            return _model.Instances.New<IfcCartesianTransformationOperator3D>(operation =>
            {
                operation.LocalOrigin = _model.Instances.New<IfcCartesianPoint>(point => point.SetXYZ(
                    source.Origin.X * _unitScale,
                    source.Origin.Y * _unitScale,
                    source.Origin.Z * _unitScale));
                operation.Axis1 = Direction(source.X);
                operation.Axis2 = Direction(source.Y);
                operation.Axis3 = Direction(source.Z);
                operation.Scale = source.Scale;
            });
        }

        private IfcDirection Direction(Vector3d vector)
        {
            return _model.Instances.New<IfcDirection>(direction =>
            {
                direction.DirectionRatios.Add(vector.X);
                direction.DirectionRatios.Add(vector.Y);
                direction.DirectionRatios.Add(vector.Z);
            });
        }

        private static void Dispose(IEnumerable<ExportGeometry> geometry)
        {
            if (geometry == null) return;
            foreach (var group in geometry)
                foreach (var mesh in group.Meshes)
                    mesh?.Dispose();
        }

        private struct MappingTransform
        {
            public Point3d Origin;
            public Vector3d X;
            public Vector3d Y;
            public Vector3d Z;
            public double Scale;
        }
    }
}
