using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using LuaInterface;
using Mjslib.AssetSwap;

namespace Mjslib
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "vg.mjg.mjslib";
        public const string Name = "mjslib";
        public const string Version = "0.1.0";

        internal static Plugin? Instance;

        public override void Load()
        {
            Instance = this;
            Log.LogInfo($"{Name} {Version} loading");
            var harmony = new Harmony(Guid);

            StartAssetReplace(harmony);
            StartLuaHook(harmony);
            StartLuaConfig();
        }

        private void StartLuaConfig()
        {
            Lua.Ready(state => global::Mjslib.Config.RegisterNatives(state));
            Log.LogInfo("Queued MjsLua config native registration for LuaState readiness");
        }

        private void StartAssetReplace(Harmony harmony)
        {
            // register embedded dependencies before pack discovery
            AssetReplace.RegisterAssemblyResolver();

            var discoveryEnabled = global::Mjslib.Config.Bind(
                Config, "Discovery", "Enabled", false,
                "Log every requested asset path to mjslib.discovery.log (in the BepInEx folder, next to "
                + "LogOutput.log) so you can find the game_path values to map. Default off.").Value;
            var discoveryPath = Path.Combine(Paths.BepInExRootPath, "mjslib.discovery.log");
            AssetReplace.Discovery = new DiscoveryLog(discoveryEnabled, discoveryPath, Log);
            if (discoveryEnabled) Log.LogInfo($"Discovery mode ON, logging requested paths to {discoveryPath}");

            var decoder = global::Mjslib.Config.Bind(
                Config, "Audio", "Decoder", AudioDecoder.UnityWebRequest,
                "How mapped OGG files are decoded. UnityWebRequest decodes via the engine over a "
                + "file:// URI (the default), falling back to NVorbis on failure. NVorbis forces the "
                + "fully-managed decoder, for builds where file:// UWR multimedia is unreliable.").Value;

            var packs = PackDiscovery.Discover(Paths.PluginPath);
            Log.LogInfo($"Discovered {packs.Count} pack(s) under {Paths.PluginPath}");

            AssetReplace.Registry = ReplacementRegistry.Build(packs, Log);
            AssetReplace.Textures = new TextureFactory(Log);
            AssetReplace.Sprites = new SpriteFactory(AssetReplace.Textures, Log);
            AssetReplace.Audio = new AudioFactory(decoder, Log);
            AssetReplace.Texts = new TextAssetFactory(Log);
            AssetReplace.Baked = new BakedTextureSwap(AssetReplace.Registry, AssetReplace.Textures, Log);

            harmony.PatchAll(typeof(LoadTexturePatch));
            harmony.PatchAll(typeof(LoadTextureAsyncPatch));
            harmony.PatchAll(typeof(LoadSpritePatch));
            harmony.PatchAll(typeof(LoadSpriteAsyncPatch));
            harmony.PatchAll(typeof(LoadClipPatch));
            harmony.PatchAll(typeof(LoadTextPatch));
            harmony.PatchAll(typeof(LoadTextAsyncPatch));
            harmony.PatchAll(typeof(LoadBytesPatch));
            harmony.PatchAll(typeof(LoadBytesAsyncPatch));
            harmony.PatchAll(typeof(LoadPrefabPatch));
            harmony.PatchAll(typeof(LoadPrefabAsyncPatch));
            Log.LogInfo(
                "Patched ResLoadMgr.LoadTexture/LoadTextureAsync, LoadSprite/LoadSpriteAsync, "
                + "LoadText/LoadTextAsync, LoadBytes/LoadBytesAsync, LoadPrefab/LoadPrefabAsync, "
                + $"and AudioLoaderManager.LoadClip (audio decoder: {decoder})");
        }

        private void StartLuaHook(Harmony harmony)
        {
            harmony.PatchAll(typeof(LuaStateStartPatch));
            Log.LogInfo("Patched LuaState.Start. Waiting for the game's LuaState to start");
        }
    }

    [HarmonyPatch(typeof(LuaState), nameof(LuaState.Start))]
    internal static class LuaStateStartPatch
    {
        private static bool _injected;

        private static void Postfix(LuaState __instance)
        {
            if (_injected) return;
            _injected = true;

            var log = Plugin.Instance!.Log;
            try
            {
                var bootstrap = ReadEmbeddedBootstrap();
                __instance.DoString(bootstrap, "@mjs-lua-hook");
                log.LogInfo("Injected MjsLua bootstrap into the game's LuaState");
            }
            catch (Exception e)
            {
                log.LogError($"Failed to inject MjsLua bootstrap: {e}");
                return;
            }

            // back MjsLua.log/warn/error with a synchronous native logger before any mod runs
            try
            {
                Lua.RegisterNativeFunction(__instance, "MjsLua", "_logNative", LogNative);
            }
            catch (Exception e)
            {
                log.LogError($"Failed to register MjsLua._logNative: {e}");
            }

            // let managed plugins register native lua functions first
            try
            {
                Lua.RaiseReady(__instance);
            }
            catch (Exception e)
            {
                log.LogError($"Mjslib.Lua.Ready dispatch failed: {e}");
            }

            // load scanned mods before queued code chunks
            LoadConsumerMods(__instance);
            Lua.RunQueuedChunks(__instance);
        }

        // MjsLua._logNative(level, msg): 1 = warning, 2 = error, anything else = info.
        private static int LogNative(IntPtr L)
        {
            var log = Plugin.Instance?.Log;
            if (log == null) return 0;

            var level = LuaDLL.lua_tointeger(L, 1);
            var msg = LuaDLL.lua_tostring(L, 2) ?? "";
            switch (level)
            {
                case 1: log.LogWarning(msg); break;
                case 2: log.LogError(msg); break;
                default: log.LogInfo(msg); break;
            }

            return 0;
        }

        private static string ReadEmbeddedBootstrap()
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("bootstrap.lua")
                ?? throw new InvalidOperationException("embedded bootstrap.lua not found");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static void LoadConsumerMods(LuaState luaState)
        {
            var log = Plugin.Instance!.Log;
            var pluginRoot = Paths.PluginPath;
            if (!Directory.Exists(pluginRoot))
            {
                log.LogInfo($"No consumer mods: {pluginRoot} does not exist");
                return;
            }

            var mods = DiscoverConsumerMods(pluginRoot);
            if (mods.Count == 0)
            {
                log.LogInfo($"No consumer mods found under {pluginRoot}");
                return;
            }

            // add the plugins root to package.path so a mod directory doubles as its lua namespace
            // require("my_mod.thing") resolves to <pluginRoot>/my_mod/thing.lua
            AddLuaPackagePath(luaState, pluginRoot);
            log.LogInfo($"Added mod package path root: {pluginRoot}");

            log.LogInfo($"Loading {mods.Count} consumer mod(s) in sorted path order");
            foreach (var mod in mods)
            {
                try
                {
                    var source = File.ReadAllText(mod.Path);
                    luaState.DoString(source, "@" + mod.Path);
                    log.LogInfo($"Loaded consumer mod: {mod.Path}");
                }
                catch (Exception e)
                {
                    log.LogError($"Failed to load consumer mod {mod.Path}: {e}");
                }
            }
        }

        private static List<ConsumerModFile> DiscoverConsumerMods(string pluginRoot)
        {
            // looks for all BepInEx/plugins/<mod>/mod.lua
            var mods = new List<ConsumerModFile>();
            foreach (var modDir in Directory.EnumerateDirectories(pluginRoot))
            {
                var path = Path.Combine(modDir, "mod.lua");
                if (File.Exists(path))
                {
                    mods.Add(new ConsumerModFile(path, modDir));
                }
            }

            mods.Sort((a, b) => StringComparer.Ordinal.Compare(a.Path, b.Path));
            return mods;
        }

        private static void AddLuaPackagePath(LuaState luaState, string root)
        {
            var template = Path.Combine(root, "?.lua");
            luaState.DoString(
                "local p = " + LuaQuote(template) + "\n" +
                "local current = package.path or ''\n" +
                "local needle = ';' .. current .. ';'\n" +
                "if not needle:find(';' .. p .. ';', 1, true) then\n" +
                "  package.path = current == '' and p or (current .. ';' .. p)\n" +
                "end",
                "@mjs-lua-hook-package-path");
        }

        private static string LuaQuote(string value)
        {
            return "\"" + value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n") + "\"";
        }

        private sealed class ConsumerModFile
        {
            public ConsumerModFile(string path, string directory)
            {
                Path = path;
                Directory = directory;
            }

            public string Path { get; }
            public string Directory { get; }
        }
    }

}
