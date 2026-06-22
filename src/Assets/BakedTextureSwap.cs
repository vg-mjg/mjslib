using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Mjslib.AssetSwap
{
    // replaces textures that live in prefab's materials
    // matches by AssetBundle container object identity
    internal sealed class BakedTextureSwap
    {
        // known texture slots to inspect
        private static readonly string[] TextureSlots =
        {
            "_MainTex", "_BaseMap", "_BaseColorMap", "_Tex", "_BumpMap", "_NormalMap",
            "_EmissionMap", "_SpecGlossMap", "_MetallicGlossMap", "_OcclusionMap",
            "_DetailAlbedoMap", "_DetailNormalMap", "_ParallaxMap",
        };

        private readonly ReplacementRegistry _registry;
        private readonly TextureFactory _textures;
        private readonly DiscoveryLog _discovery;
        private readonly ManualLogSource _log;

        private readonly HashSet<int> _scannedBundles = new HashSet<int>();
        private readonly Dictionary<int, SeedEntry> _seed = new Dictionary<int, SeedEntry>();

        public BakedTextureSwap(
            ReplacementRegistry registry, TextureFactory textures, DiscoveryLog discovery, ManualLogSource log)
        {
            _registry = registry;
            _textures = textures;
            _discovery = discovery;
            _log = log;
        }

        public bool ShouldScan => _registry.Count > 0 || _discovery.Enabled;

        public void OnPrefabLoaded(Transform? root)
        {
            if (root == null || !ShouldScan) return;

            try
            {
                ScanBundles();
                if (_registry.Count > 0) WalkPrefab(root);
            }
            catch (Exception e)
            {
                _log.LogError($"[mjslib] baked-texture swap failed: {e}");
            }
        }

        private void ScanBundles()
        {
            var bundles = Resources.FindObjectsOfTypeAll(Il2CppType.Of<AssetBundle>());
            if (bundles == null) return;

            for (int i = 0; i < bundles.Length; i++)
            {
                var bundle = bundles[i]?.TryCast<AssetBundle>();
                if (bundle == null) continue;
                if (!_scannedBundles.Add(bundle.GetInstanceID())) continue;

                SeedBundle(bundle);
            }
        }

        private void SeedBundle(AssetBundle bundle)
        {
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray names;
            try
            {
                names = bundle.GetAllAssetNames();
            }
            catch (Exception e)
            {
                _log.LogWarning($"[mjslib] could not read asset names from bundle '{SafeName(bundle)}': {e.Message}");
                return;
            }
            if (names == null) return;

            for (int i = 0; i < names.Length; i++)
            {
                var rawKey = names[i];
                if (string.IsNullOrEmpty(rawKey)) continue;

                var normalized = PathNormalizer.Normalize(rawKey);

                // XXX: doesn't read dimensions just like the other probes
                if (_discovery.Enabled && PathNormalizer.HasTextureExtension(rawKey))
                    _discovery.RecordBakedTexture(normalized);

                if (!_registry.TryGet(normalized, out var entry)) continue;

                Texture? original;
                try
                {
                    original = bundle.LoadAsset(rawKey, Il2CppType.Of<Texture>())?.TryCast<Texture>();
                }
                catch (Exception e)
                {
                    _log.LogWarning($"[mjslib] LoadAsset('{rawKey}') failed: {e.Message}");
                    continue;
                }
                if (original == null) continue;

                var inherit = InheritFrom(original, entry);
                _seed[original.GetInstanceID()] = new SeedEntry(normalized, entry, inherit);
                _log.LogInfo(
                    $"[mjslib] baked seed: '{normalized}' from container '{rawKey}' (instance {original.GetInstanceID()}); " +
                    $"inherited srgb={inherit.Srgb} wrap={inherit.Wrap} filter={inherit.Filter}");
            }
        }

        private static TextureImportSettings InheritFrom(Texture original, ReplacementEntry entry)
        {
            var wrap = entry.WrapExplicit ?? original.wrapMode;
            var filter = original.filterMode;
            var srgb = entry.SrgbExplicit ?? InferSrgb(original) ?? entry.Srgb;

            return new TextureImportSettings(srgb, wrap, filter);
        }

        private static bool? InferSrgb(Texture original)
        {
            string format;
            try { format = original.graphicsFormat.ToString(); }
            catch { return null; }

            if (format.IndexOf("SRGB", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (format.IndexOf("UNorm", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return null;
        }

        private void WalkPrefab(Transform root)
        {
            var go = root.gameObject;
            if (go == null) return;

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers == null) return;

            for (int r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                if (renderer == null) continue;

                var materials = renderer.sharedMaterials;
                if (materials == null) continue;

                for (int m = 0; m < materials.Length; m++)
                {
                    var mat = materials[m];
                    if (mat == null) continue;
                    SwapMaterial(mat);
                }
            }
        }

        private void SwapMaterial(Material mat)
        {
            foreach (var slot in TextureSlots)
            {
                if (!mat.HasProperty(slot)) continue;

                var tex = mat.GetTexture(slot);
                if (tex == null) continue;
                if (!_seed.TryGetValue(tex.GetInstanceID(), out var seed)) continue;

                var replacement = _textures.GetOrBuild(seed.Normalized, seed.Entry, seed.Inherit);
                if (replacement == null || replacement.GetInstanceID() == tex.GetInstanceID()) continue;

                mat.SetTexture(slot, replacement);
                _log.LogInfo(
                    $"[mjslib] baked swap: material '{SafeName(mat)}' slot {slot} to '{seed.Normalized}'");
            }
        }

        private static string SafeName(UnityEngine.Object o)
        {
            try { return o.name; } catch { return "<?>"; }
        }

        private readonly struct SeedEntry
        {
            public SeedEntry(string normalized, ReplacementEntry entry, TextureImportSettings inherit)
            {
                Normalized = normalized;
                Entry = entry;
                Inherit = inherit;
            }

            public string Normalized { get; }
            public ReplacementEntry Entry { get; }
            public TextureImportSettings Inherit { get; }
        }
    }
}
