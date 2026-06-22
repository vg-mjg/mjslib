using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using Tomlyn;
using Tomlyn.Model;
using UnityEngine;

namespace Mjslib.AssetSwap
{
    internal sealed class ReplacementEntry
    {
        public const bool DefaultSrgb = true;
        public const TextureWrapMode DefaultWrap = TextureWrapMode.Repeat;

        public ReplacementEntry(
            string gamePathRaw, string filePath, bool? srgb, string sourcePack,
            float pivotX, float pivotY, float ppu, TextureWrapMode? wrap)
        {
            GamePathRaw = gamePathRaw;
            FilePath = filePath;
            SrgbExplicit = srgb;
            SourcePack = sourcePack;
            PivotX = pivotX;
            PivotY = pivotY;
            Ppu = ppu;
            WrapExplicit = wrap;
        }

        public string GamePathRaw { get; }
        public string FilePath { get; }
        public string SourcePack { get; }

        public bool? SrgbExplicit { get; }
        public TextureWrapMode? WrapExplicit { get; }

        public bool Srgb => SrgbExplicit ?? DefaultSrgb;
        public TextureWrapMode Wrap => WrapExplicit ?? DefaultWrap;

        public float PivotX { get; }
        public float PivotY { get; }
        public float Ppu { get; }
    }

    internal sealed class ReplacementRegistry
    {
        private readonly Dictionary<string, ReplacementEntry> _byPath =
            new Dictionary<string, ReplacementEntry>(StringComparer.OrdinalIgnoreCase);

        public int Count => _byPath.Count;

        public bool TryGet(string normalizedPath, out ReplacementEntry entry) =>
            _byPath.TryGetValue(normalizedPath, out entry!);

        public void AddReplacement(
            string gamePath, string resolvedFile, bool? srgb,
            (float x, float y)? pivot, float? ppu, TextureWrapMode? wrap, string sourceLabel, ManualLogSource log)
        {
            var (pivotX, pivotY) = pivot ?? (DefaultPivot, DefaultPivot);
            var resolvedPpu = ppu is { } p && p > 0f ? p : DefaultPpu;

            var normalized = PathNormalizer.Normalize(gamePath);
            var entry = new ReplacementEntry(
                gamePath, resolvedFile, srgb, sourceLabel, pivotX, pivotY, resolvedPpu, wrap);

            if (_byPath.TryGetValue(normalized, out var existing))
            {
                log.LogWarning(
                    $"Duplicate game_path '{gamePath}': '{existing.SourcePack}' overridden by '{sourceLabel}' (last-wins)");
            }

            _byPath[normalized] = entry;
        }

        public static ReplacementRegistry Build(IReadOnlyList<DiscoveredPack> packs, ManualLogSource log)
        {
            var registry = new ReplacementRegistry();
            foreach (var pack in packs)
            {
                registry.LoadPack(pack, log);
            }

            log.LogInfo($"Registry built: {registry.Count} replacement(s) from {packs.Count} pack(s)");
            return registry;
        }

        private void LoadPack(DiscoveredPack pack, ManualLogSource log)
        {
            string text;
            try
            {
                text = File.ReadAllText(pack.TomlPath);
            }
            catch (Exception e)
            {
                log.LogError($"Could not read {pack.TomlPath}: {e.Message}");
                return;
            }

            var doc = Toml.Parse(text, pack.TomlPath);
            if (doc.HasErrors)
            {
                foreach (var diag in doc.Diagnostics)
                {
                    log.LogError($"TOML error in {pack.TomlPath}: {diag}");
                }
                return;
            }

            TomlTable model;
            try
            {
                model = doc.ToModel();
            }
            catch (Exception e)
            {
                log.LogError($"Could not build model for {pack.TomlPath}: {e.Message}");
                return;
            }

            if (!model.TryGetValue("replace", out var replaceObj) || replaceObj is not TomlTableArray entries)
            {
                log.LogWarning($"No [[replace]] entries in {pack.TomlPath}");
                return;
            }

            foreach (var rowObj in entries)
            {
                if (rowObj is not TomlTable row) continue;
                AddEntry(pack, row, log);
            }
        }

