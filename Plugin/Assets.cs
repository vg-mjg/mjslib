using System;
using System.IO;
using System.Reflection;
using Mjslib.AssetSwap;

namespace Mjslib
{
    public sealed class AssetOptions
    {
        public (float X, float Y)? Pivot { get; set; }

        public float? Ppu { get; set; }

        public bool? Srgb { get; set; }
    }

    public static class Assets
    {
        public static void Replace(string gamePath, string filePath, AssetOptions? opts = null)
        {
            if (string.IsNullOrEmpty(gamePath)) throw new ArgumentException("gamePath must be non-empty", nameof(gamePath));
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("filePath must be non-empty", nameof(filePath));

            var log = Plugin.Instance?.Log;

            string resolvedFile;
            if (Path.IsPathRooted(filePath))
            {
                resolvedFile = Path.GetFullPath(filePath);
            }
            else
            {
                var callerDir = Path.GetDirectoryName(Assembly.GetCallingAssembly().Location);
                if (string.IsNullOrEmpty(callerDir))
                {
                    log?.LogWarning(
                        $"Mjslib.Assets.Replace('{gamePath}'): cannot resolve relative file '{filePath}' " +
                        "(calling assembly has no on-disk location); pass a rooted path instead.");
                    return;
                }

                resolvedFile = Path.GetFullPath(Path.Combine(callerDir, filePath));
            }

            var sourceLabel = "code:" + (Assembly.GetCallingAssembly().GetName().Name ?? "unknown");

            var registry = AssetReplace.Registry;
            if (registry == null)
            {
                log?.LogWarning(
                    $"Mjslib.Assets.Replace('{gamePath}') called before the asset registry was built; ignored.");
                return;
            }

            if (!File.Exists(resolvedFile))
            {
                log?.LogWarning(
                    $"Mjslib.Assets.Replace('{gamePath}') from {sourceLabel}: file not found '{resolvedFile}'; ignored.");
                return;
            }

            registry.AddReplacement(
                gamePath, resolvedFile, opts?.Srgb, opts?.Pivot, opts?.Ppu, sourceLabel, log!);
            log?.LogInfo($"Registered code asset replacement: '{gamePath}' -> '{resolvedFile}' ({sourceLabel})");
        }
    }
}
