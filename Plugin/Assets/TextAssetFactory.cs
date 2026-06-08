using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace Mjslib.AssetSwap
{
    internal sealed class TextAssetFactory
    {
        private readonly Dictionary<string, byte[]> _bytesCache = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, TextAsset> _textCache = new Dictionary<string, TextAsset>(StringComparer.Ordinal);
        private readonly ManualLogSource _log;

        public TextAssetFactory(ManualLogSource log)
        {
            _log = log;
        }

        public byte[]? GetOrBuildBytes(string normalizedPath, ReplacementEntry entry)
        {
            if (_bytesCache.TryGetValue(normalizedPath, out var cached)) return cached;

            try
            {
                var bytes = File.ReadAllBytes(entry.FilePath);
                _bytesCache[normalizedPath] = bytes;
                return bytes;
            }
            catch (Exception e)
            {
                _log.LogError(
                    $"[mjslib] failed to build {entry.FilePath} for {entry.GamePathRaw}: {e.Message}");
                return null;
            }
        }

        public TextAsset? GetOrBuildText(string normalizedPath, ReplacementEntry entry)
        {
            if (_textCache.TryGetValue(normalizedPath, out var cached))
            {
                // rebuild unity-destroyed cached assets
                if (cached != null) return cached;
                _textCache.Remove(normalizedPath);
            }

            var bytes = GetOrBuildBytes(normalizedPath, entry);
            if (bytes == null) return null;

            try
            {
                var asset = new TextAsset(Encoding.UTF8.GetString(bytes));
                if (asset == null)
                {
                    _log.LogError(
                        $"[mjslib] failed to build {entry.FilePath} for {entry.GamePathRaw}: TextAsset ctor returned null");
                    return null;
                }

                // hide the asset from unity cleanup
                asset.hideFlags = HideFlags.HideAndDontSave;

                _textCache[normalizedPath] = asset;
                return asset;
            }
            catch (Exception e)
            {
                _log.LogError(
                    $"[mjslib] failed to build {entry.FilePath} for {entry.GamePathRaw}: {e.Message}");
                return null;
            }
        }
    }
}
