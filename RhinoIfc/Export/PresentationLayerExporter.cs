using System;
using System.Collections.Generic;
using Xbim.Ifc;
using Xbim.Ifc4.PresentationOrganizationResource;

namespace RhinoIfc.Export
{
    public static class PresentationLayerExporter
    {
        public static void Assign(
            IfcStore model,
            IDictionary<Guid, IfcPresentationLayerAssignment> assignments,
            Guid layerId,
            string layerName,
            IfcLayeredItem item)
        {
            if (!assignments.TryGetValue(layerId, out var assignment))
            {
                assignment = model.Instances.New<IfcPresentationLayerAssignment>(layer =>
                {
                    layer.Name = layerName;
                });
                assignments[layerId] = assignment;
            }

            assignment.AssignedItems.Add(item);
        }
    }
}
