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
        private readonly bool _enabled;
        private readonly string _path;
        private readonly ManualLogSource _log;
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly object _gate = new object();

        public DiscoveryLog(bool enabled, string path, ManualLogSource log)
        {
            _enabled = enabled;
            _path = path;
            _log = log;
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

        private void Record(string loaderKind, string? rawPath, string? dimensions)
        {
            if (!_enabled) return;
            if (string.IsNullOrEmpty(rawPath)) return;

            var key = loaderKind + "\0" + rawPath;
            lock (_gate)
            {
                if (!_seen.Add(key)) return;

                try
                {
                    var size = string.IsNullOrEmpty(dimensions) ? "" : $"  {dimensions}";
                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{loaderKind}]{size}  {rawPath}{Environment.NewLine}";
                    File.AppendAllText(_path, line);
                }
                catch (Exception e)
                {
                    _log.LogWarning($"discovery.log append failed: {e.Message}");
                }
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
