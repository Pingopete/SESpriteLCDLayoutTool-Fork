using System.Collections.Generic;

namespace SESpriteLCDLayoutTool.Services
{
    public static class TemplateMerger
    {
        public sealed class MergeResult
        {
            public bool Success { get; set; }
            public bool UsedHostScope { get; set; }
            public string MergedCode { get; set; }
            public List<string> Notes { get; } = new List<string>();
        }

        public static MergeResult Merge(string hostCode, string snippet)
        {
            return new MergeResult { Success = false, UsedHostScope = false, MergedCode = hostCode };
        }
    }
}
