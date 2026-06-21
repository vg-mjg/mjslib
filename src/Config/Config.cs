using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using LuaInterface;

namespace Mjslib
{
    // bepinex-backed config for lua and c# mods
    public static class Config
    {
        private static readonly object Gate = new object();

        private static readonly List<ConfigEntryInfo> EntryList = new List<ConfigEntryInfo>();

        private static readonly Dictionary<string, LuaConfigEntry> Store =
            new Dictionary<string, LuaConfigEntry>(StringComparer.Ordinal);

        private static readonly Dictionary<string, ConfigFile> Files =
            new Dictionary<string, ConfigFile>(StringComparer.Ordinal);

        // keep native delegates rooted for tolua
        private static readonly List<Lua.LuaNativeFunction> NativeRoots = new List<Lua.LuaNativeFunction>();

        public static IReadOnlyCollection<ConfigEntryInfo> Entries
        {
            get { lock (Gate) { return EntryList.ToArray(); } }
        }

        internal static void RegisterNatives(LuaState state)
        {
            Lua.LuaNativeFunction bind = ConfigBind;
            Lua.LuaNativeFunction get = ConfigGet;
            Lua.LuaNativeFunction set = ConfigSet;
            Lua.LuaNativeFunction onChange = ConfigOnChange;

            lock (Gate)
            {
                NativeRoots.Add(bind);
                NativeRoots.Add(get);
                NativeRoots.Add(set);
                NativeRoots.Add(onChange);
            }

            Lua.RegisterNativeFunction(state, "MjsLua", "_configBind", bind);
            Lua.RegisterNativeFunction(state, "MjsLua", "_configGet", get);
            Lua.RegisterNativeFunction(state, "MjsLua", "_configSet", set);
            Lua.RegisterNativeFunction(state, "MjsLua", "_onConfigChange", onChange);
        }

        public static ConfigEntry<T> Bind<T>(ConfigFile file, string section, string key, T defaultValue, string description)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            var entry = file.Bind(section, key, defaultValue, description);
            RecordCSharp(entry, "mjslib");
            return entry;
        }

