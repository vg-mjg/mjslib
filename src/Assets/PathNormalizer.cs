using System;

namespace Mjslib.AssetSwap
{
    internal static class PathNormalizer
    {
        private static readonly string[] KnownExtensions = { ".png", ".jpg", ".jpeg" };
        private const string BundleRootPrefix = "myassets/";

        public static string Normalize(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            var s = raw!.Replace('\\', '/').Trim().ToLowerInvariant();

            if (s.StartsWith("./", StringComparison.Ordinal)) s = s.Substring(2);

            // AssetBundle container keys are "myassets/<path>.<ext>"
            // strip the root so they look like raw loader paths for consistency
            if (s.StartsWith(BundleRootPrefix, StringComparison.Ordinal))
                s = s.Substring(BundleRootPrefix.Length);

            foreach (var ext in KnownExtensions)
            {
                if (s.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    s = s.Substring(0, s.Length - ext.Length);
                    break;
                }
            }

            return s;
        }

        public static bool HasTextureExtension(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;

            foreach (var ext in KnownExtensions)
            {
                if (raw!.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }
}
