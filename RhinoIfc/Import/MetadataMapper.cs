using System.Linq;
using Rhino.DocObjects;
using Xbim.Ifc4.Interfaces;

namespace RhinoIfc.Import
{
    /// <summary>
    /// Attaches IFC metadata (GlobalId, Name, Class, PropertySets)
    /// as Rhino user strings on the imported object.
    /// </summary>
    public static class MetadataMapper
    {
        public static void AttachMetadata(RhinoObject obj, IIfcProduct product)
        {
            // Core identity
            obj.Attributes.SetUserString("IFC_GlobalId", product.GlobalId.ToString());
            obj.Attributes.SetUserString("IFC_Name", product.Name?.ToString() ?? "");
            obj.Attributes.SetUserString("IFC_Class", product.ExpressType.ExpressName);
            obj.Attributes.SetUserString("IFC_Description", product.Description?.ToString() ?? "");
            obj.Attributes.SetUserString("IFC_ObjectType", product.ObjectType?.ToString() ?? "");

            // Property sets → user strings as "PsetName.PropertyName" = value
            var relDefines = product.IsDefinedBy?.OfType<IIfcRelDefinesByProperties>() ??
                             Enumerable.Empty<IIfcRelDefinesByProperties>();

            foreach (var relDef in relDefines)
            {
                if (relDef.RelatingPropertyDefinition is IIfcPropertySet pset)
                {
                    string psetName = pset.Name?.ToString() ?? "Properties";

                    foreach (var prop in pset.HasProperties.OfType<IIfcPropertySingleValue>())
                    {
                        string key = $"{psetName}.{prop.Name}";
                        string val = prop.NominalValue?.ToString() ?? "";
                        obj.Attributes.SetUserString(key, val);
                    }
                }
            }

            // Quantities
            foreach (var relDef in relDefines)
            {
                if (relDef.RelatingPropertyDefinition is IIfcElementQuantity eq)
                {
                    string qName = eq.Name?.ToString() ?? "Quantities";
                    foreach (var q in eq.Quantities.OfType<IIfcPhysicalSimpleQuantity>())
                    {
                        string key = $"{qName}.{q.Name}";
                        string val = q switch
                        {
                            IIfcQuantityLength ql => ql.LengthValue.ToString(),
                            IIfcQuantityArea qa => qa.AreaValue.ToString(),
                            IIfcQuantityVolume qv => qv.VolumeValue.ToString(),
                            IIfcQuantityWeight qw => qw.WeightValue.ToString(),
                            IIfcQuantityCount qc => qc.CountValue.ToString(),
                            _ => ""
                        };
                        if (!string.IsNullOrEmpty(val))
                            obj.Attributes.SetUserString(key, val);
                    }
                }
            }

            obj.CommitChanges();
        }
    }
}
