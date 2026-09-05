namespace RhinoIfc.Export
{
    internal enum ExportPostProcessingPath
    {
        Mapped,
        Object,
        Extracted
    }

    internal static class ExportPostProcessing
    {
        internal static ExportPostProcessingPath Select<T>(bool usesMappedGeometry, T[] exportGeometry)
        {
            if (usesMappedGeometry) return ExportPostProcessingPath.Mapped;
            return exportGeometry == null
                ? ExportPostProcessingPath.Object
                : ExportPostProcessingPath.Extracted;
        }
    }
}