        private void AddEntry(DiscoveredPack pack, TomlTable row, ManualLogSource log)
        {
            var gamePath = GetString(row, "game_path");
            var file = GetString(row, "file");

            if (string.IsNullOrEmpty(gamePath) || string.IsNullOrEmpty(file))
            {
                log.LogWarning($"Skipping [[replace]] in {pack.TomlPath}: both game_path and file are required");
                return;
            }

            var srgb = GetBool(row, "srgb");
            var (pivotX, pivotY) = GetPivot(pack, row, log);
            var ppu = GetPpu(pack, row, log);
            var wrap = GetWrap(pack, row, log);

            var resolvedFile = Path.GetFullPath(Path.Combine(pack.BaseDir, file!));
            if (!File.Exists(resolvedFile))
            {
                log.LogWarning(
                    $"Dropping replacement for '{gamePath}' from {pack.TomlPath}: file not found '{resolvedFile}'");
                return;
            }

            var normalized = PathNormalizer.Normalize(gamePath);
            var entry = new ReplacementEntry(gamePath!, resolvedFile, srgb, pack.TomlPath, pivotX, pivotY, ppu, wrap);

            if (_byPath.TryGetValue(normalized, out var existing))
            {
                log.LogWarning(
                    $"Duplicate game_path '{gamePath}': '{existing.SourcePack}' overridden by '{pack.TomlPath}' (last-wins)");
            }

            _byPath[normalized] = entry;
        }

        private static string? GetString(TomlTable row, string key) =>
            row.TryGetValue(key, out var v) && v is string s ? s : null;

        private static bool? GetBool(TomlTable row, string key) =>
            row.TryGetValue(key, out var v) && v is bool b ? b : (bool?)null;

        private const float DefaultPivot = 0.5f;
        private const float DefaultPpu = 100f;

        private static (float x, float y) GetPivot(DiscoveredPack pack, TomlTable row, ManualLogSource log)
        {
            if (!row.TryGetValue("pivot", out var v)) return (DefaultPivot, DefaultPivot);
            if (v is TomlArray arr && arr.Count == 2 &&
                TryToFloat(arr[0], out var x) && TryToFloat(arr[1], out var y))
            {
                return (x, y);
            }

            log.LogWarning($"Ignoring invalid pivot for '{GetString(row, "game_path")}' in {pack.TomlPath} (expected [x, y]); using default center");
            return (DefaultPivot, DefaultPivot);
        }

        private static float GetPpu(DiscoveredPack pack, TomlTable row, ManualLogSource log)
        {
            if (!row.TryGetValue("ppu", out var v)) return DefaultPpu;
            if (TryToFloat(v, out var ppu) && ppu > 0f) return ppu;

            log.LogWarning($"Ignoring invalid ppu for '{GetString(row, "game_path")}' in {pack.TomlPath} (expected a positive number); using default {DefaultPpu}");
            return DefaultPpu;
        }

        private static TextureWrapMode? GetWrap(DiscoveredPack pack, TomlTable row, ManualLogSource log)
        {
            if (!row.TryGetValue("wrap", out var v)) return null;
            if (v is string s)
            {
                switch (s.Trim().ToLowerInvariant())
                {
                    case "repeat": return TextureWrapMode.Repeat;
                    case "clamp": return TextureWrapMode.Clamp;
                    case "mirror": return TextureWrapMode.Mirror;
                    case "mirror_once":
                    case "mirroronce": return TextureWrapMode.MirrorOnce;
                }
            }

            log.LogWarning($"Ignoring invalid wrap for '{GetString(row, "game_path")}' in {pack.TomlPath} (expected repeat, clamp, mirror, or mirror_once); using default {ReplacementEntry.DefaultWrap}");
            return null;
        }

        private static bool TryToFloat(object? v, out float result)
        {
            switch (v)
            {
                case long l: result = l; return true;
                case double d: result = (float)d; return true;
                default: result = 0f; return false;
            }
        }
    }
}
