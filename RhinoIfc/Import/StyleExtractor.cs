using System.Drawing;
using System.Linq;
using Xbim.Common;
using Xbim.Ifc4.Interfaces;

namespace RhinoIfc.Import
{
    /// <summary>
    /// Extracts color/transparency from IFC surface styles
    /// and converts to System.Drawing.Color for Rhino.
    /// </summary>
    public static class StyleExtractor
    {
        /// <summary>
        /// Given a style label from a ShapeInstance, extract the RGBA color.
        /// Returns null if no valid color is found.
        /// </summary>
        public static Color? GetColor(IModel model, int styleLabel)
        {
            if (styleLabel <= 0) return null;

            var style = model.Instances[styleLabel] as IIfcSurfaceStyle;
            if (style == null) return null;

            // Look for rendering style first (has transparency), then shading
            var rendering = style.Styles.OfType<IIfcSurfaceStyleRendering>().FirstOrDefault();
            if (rendering?.SurfaceColour != null)
            {
                var c = rendering.SurfaceColour;
                double transparency = rendering.Transparency.HasValue ? (double)rendering.Transparency.Value : 0.0;
                int alpha = (int)(255 * (1.0 - transparency));
                return Color.FromArgb(
                    alpha,
                    Clamp(c.Red),
                    Clamp(c.Green),
                    Clamp(c.Blue));
            }

            var shading = style.Styles.OfType<IIfcSurfaceStyleShading>().FirstOrDefault();
            if (shading?.SurfaceColour != null)
            {
                var c = shading.SurfaceColour;
                double transparency = shading.Transparency.HasValue ? (double)shading.Transparency.Value : 0.0;
                int alpha = (int)(255 * (1.0 - transparency));
                return Color.FromArgb(
                    alpha,
                    Clamp(c.Red),
                    Clamp(c.Green),
                    Clamp(c.Blue));
            }

            return null;
        }

        /// <summary>
        /// IFC colour channels are 0.0–1.0 doubles → clamp to 0–255 int.
        /// </summary>
        private static int Clamp(double channel)
        {
            int v = (int)(channel * 255);
            if (v < 0) return 0;
            if (v > 255) return 255;
            return v;
        }
    }
}
