using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace Mjslib.AssetSwap
{
    internal sealed class DiscoveryLog
    {
        private const string TextureKind = "texture";

        private readonly bool _enabled;
        private readonly string _path;
        private readonly ManualLogSource _log;
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _bakedLines = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly object _gate = new object();

        public DiscoveryLog(bool enabled, string path, ManualLogSource log)
        {
            _enabled = enabled;
            _path = path;
            _log = log;

            if (_enabled) ResetForCurrentBoot();
        }

        public bool Enabled => _enabled;

        public void Record(string loaderKind, string? rawPath)
        {
            Record(loaderKind, rawPath, null);
        }

        public void RecordTexture(string loaderKind, string? rawPath, Texture? texture)
        {
            Record(loaderKind, rawPath, TextureDimensions(texture));
        }

        public void RecordSprite(string loaderKind, string? rawPath, Sprite? sprite)
        {
            Record(loaderKind, rawPath, SpriteDimensions(sprite));
        }

        public void RecordBakedTexture(string? normalizedPath)
        {
            if (!_enabled) return;
            if (string.IsNullOrEmpty(normalizedPath)) return;

            var path = PathNormalizer.Normalize(normalizedPath);
            if (string.IsNullOrEmpty(path)) return;

            var key = Key(TextureKind, path);
            lock (_gate)
            {
                if (!_seen.Add(key)) return;

                var line = CreateLine(TextureKind, path, null);
                _bakedLines[key] = line;
                AppendLine(line);
            }
        }

        private void Record(string loaderKind, string? rawPath, string? dimensions)
        {
            if (!_enabled) return;
            if (string.IsNullOrEmpty(rawPath)) return;

            var dedupPath = loaderKind == TextureKind
                ? PathNormalizer.Normalize(rawPath)
                : rawPath!;
            var key = Key(loaderKind, dedupPath);
            lock (_gate)
            {
                var line = CreateLine(loaderKind, rawPath!, dimensions);
                if (_seen.Add(key))
                    AppendLine(line);
                else if (loaderKind == TextureKind && _bakedLines.TryGetValue(key, out var bakedLine))
                    ReplaceBakedLine(key, bakedLine, line);
            }
        }

        private static string Key(string loaderKind, string path) =>
            loaderKind + "\0" + path;

        private static string CreateLine(string loaderKind, string path, string? dimensions)
        {
            var size = string.IsNullOrEmpty(dimensions) ? "" : $"  {dimensions}";
            return $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{loaderKind}]{size}  {path}{Environment.NewLine}";
        }

        private void AppendLine(string line)
        {
            try
            {
                File.AppendAllText(_path, line);
            }
            catch (Exception e)
            {
                _log.LogWarning($"discovery.log append failed: {e.Message}");
            }
        }

        private void ResetForCurrentBoot()
        {
            try
            {
                File.WriteAllText(_path, string.Empty);
            }
            catch (Exception e)
            {
                _log.LogWarning($"discovery.log reset failed: {e.Message}");
            }
        }

        private void ReplaceBakedLine(string key, string previous, string current)
        {
            try
            {
                var text = File.Exists(_path) ? File.ReadAllText(_path) : string.Empty;
                var index = text.LastIndexOf(previous, StringComparison.Ordinal);
                text = index >= 0
                    ? text.Substring(0, index) + current + text.Substring(index + previous.Length)
                    : text + current;
                File.WriteAllText(_path, text);
                _bakedLines.Remove(key);
            }
            catch (Exception e)
            {
                _log.LogWarning($"discovery.log rewrite failed: {e.Message}");
            }
        }

        private string? TextureDimensions(Texture? texture)
        {
            if (texture == null) return null;
            try
            {
                return $"{texture.width}x{texture.height}";
            }
            catch (Exception e)
            {
                _log.LogWarning($"discovery.log texture dimension read failed: {e.Message}");
                return null;
            }
        }

        private string? SpriteDimensions(Sprite? sprite)
        {
            if (sprite == null) return null;
            try
            {
                var rect = sprite.rect;
                return $"{FormatDimension(rect.width)}x{FormatDimension(rect.height)}";
            }
            catch (Exception e)
            {
                _log.LogWarning($"discovery.log sprite dimension read failed: {e.Message}");
                return null;
            }
        }

        private static string FormatDimension(float value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
