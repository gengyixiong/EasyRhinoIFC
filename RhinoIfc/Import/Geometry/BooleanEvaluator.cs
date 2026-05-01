using System;
using System.Collections.Generic;
using Rhino.Geometry;
using Xbim.Ifc4.Interfaces;

namespace RhinoIfc.Import.Geometry
{
    /// <summary>
    /// Evaluates an IfcBooleanResult / IfcBooleanClippingResult tree by
    /// walking down the FirstOperand chain to the leaf solid, then replaying
    /// the SecondOperand cuts on top in order. Each level may produce
    /// multiple result Breps; we keep the largest. If a single boolean
    /// fails, we keep the previous result rather than failing the whole
    /// element (Rhino's boolean engine occasionally fails on degenerate
    /// half-space cuts where the IFC source is "almost" valid).
    /// </summary>
    internal static class BooleanEvaluator
    {
        public static Brep Evaluate(IIfcBooleanResult root, ConversionContext ctx)
        {
            if (root == null) return null;

            try
            {
                // Walk down to the leaf first operand, recording each operation
                // (operator + second operand) so we can replay them on the way up.
                var ops = new Stack<IIfcBooleanResult>();
                IIfcBooleanOperand current = root;
                int safety = 0;
                while (current is IIfcBooleanResult br && safety++ < 256)
                {
                    ops.Push(br);
                    current = br.FirstOperand;
                }

                // Convert the leaf (which is NOT a boolean result).
                var result = IfcGeometryConverter.ConvertOperand(current, ctx, BoundingBox.Empty);
                if (result == null) return null;

                while (ops.Count > 0)
                {
                    var op = ops.Pop();
                    var hostBox = result.GetBoundingBox(true);
                    var second = IfcGeometryConverter.ConvertOperand(op.SecondOperand, ctx, hostBox);
                    if (second == null) continue;

                    Brep[] outcome = null;
                    try
                    {
                        switch (op.Operator)
                        {
                            case IfcBooleanOperator.DIFFERENCE:
                                outcome = Brep.CreateBooleanDifference(result, second, ctx.Tolerance);
                                break;
                            case IfcBooleanOperator.UNION:
                                outcome = Brep.CreateBooleanUnion(new[] { result, second }, ctx.Tolerance);
                                break;
                            case IfcBooleanOperator.INTERSECTION:
                                outcome = Brep.CreateBooleanIntersection(result, second, ctx.Tolerance);
                                break;
                        }
                    }
                    catch
                    {
                        outcome = null;
                    }

                    if (outcome != null && outcome.Length > 0)
                    {
                        // Pick the largest piece by volume.
                        Brep best = outcome[0];
                        double bestVol = SafeVolume(best);
                        for (int i = 1; i < outcome.Length; i++)
                        {
                            double v = SafeVolume(outcome[i]);
                            if (v > bestVol) { best = outcome[i]; bestVol = v; }
                        }
                        result = best;
                    }
                    // else: keep the previous result (best-effort).
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        private static double SafeVolume(Brep b)
        {
            try
            {
                if (b == null || !b.IsValid) return 0;
                var vmp = VolumeMassProperties.Compute(b);
                return vmp != null ? Math.Abs(vmp.Volume) : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
