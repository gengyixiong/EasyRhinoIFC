using System;
using System.Collections.Generic;

namespace RhinoIfc.Import
{
    public enum GroupingMode
    {
        ByFile,
        ByGroup,
        Flat
    }

    public class ImportOptions
    {
        public GroupingMode Grouping { get; set; } = GroupingMode.ByFile;
        public string ParentLayerName { get; set; }
        public int FileIndex { get; set; }
    }

    public class ImportResult
    {
        public int ElementCount { get; set; }
        public List<Guid> ObjectIds { get; set; } = new List<Guid>();
        public TimeSpan Duration { get; set; }
        public string FileName { get; set; }
    }
}
