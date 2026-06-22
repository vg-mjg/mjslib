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
        private readonly ManualLogSource _log;

        private readonly HashSet<int> _scannedBundles = new HashSet<int>();
        private readonly Dictionary<int, SeedEntry> _seed = new Dictionary<int, SeedEntry>();

        public BakedTextureSwap(ReplacementRegistry registry, TextureFactory textures, ManualLogSource log)
        {
            _registry = registry;
            _textures = textures;
            _log = log;
        }

        public bool Active => _registry.Count > 0;

        public void OnPrefabLoaded(Transform? root)
        {
            if (!Active || root == null) return;

            try
            {
                ScanBundles();
                WalkPrefab(root);
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

                _seed[original.GetInstanceID()] = new SeedEntry(normalized, entry);
                _log.LogInfo(
                    $"[mjslib] baked seed: '{normalized}' <- container '{rawKey}' (instance {original.GetInstanceID()})");
            }
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

                var replacement = _textures.GetOrBuild(seed.Normalized, seed.Entry);
                if (replacement == null || replacement.GetInstanceID() == tex.GetInstanceID()) continue;

                mat.SetTexture(slot, replacement);
                _log.LogInfo(
                    $"[mjslib] baked swap: material '{SafeName(mat)}' slot {slot} -> '{seed.Normalized}'");
            }
        }

        private static string SafeName(UnityEngine.Object o)
        {
            try { return o.name; } catch { return "<?>"; }
        }

        private readonly struct SeedEntry
        {
            public SeedEntry(string normalized, ReplacementEntry entry)
            {
                Normalized = normalized;
                Entry = entry;
            }

            public string Normalized { get; }
            public ReplacementEntry Entry { get; }
        }
    }
}
