using System;

namespace Mjslib.AssetSwap
{
    internal static class PathNormalizer
    {
        private static readonly string[] KnownExtensions = { ".png", ".jpg", ".jpeg" };

        public static string Normalize(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            var s = raw!.Replace('\\', '/').Trim();

            if (s.StartsWith("./", StringComparison.Ordinal)) s = s.Substring(2);

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
    }
}
