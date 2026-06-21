using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace Mjslib.AssetSwap
{
    internal sealed class SpriteFactory
    {
        private readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private readonly TextureFactory _textures;
        private readonly ManualLogSource _log;

        public SpriteFactory(TextureFactory textures, ManualLogSource log)
        {
            _textures = textures;
            _log = log;
        }

        public Sprite? GetOrBuild(string normalizedPath, ReplacementEntry entry)
        {
            if (_cache.TryGetValue(normalizedPath, out var cached))
            {
                if (cached != null) return cached;
                _cache.Remove(normalizedPath);
            }

            var tex = _textures.GetOrBuild(normalizedPath, entry);
            if (tex == null) return null;

            try
            {
                var rect = new Rect(0f, 0f, tex.width, tex.height);
                var pivot = new Vector2(entry.PivotX, entry.PivotY);
                var sprite = Sprite.Create(tex, rect, pivot, entry.Ppu);
                if (sprite == null)
                {
                    _log.LogError(
                        $"[mjslib] failed to build {entry.FilePath} for {entry.GamePathRaw}: Sprite.Create returned null");
                    return null;
                }

                // hide the sprite from unity cleanup
                sprite.hideFlags = HideFlags.HideAndDontSave;

                _cache[normalizedPath] = sprite;
                return sprite;
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
