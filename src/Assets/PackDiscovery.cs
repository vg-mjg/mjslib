using System;
using System.Collections.Generic;
using System.IO;

namespace Mjslib.AssetSwap
{
    internal sealed class DiscoveredPack
    {
        public DiscoveredPack(string tomlPath, string baseDir)
        {
            TomlPath = tomlPath;
            BaseDir = baseDir;
        }

        public string TomlPath { get; }
        public string BaseDir { get; }
    }

    internal static class PackDiscovery
    {
        public const string ConfigName = "assets.toml";

        public static List<DiscoveredPack> Discover(string pluginRoot)
        {
            var packs = new List<DiscoveredPack>();
            if (!Directory.Exists(pluginRoot)) return packs;

            foreach (var toml in Directory.EnumerateFiles(pluginRoot, ConfigName, SearchOption.AllDirectories))
            {
                var dir = Path.GetDirectoryName(toml)!;
                packs.Add(new DiscoveredPack(Path.GetFullPath(toml), Path.GetFullPath(dir)));
            }

            packs.Sort((a, b) => string.CompareOrdinal(a.TomlPath, b.TomlPath));
            return packs;
        }
    }
}
