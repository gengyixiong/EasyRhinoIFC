using Xbim.Ifc;
using Xbim.Ifc4.GeometricConstraintResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.RepresentationResource;

namespace RhinoIfc.Export
{
    internal static class MappedRepresentationFactory
    {
        internal static IfcRepresentationMap CreateMap(
            IfcStore model,
            IfcShapeRepresentation representation)
        {
            return model.Instances.New<IfcRepresentationMap>(map =>
            {
                map.MappingOrigin = model.Instances.New<IfcAxis2Placement3D>(placement =>
                {
                    placement.Location = model.Instances.New<IfcCartesianPoint>(point => point.SetXYZ(0, 0, 0));
                });
                map.MappedRepresentation = representation;
            });
        }

        internal static IfcShapeRepresentation CreateOccurrence(
            IfcStore model,
            IfcGeometricRepresentationContext context,
            IfcRepresentationMap map,
            IfcCartesianTransformationOperator3D transform)
        {
            var item = model.Instances.New<IfcMappedItem>(mapped =>
            {
                mapped.MappingSource = map;
                mapped.MappingTarget = transform;
            });

            return model.Instances.New<IfcShapeRepresentation>(representation =>
            {
                representation.ContextOfItems = context;
                representation.RepresentationIdentifier = "Body";
                representation.RepresentationType = "MappedRepresentation";
                representation.Items.Add(item);
            });
        }
    }
}
