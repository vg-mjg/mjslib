using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace Mjslib.AssetSwap
{
    internal static class AssetReplace
    {
        internal static ReplacementRegistry? Registry;
        internal static TextureFactory? Textures;
        internal static SpriteFactory? Sprites;
        internal static AudioFactory? Audio;
        internal static TextAssetFactory? Texts;

        internal static DiscoveryLog? Discovery;

        internal static void RegisterAssemblyResolver()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssembly;
        }

        private static Assembly? ResolveEmbeddedAssembly(object? sender, ResolveEventArgs args)
        {
            var simpleName = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(simpleName)) return null;

            var resourceName = simpleName + ".dll";
            var self = Assembly.GetExecutingAssembly();
            using var stream = self.GetManifestResourceStream(resourceName);
            if (stream == null) return null;

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return Assembly.Load(ms.ToArray());
        }

        internal static UnityEngine.Texture? Resolve(string rawPath)
        {
            var registry = Registry;
            var textures = Textures;
            if (registry == null || textures == null) return null;

            var normalized = PathNormalizer.Normalize(rawPath);
            if (!registry.TryGet(normalized, out var entry)) return null;

            return textures.GetOrBuild(normalized, entry);
        }

        internal static UnityEngine.Sprite? ResolveSprite(string rawPath)
        {
            var registry = Registry;
            var sprites = Sprites;
            if (registry == null || sprites == null) return null;

            var normalized = PathNormalizer.Normalize(rawPath);
            if (!registry.TryGet(normalized, out var entry)) return null;

            return sprites.GetOrBuild(normalized, entry);
        }

        internal static bool ResolveAudio(
            string rawPath, string suffix, Il2CppSystem.Action<UnityEngine.AudioClip>? onCompleted)
        {
            var registry = Registry;
            var audio = Audio;
            if (registry == null || audio == null) return false;

            var normalized = PathNormalizer.Normalize(rawPath);
            if (!registry.TryGet(normalized, out var entry)) return false;

            return audio.TryReplace(rawPath, suffix, normalized, entry, onCompleted);
        }

        internal static UnityEngine.TextAsset? ResolveText(string rawPath)
        {
            var registry = Registry;
            var texts = Texts;
            if (registry == null || texts == null) return null;

            var normalized = PathNormalizer.Normalize(rawPath);
            if (!registry.TryGet(normalized, out var entry)) return null;

            return texts.GetOrBuildText(normalized, entry);
        }

        internal static byte[]? ResolveBytes(string rawPath)
        {
            var registry = Registry;
            var texts = Texts;
            if (registry == null || texts == null) return null;

            var normalized = PathNormalizer.Normalize(rawPath);
            if (!registry.TryGet(normalized, out var entry)) return null;

            return texts.GetOrBuildBytes(normalized, entry);
        }

        internal static Il2CppSystem.Action<UnityEngine.Texture>? WrapTextureDiscovery(
            string path, Il2CppSystem.Action<UnityEngine.Texture>? onComplete)
        {
            var discovery = Discovery;
            if (discovery == null || !discovery.Enabled || onComplete == null) return onComplete;

            var original = onComplete;
            return DelegateSupport.ConvertDelegate<Il2CppSystem.Action<UnityEngine.Texture>>(
                (Action<UnityEngine.Texture>)(texture =>
                {
                    discovery.RecordTexture("texture", path, texture);
                    original.Invoke(texture);
                }));
        }

        internal static Il2CppSystem.Action<UnityEngine.Sprite>? WrapSpriteDiscovery(
            string path, Il2CppSystem.Action<UnityEngine.Sprite>? onComplete)
        {
            var discovery = Discovery;
            if (discovery == null || !discovery.Enabled || onComplete == null) return onComplete;

            var original = onComplete;
            return DelegateSupport.ConvertDelegate<Il2CppSystem.Action<UnityEngine.Sprite>>(
                (Action<UnityEngine.Sprite>)(sprite =>
                {
                    discovery.RecordSprite("sprite", path, sprite);
                    original.Invoke(sprite);
                }));
        }
    }

    [HarmonyPatch(typeof(ResLoadMgr), nameof(ResLoadMgr.LoadTexture))]
    internal static class LoadTexturePatch
    {
        private static bool Prefix(string path, ref UnityEngine.Texture __result)
        {
            var tex = AssetReplace.Resolve(path);
            if (tex == null) return true;

            __result = tex;
            return false;
        }

        private static void Postfix(string path, UnityEngine.Texture? __result)
        {
            AssetReplace.Discovery?.RecordTexture("texture", path, __result);
        }
    }

    [HarmonyPatch(typeof(ResLoadMgr), nameof(ResLoadMgr.LoadTextureAsync))]
    internal static class LoadTextureAsyncPatch
    {
        private static bool Prefix(string path, ref Il2CppSystem.Action<UnityEngine.Texture>? onComplete)
        {
            onComplete = AssetReplace.WrapTextureDiscovery(path, onComplete);

            var tex = AssetReplace.Resolve(path);
            if (tex == null)
            {
                if (onComplete == null) AssetReplace.Discovery?.Record("texture", path);
                return true;
            }

            onComplete?.Invoke(tex);
            if (onComplete == null) AssetReplace.Discovery?.RecordTexture("texture", path, tex);
            return false;
        }
    }

    [HarmonyPatch(typeof(ResLoadMgr), nameof(ResLoadMgr.LoadSprite))]
    internal static class LoadSpritePatch
    {
        private static bool Prefix(string path, ref UnityEngine.Sprite __result)
        {
            var sprite = AssetReplace.ResolveSprite(path);
            if (sprite == null) return true;

            __result = sprite;
            return false;
        }

        private static void Postfix(string path, UnityEngine.Sprite? __result)
        {
            AssetReplace.Discovery?.RecordSprite("sprite", path, __result);
        }
    }

    [HarmonyPatch(typeof(ResLoadMgr), nameof(ResLoadMgr.LoadSpriteAsync))]
    internal static class LoadSpriteAsyncPatch
    {
        private static bool Prefix(string path, ref Il2CppSystem.Action<UnityEngine.Sprite>? onComplete)
        {
            onComplete = AssetReplace.WrapSpriteDiscovery(path, onComplete);

            var sprite = AssetReplace.ResolveSprite(path);
            if (sprite == null)
            {
                if (onComplete == null) AssetReplace.Discovery?.Record("sprite", path);
                return true;
            }

            onComplete?.Invoke(sprite);
            if (onComplete == null) AssetReplace.Discovery?.RecordSprite("sprite", path, sprite);
            return false;
        }
    }

    [HarmonyPatch(typeof(AudioLoaderManager), nameof(AudioLoaderManager.LoadClip),
        new[] { typeof(string), typeof(string), typeof(Il2CppSystem.Action<UnityEngine.AudioClip>) })]
    internal static class LoadClipPatch
    {
        private static bool Prefix(string path, string suffix, Il2CppSystem.Action<UnityEngine.AudioClip> onCompleted)
        {
            if (AssetReplace.Audio?.ConsumeFallthrough(path) == true) return true;

            AssetReplace.Discovery?.Record("audio", path);

            // skip the original once the replacement is handled
            return !AssetReplace.ResolveAudio(path, suffix, onCompleted);
        }
    }

    [HarmonyPatch(typeof(ResLoadMgr), nameof(ResLoadMgr.LoadText))]
    internal static class LoadTextPatch
    {
        private static bool Prefix(string path, ref UnityEngine.TextAsset __result)
        {
            AssetReplace.Discovery?.Record("text", path);

            var asset = AssetReplace.ResolveText(path);
            if (asset == null) return true;

            __result = asset;
            return false;
        }
    }

    [HarmonyPatch(typeof(ResLoadMgr), nameof(ResLoadMgr.LoadTextAsync))]
    internal static class LoadTextAsyncPatch
    {
        private static bool Prefix(string path, Il2CppSystem.Action<UnityEngine.TextAsset> onComplete)
        {
            AssetReplace.Discovery?.Record("text", path);

            var asset = AssetReplace.ResolveText(path);
            if (asset == null) return true;

            onComplete?.Invoke(asset);
            return false;
        }
    }

    [HarmonyPatch(typeof(ResLoadMgr), nameof(ResLoadMgr.LoadBytes))]
    internal static class LoadBytesPatch
    {
        private static bool Prefix(string path, ref Il2CppStructArray<byte> __result)
        {
            AssetReplace.Discovery?.Record("bytes", path);

            var bytes = AssetReplace.ResolveBytes(path);
            if (bytes == null) return true;

            __result = (Il2CppStructArray<byte>)bytes;
            return false;
        }
    }

    [HarmonyPatch(typeof(ResLoadMgr), nameof(ResLoadMgr.LoadBytesAsync))]
    internal static class LoadBytesAsyncPatch
    {
        private static bool Prefix(string path, Il2CppSystem.Action<Il2CppStructArray<byte>> onComplete)
        {
            AssetReplace.Discovery?.Record("bytes", path);

            var bytes = AssetReplace.ResolveBytes(path);
            if (bytes == null) return true;

            onComplete?.Invoke((Il2CppStructArray<byte>)bytes);
            return false;
        }
    }
}