        public static ConfigEntry<string> BindKeybind(ConfigFile file, string section, string key, string defaultChord, string description)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            var entry = file.Bind(section, key, defaultChord ?? "", new ConfigDescription(description));
            var info = new ConfigEntryInfo(
                "mjslib", entry.Definition.Section, entry.Definition.Key, "keybind",
                entry.Description?.Description ?? "", entry.DefaultValue, null, null, null, "csharp", entry);
            lock (Gate) EntryList.Add(info);
            return entry;
        }

        public static void RecordCSharp(ConfigEntryBase entry, string mod)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            var (min, max) = RangeOf(entry);
            var info = new ConfigEntryInfo(
                mod, entry.Definition.Section, entry.Definition.Key,
                ClassifyClrType(entry.SettingType), entry.Description?.Description ?? "",
                entry.DefaultValue, min, max, ChoicesOf(entry), "csharp", entry);
            lock (Gate) EntryList.Add(info);
        }

        private static int ConfigBind(IntPtr L)
        {
            try
            {
                var mod = LuaDLL.lua_tostring(L, 1);
                var key = ReadStringField(L, 2, "key");
                if (string.IsNullOrEmpty(mod) || string.IsNullOrEmpty(key)) return 0;

                var type = ReadStringField(L, 2, "type") ?? "string";
                var section = ReadStringField(L, 2, "section");
                if (string.IsNullOrEmpty(section)) section = mod;
                var desc = ReadStringField(L, 2, "desc") ?? "";
                var min = ReadNumberField(L, 2, "min");
                var max = ReadNumberField(L, 2, "max");
                var choices = ReadStringArrayField(L, 2, "choices");
                var def = ReadDefaultField(L, 2, type);

                BindEntry(mod, section!, key!, type, def, desc, min, max, choices);
            }
            catch (Exception e)
            {
                Plugin.Instance?.Log.LogError($"MjsLua._configBind failed: {e}");
            }

            return 0;
        }

        private static void BindEntry(string mod, string section, string key, string type,
            object def, string desc, double? min, double? max, List<string>? choices)
        {
            var file = GetOrCreateFile(mod);
            ConfigEntryBase entry;

            switch (type)
            {
                case "bool":
                    entry = file.Bind(section, key, def is bool b && b, new ConfigDescription(desc));
                    break;
                case "int":
                    {
                        AcceptableValueBase? av = (min.HasValue && max.HasValue)
                            ? new AcceptableValueRange<int>((int)min.Value, (int)max.Value)
                            : null;
                        entry = file.Bind(section, key, Convert.ToInt32(def), new ConfigDescription(desc, av));
                        break;
                    }
                case "number":
                    {
                        AcceptableValueBase? av = (min.HasValue && max.HasValue)
                            ? new AcceptableValueRange<double>(min.Value, max.Value)
                            : null;
                        entry = file.Bind(section, key, Convert.ToDouble(def), new ConfigDescription(desc, av));
                        break;
                    }
                case "choices":
                    {
                        var av = new AcceptableValueList<string>((choices ?? new List<string>()).ToArray());
                        entry = file.Bind(section, key, def?.ToString() ?? "", new ConfigDescription(desc, av));
                        break;
                    }
                case "keybind":
                    // keybinds are saved as editable chord strings
                    entry = file.Bind(section, key, def?.ToString() ?? "", new ConfigDescription(desc));
                    break;
                default:
                    entry = file.Bind(section, key, def?.ToString() ?? "", new ConfigDescription(desc));
                    break;
            }

            var record = new LuaConfigEntry(mod, key, type, entry);
            var info = new ConfigEntryInfo(
                mod, section, key, type, desc, entry.DefaultValue, min, max,
                choices, "lua", entry);

            lock (Gate)
            {
                var storeKey = StoreKey(mod, key);
                // re-declared entries keep their existing change handlers
                if (Store.TryGetValue(storeKey, out var existing))
                {
                    record.ChangeRefs.AddRange(existing.ChangeRefs);
                    EntryList.RemoveAll(i => i.Source == "lua" && i.Mod == mod && i.Key == key);
                }
                Store[storeKey] = record;
                EntryList.Add(info);
            }
        }

        private static int ConfigGet(IntPtr L)
        {
            try
            {
                var mod = LuaDLL.lua_tostring(L, 1);
                var key = LuaDLL.lua_tostring(L, 2);
                LuaConfigEntry? entry;
                lock (Gate) Store.TryGetValue(StoreKey(mod, key), out entry);

                if (entry == null) LuaDLL.lua_pushnil(L);
                else PushValue(L, entry);
            }
            catch (Exception e)
            {
                Plugin.Instance?.Log.LogError($"MjsLua._configGet failed: {e}");
                LuaDLL.lua_pushnil(L);
            }

            return 1;
        }

        private static int ConfigSet(IntPtr L)
        {
            try
            {
                var mod = LuaDLL.lua_tostring(L, 1);
                var key = LuaDLL.lua_tostring(L, 2);
                LuaConfigEntry? entry;
                lock (Gate) Store.TryGetValue(StoreKey(mod, key), out entry);
                if (entry == null) return 0;

                object value = entry.Type switch
                {
                    "bool" => LuaDLL.lua_toboolean(L, 3),
                    "int" => LuaDLL.lua_tointeger(L, 3),
                    "number" => LuaDLL.lua_tonumber(L, 3),
                    _ => LuaDLL.lua_tostring(L, 3),
                };

                // setting BoxedValue saves the config and fires change handlers
                entry.Entry.BoxedValue = value;
            }
            catch (Exception e)
            {
                Plugin.Instance?.Log.LogError($"MjsLua._configSet failed: {e}");
            }

            return 0;
        }

        private static int ConfigOnChange(IntPtr L)
        {
            try
            {
                var mod = LuaDLL.lua_tostring(L, 1);
                var key = LuaDLL.lua_tostring(L, 2);
                if (!LuaDLL.lua_isfunction(L, 3))
                {
                    Plugin.Instance?.Log.LogWarning("MjsLua._onConfigChange: handler is not a function");
                    return 0;
                }

                LuaConfigEntry? entry;
                lock (Gate) Store.TryGetValue(StoreKey(mod, key), out entry);
                if (entry == null) return 0;

                // copy the handler before luaL_ref pops it
                LuaDLL.lua_pushvalue(L, 3);
                int refId = LuaDLL.lua_ref(L);
                lock (Gate) entry.ChangeRefs.Add(refId);
            }
            catch (Exception e)
            {
                Plugin.Instance?.Log.LogError($"MjsLua._onConfigChange failed: {e}");
            }

            return 0;
        }

        // dispatch bepinex config changes to lua handlers
        private static void OnSettingChanged(object? sender, SettingChangedEventArgs args)
        {
            try
            {
                var changed = args.ChangedSetting;
                LuaConfigEntry? entry = null;
                int[] refs;
                lock (Gate)
                {
                    foreach (var e in Store.Values)
                    {
                        if (ReferenceEquals(e.Entry, changed)) { entry = e; break; }
                    }
                    if (entry == null || entry.ChangeRefs.Count == 0) return;
                    refs = entry.ChangeRefs.ToArray();
                }

                var state = Lua.CurrentLuaState;
                if (state == null) return;
                var L = state.L;

                foreach (var refId in refs)
                {
                    int status = LuaDLL.lua_pcall(L, 1, 0, 0);
                    if (status != 0)
                    {
                        var err = LuaDLL.lua_tostring(L, -1);
                        LuaDLL.lua_pop(L, 1);
                        Plugin.Instance?.Log.LogError(
                            $"onConfigChange handler for {entry.Mod}.{entry.Key} errored: {err}");
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Instance?.Log.LogError($"Config SettingChanged dispatch failed: {e}");
            }
        }

        private static ConfigFile GetOrCreateFile(string mod)
        {
            lock (Gate)
            {
                if (Files.TryGetValue(mod, out var existing)) return existing;
                var path = Path.Combine(Paths.ConfigPath, SanitizeFileName(mod) + ".cfg");
                var file = new ConfigFile(path, saveOnInit: true);
                file.SettingChanged += OnSettingChanged;
                Files[mod] = file;
                return file;
            }
        }

        private static void PushValue(IntPtr L, LuaConfigEntry entry)
        {
            var boxed = entry.Entry.BoxedValue;
            switch (entry.Type)
            {
                case "bool":
                    LuaDLL.lua_pushboolean(L, boxed is bool b && b);
                    break;
                case "int":
                    LuaDLL.lua_pushinteger(L, Convert.ToInt32(boxed));
                    break;
                case "number":
                    LuaDLL.lua_pushnumber(L, Convert.ToDouble(boxed));
                    break;
                default:
                    LuaDLL.lua_pushstring(L, boxed?.ToString() ?? "");
                    break;
            }
        }

        private static void PushField(IntPtr L, int tableIdx, string field)
        {
            // use an absolute table index because pushing the key shifts the stack
            LuaDLL.lua_pushstring(L, field);
            LuaDLL.lua_rawget(L, tableIdx);
        }

        private static string? ReadStringField(IntPtr L, int tableIdx, string field)
        {
            PushField(L, tableIdx, field);
            var value = LuaDLL.lua_isnil(L, -1) ? null : LuaDLL.lua_tostring(L, -1);
            LuaDLL.lua_pop(L, 1);
            return value;
        }

        private static double? ReadNumberField(IntPtr L, int tableIdx, string field)
        {
            PushField(L, tableIdx, field);
            double? value = LuaDLL.lua_isnil(L, -1) ? (double?)null : LuaDLL.lua_tonumber(L, -1);
            LuaDLL.lua_pop(L, 1);
            return value;
        }

        private static List<string>? ReadStringArrayField(IntPtr L, int tableIdx, string field)
        {
            PushField(L, tableIdx, field);
            if (!LuaDLL.lua_istable(L, -1))
            {
                LuaDLL.lua_pop(L, 1);
                return null;
            }

            var arrayIdx = LuaDLL.lua_gettop(L);
            var n = LuaDLL.lua_objlen(L, arrayIdx);
            var list = new List<string>(n);
            for (int i = 1; i <= n; i++)
            {
                LuaDLL.lua_rawgeti(L, arrayIdx, i);
                list.Add(LuaDLL.lua_tostring(L, -1));
                LuaDLL.lua_pop(L, 1);
            }

            LuaDLL.lua_pop(L, 1);
            return list;
        }

        private static object ReadDefaultField(IntPtr L, int tableIdx, string type)
        {
            PushField(L, tableIdx, "default");
            object value = type switch
            {
                "bool" => LuaDLL.lua_toboolean(L, -1),
                "int" => LuaDLL.lua_tointeger(L, -1),
                "number" => LuaDLL.lua_tonumber(L, -1),
                _ => LuaDLL.lua_tostring(L, -1),
            };
            LuaDLL.lua_pop(L, 1);
            return value;
        }

        private static string ClassifyClrType(Type t)
        {
            if (t == typeof(bool)) return "bool";
            if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)) return "int";
            if (t == typeof(float) || t == typeof(double)) return "number";
            if (t == typeof(string)) return "string";
            if (t.IsEnum) return "enum";
            return t.Name;
        }

        private static (double? min, double? max) RangeOf(ConfigEntryBase entry)
        {
            var acceptable = entry.Description?.AcceptableValues;
            if (acceptable == null) return (null, null);
            var t = acceptable.GetType();
            if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(AcceptableValueRange<>))
                return (null, null);
            try
            {
                var min = Convert.ToDouble(t.GetProperty("MinValue")!.GetValue(acceptable));
                var max = Convert.ToDouble(t.GetProperty("MaxValue")!.GetValue(acceptable));
                return (min, max);
            }
            catch
            {
                return (null, null);
            }
        }

        private static IReadOnlyList<string>? ChoicesOf(ConfigEntryBase entry)
        {
            if (entry.Description?.AcceptableValues is AcceptableValueList<string> list)
                return list.AcceptableValues.ToList();
            return null;
        }

        private static string StoreKey(string mod, string key) => mod + "\0" + key;

        private static string SanitizeFileName(string name)
        {
            var sb = new StringBuilder(name.Length);
            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in name)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            var sanitized = sb.ToString().Trim();
            return sanitized.Length == 0 ? "mod" : sanitized;
        }

        private sealed class LuaConfigEntry
        {
            public LuaConfigEntry(string mod, string key, string type, ConfigEntryBase entry)
            {
                Mod = mod;
                Key = key;
                Type = type;
                Entry = entry;
            }

            public string Mod { get; }
            public string Key { get; }
            public string Type { get; }
            public ConfigEntryBase Entry { get; }
            public List<int> ChangeRefs { get; } = new List<int>();
        }

    }
}
