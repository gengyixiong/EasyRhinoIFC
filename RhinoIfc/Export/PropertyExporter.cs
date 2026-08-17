using System.Collections.Generic;
using System.Linq;
using Rhino.DocObjects;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.PropertyResource;

namespace RhinoIfc.Export
{
    /// <summary>
    /// Exports Rhino object user strings as IFC property sets.
    ///
    /// User strings formatted as "PsetName.PropertyName" = "value"
    /// are grouped into IfcPropertySet instances.
    /// Strings without a dot separator go into a "RhinoProperties" pset.
    /// IFC-prefixed keys (IFC_GlobalId, IFC_Class, etc.) are skipped — those
    /// are handled by the element creation itself.
    /// </summary>
    public static class PropertyExporter
    {
        private static readonly HashSet<string> SkipKeys = new HashSet<string>
        {
            "IFC_GlobalId", "IFC_Name", "IFC_Class", "IFC_Description", "IFC_ObjectType"
        };

        public static void ExportUserStrings(
            IfcStore model,
            IIfcProduct element,
            RhinoObject rhinoObj,
            string sourceLayer)
        {
            var psets = new Dictionary<string, List<(string propName, string value)>>();
            if (!string.IsNullOrWhiteSpace(sourceLayer))
            {
                psets["RhinoProperties"] = new List<(string, string)>
                {
                    ("SourceLayer", sourceLayer)
                };
            }

            var userStrings = rhinoObj.Attributes.GetUserStrings();
            foreach (string key in userStrings?.AllKeys ?? new string[0])
            {
                if (SkipKeys.Contains(key)) continue;

                string value = userStrings[key];
                if (string.IsNullOrEmpty(value)) continue;

                string psetName;
                string propName;

                int dotIdx = key.IndexOf('.');
                if (dotIdx > 0)
                {
                    psetName = key.Substring(0, dotIdx);
                    propName = key.Substring(dotIdx + 1);
                }
                else
                {
                    psetName = "RhinoProperties";
                    propName = key;
                }

                if (psetName == "RhinoProperties" && propName == "SourceLayer" &&
                    !string.IsNullOrWhiteSpace(sourceLayer))
                    continue;

                if (!psets.ContainsKey(psetName))
                    psets[psetName] = new List<(string, string)>();

                psets[psetName].Add((propName, value));
            }

            // Create IFC property sets
            foreach (var kvp in psets)
            {
                var ifcPset = model.Instances.New<IfcPropertySet>(ps =>
                {
                    ps.Name = kvp.Key;
                    foreach (var (propName, value) in kvp.Value)
                    {
                        ps.HasProperties.Add(model.Instances.New<IfcPropertySingleValue>(p =>
                        {
                            p.Name = propName;
                            // Try to parse as number, otherwise store as text
                            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out double numVal))
                            {
                                p.NominalValue = new IfcReal(numVal);
                            }
                            else
                            {
                                p.NominalValue = new IfcText(value);
                            }
                        }));
                    }
                });

                // Link pset to element
                model.Instances.New<IfcRelDefinesByProperties>(rel =>
                {
                    rel.RelatingPropertyDefinition = ifcPset;
                    rel.RelatedObjects.Add((IfcObjectDefinition)element);
                });
            }
        }
    }
}
