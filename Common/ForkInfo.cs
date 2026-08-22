using System;

namespace MediaInfoKeeper.Common {
    internal static class ForkInfo {
        public const string VersionSuffix = "-Maxiaotu";
        public const string UpstreamRepoUrl = "https://github.com/honue/MediaInfoKeeper";
        public static readonly bool EnableUpstreamUpdateCheck = false;

        public static string FormatDisplayVersion(string baseVersion) {
            if (string.IsNullOrWhiteSpace(baseVersion)) return "未知";

            var trimmed = baseVersion.Trim();
            if (trimmed.EndsWith(VersionSuffix, StringComparison.OrdinalIgnoreCase)) return trimmed;

            return trimmed + VersionSuffix;
        }
    }
}
