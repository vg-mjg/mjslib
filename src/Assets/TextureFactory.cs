using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace Mjslib.AssetSwap
{
    // import settings inherited from the original game texture
    internal readonly struct TextureImportSettings
    {
        public TextureImportSettings(bool srgb, TextureWrapMode wrap, FilterMode filter)
        {
            Srgb = srgb;
            Wrap = wrap;
            Filter = filter;
        }

        public bool Srgb { get; }
        public TextureWrapMode Wrap { get; }
        public FilterMode Filter { get; }
    }

    internal sealed class TextureFactory
    {
        private readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly ManualLogSource _log;

        public TextureFactory(ManualLogSource log)
        {
            _log = log;
        }

        public Texture2D? GetOrBuild(string normalizedPath, ReplacementEntry entry, TextureImportSettings? inherit = null)
        {
            if (_cache.TryGetValue(normalizedPath, out var cached))
            {
                // rebuild unity-destroyed cached textures
                if (cached != null) return cached;
                _cache.Remove(normalizedPath);
            }

            var srgb = inherit?.Srgb ?? entry.Srgb;
            var wrap = inherit?.Wrap ?? entry.Wrap;
            var filter = inherit?.Filter ?? FilterMode.Bilinear;

            try
            {
                var bytes = File.ReadAllBytes(entry.FilePath);

                // unity uses linear=false for srgb textures
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear: !srgb);

                var data = (Il2CppStructArray<byte>)bytes;
                if (!ImageConversion.LoadImage(tex, data, markNonReadable: false))
                {
                    UnityEngine.Object.Destroy(tex);
                    _log.LogError(
                        $"[mjslib] failed to build {entry.FilePath} for {entry.GamePathRaw}: undecodable image");
                    return null;
                }

                tex.filterMode = filter;
                tex.wrapMode = wrap;
                // hide the texture from unity cleanup
                tex.hideFlags = HideFlags.HideAndDontSave;

                _cache[normalizedPath] = tex;
                return tex;
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
