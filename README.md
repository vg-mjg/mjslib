# mjslib

mjslib is a BepInEx 6 (IL2CPP) plugin for the Mahjong Soul client that simplifies mod development.
It adds:

- [**Asset replacement**](#asset-replacement)
- [**Lua hooks**](#lua-hooks)
- [**Config bindings**](#config-bindings)
- [**C# API**](#c-api)

## tl;dr

1. Download `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.*.zip` from [here](https://builds.bepinex.dev/projects/bepinex_be)
2. Unzip it into the Mahjong Soul directory
3. Download [`Mjslib.dll`](https://github.com/vg-mjg/mjslib/releases)
4. Copy it to `<GameDir>/BepInEx/plugins`
5. Copy some mods into `<GameDir>/BepInEx/plugins`
6. Run the game

## Installation

To begin using mjslib, you'll first need to get yourself a copy of [BepInEx 6 Unity IL2CPP win x64](https://builds.bepinex.dev/projects/bepinex_be).

Extract it into the game directory, so that the `winhttp.dll` sits next to `Jantama_MahjongSoul.exe`.

If you are using wine/proton, you'll need to bully it into loading this dll with an environment variable `WINEDLLOVERRIDES="winhttp=n,b"`.

Run the game to ensure BepInEx is being loaded. It should emit a log to `BepInEx/LogOutput.log`.
First run will take a while, so be patient.

Download [`Mjslib.dll` from releases](https://github.com/vg-mjg/mjslib/releases) and copy it to `<GameDir>/BepInEx/plugins`.
Now when you launch the game, `BepInEx/LogOutput.log` should contain: `[Info: mjslib] mjslib 0.1.0 loading`.

## Build

If you are interested in developing mjslib or would simply prefer to build it from source, first launch the game with just BepInEx to obtain unhollowed binaries.

Use the dotnet toolchain (>=net6.0) to build it.

```sh
dotnet build Plugin/Plugin.csproj -c Release -p:BepInExRoot=/path/to/BepInEx
```

For a local `BepInExRoot` default, copy `Directory.Build.props.example` to `Directory.Build.props` and edit the `BepInExRoot` path inside.

## Asset replacement

Asset packs can serve local texture, sprite, audio, and text-data files in place of game assets by path.
It roughly functions just like MajsoulPlus resource packs.
See `example/resource_pack` for a complete pack example with files, which you can just copy into `<GameDir>/BepInEx/plugins/`.

To make one, create a text file under `<GameDir>/BepInEx/plugins/<ModName>/assets.toml` where `<ModName>` is the name of your asset pack. This file's structure is as follows:

```toml
# create any amount of [[replace]] blocks, one for each swapped asset

# texture/sprite replacement
[[replace]]
# example path for Ichihime's default skin full body art
game_path = "deco/character/yiji/full/full"
# local file path to replace it with, relative to `assets.toml`
# PNG only
file = "ichihime-full.png"
# set srgb to false for linear textures such as normal maps
srgb = true # true by default anyway

# audio replacement
[[replace]]
# example path for the mouse click sound effect
game_path = "audio/audio_common/mouseclick"
# game's audio is in Vorbis format and we do no implicit conversion,
# so make sure to convert your audio to OGG Vorbis too
file = "click.ogg"

# replace one emote by exact emote path including its ID in the atlas
[[replace]]
# example path for Ichihime's first emote
# emotes are actually atlas images with all emotes bundled
# but we can replace a specific sprite from it directly
# instead of having to ship a whole atlas
# the trailing `0` references a specific sprite from this atlas
game_path = "deco/emo/e200001/common/0"
# PNG only
file = "ichihime-0.png"
# optionally change the center point
pivot = [0.5, 0.5]
# raise ppu with larger pngs to preserve SetNativeSize footprint
# here we are replacing the 120x120 emote with 240x240, so we double the default 100
# this is technically not necessary, but helps to ensure that the game
# properly fits scaled assets even in the contexts where they aren't being scaled
ppu = 200
# pivot and ppu only apply to sprite replacements

# text and byte replacements works the same way
[[replace]]
# example path for Ichihime's first story chapter
game_path = "docs/spots/yiji/100004_en.bytes"
file = "story.txt"
```

You may be wondering now: "How do I figure out what game paths to use?".
The simplest way is to enable `Discovery` in `<GameDir>/BepInEx/config/vg.mjg.mjslib.cfg`:

```ini
[Discovery]
Enabled = true
```

After launching the game, it will now log loaded asset paths to `<GameDir>/BepInEx/config/Mjslib.discovery.log`.
Use these paths directly as the `game_path` in `assets.toml`.

Note that textures and sprites also report the dimensions.
Typically you'll want your replacement assets to have the exact same size, or at least use the same aspect ratio.
In most cases the game will stretch the textures to fit.

For paths that aren't readily loadable for you in game (like characters and skins you don't own), you can decrypt the manifest using [Asset Meido](https://git.honk.li/czen/asset_meido):

```sh
./asset_meido decrypt '<GameDir>/Jantama_MahjongSoul_Data/StreamingAssets/StandaloneWindows64/AssetBundleConfig.json' > decrypted.json
```

Skip over to the `AssetsInfo` part of the output to find all game asset paths.
It's a tree whose keys build up the path and the `/f/` just means "files", so don't include that in your path.
Don't include the `_!_<bundleid>` suffix and the extension either, since the game typically requests assets without the extension and it won't match.

This still doesn't let you actually see the assets you are modifying.
You can run `Asset Meido` to download and extract the Unity Web version assets, which should have the exact same paths.

## Lua hooks

Most of Mahjong Soul's code is actually written in Lua.
mjslib makes modifying it way easier by providing Harmony-style Lua hooks.
You can create mods without touching dotnet, C#, BepInEx or IL2CPP binaries.

Make sure to check out the `example/`.

### Game sources

In order to actually do any useful work, first you need to know the code you are hooking.

For obvious reasons I can't just dump the full game source code here, but here's how you'd obtain it yourself.

Run [Asset Meido](https://git.honk.li/czen/asset_meido) with a filter for just Lua sources:

```sh
./asset_meido --asset-filter "LuaByte\/.*" all
```

This will download and extract Lua sources for the Unity Web version of the game, which are practically identical to what's shipped with the desktop/Steam client.

BTW, you can get original game assemblies (pre-IL2CPP) that decompile into quite usable C# from the standalone desktop launcher.
In case CatFood ever notices that and pulls them down, there's a copy of it on "The Repo".

### Lua mod discovery and load order

mjslib injects `MjsLua`, then scans one level deep:

```text
BepInEx/plugins/<mod>/mod.lua
```

The plugins root is appended to Lua `package.path`:

```text
BepInEx/plugins/?.lua
```

A mod directory doubles as its Lua namespace.
That means the directory name must also be a valid Lua identifier.
Mods are expected to `require` their own files through this namespace:

```text
BepInEx/plugins/my_mod/mod.lua
BepInEx/plugins/my_mod/helper.lua
```

```lua
local helper = require("my_mod.helper")
```

### Lua hook API

```lua
MjsLua.hook("Target.Path.Function", function(orig, ...)
  return orig(...)
end)
```

`orig(...)` calls the next hook layer, or the real original at the end of the chain.
The handler controls the returned values.

For `:` methods, `self` is the first vararg:

```lua
MjsLua.hook("EventHandler.AddListener", function(orig, self, eventName, listener)
  MjsLua.log("adding listener for " .. tostring(eventName))
  return orig(self, eventName, listener)
end)
```

Lua desugars `obj:method(x)` to `obj.method(obj, x)`, so handlers receive `(orig, self, x)`.

Use `MjsLua.log(...)` for mod logs. `MjsLua.warn(...)` and `MjsLua.error(...)` for warning/error level.

```lua
MjsLua.log("[mymod] info")
MjsLua.warn("[mymod] warn")
MjsLua.error("[mymod] error")
```

Each message is written to `BepInEx/LogOutput.log`.

### Target resolution

`MjsLua.hook("Holder.Path.Key", handler)` uses these rules:

1. Split on the last dot. `Holder.Path.Key` becomes holder `Holder.Path` and key `Key`.
2. A name with no dot targets a bare global function, so `Update` means `_G.Update`.
3. Resolve the holder as a path from `_G`, like `_G.Holder.Path`.
4. When the `_G` path is absent, resolve `package.loaded[holder]`.
5. Patch the slot only when the resolved holder is a table and `holder[key]` is a function.

Examples:

```lua
-- _G.Update
MjsLua.hook("Update", handler)

-- _G.ClientClock.AddListener
MjsLua.hook("ClientClock.AddListener", handler)

-- _G.list.push or package.loaded.list.push
MjsLua.hook("list.push", handler)
```

Use table form when a dotted string is ambiguous:

```lua
MjsLua.hook{ global = "Update", handler = fn }
MjsLua.hook{ table = "ClientClock", key = "AddListener", handler = fn }
MjsLua.hook{ module = "list", key = "push", handler = fn }
```

- `global` targets `_G[name]`.
- `table` plus `key` targets a holder path from `_G`, then `holder[key]`.
- `module` plus `key` targets `package.loaded[module][key]`.

Hook registration order is outermost-first. With two mods loaded in sorted path order, the earlier
path wraps the later path:

```lua
MjsLua.hook("T.K", function(orig, ...)
  MjsLua.log("A before")
  local result = orig(...)
  MjsLua.log("A after")
  return result
end)

MjsLua.hook("T.K", function(orig, ...)
  MjsLua.log("B before")
  local result = orig(...)
  MjsLua.log("B after")
  return result
end)
```

Call order:

```text
A before
B before
original
B after
A after
```

`MjsLua.hook` returns a handle. Pass it to `MjsLua.unhook(handle)` to remove that registration:

```lua
local handle = MjsLua.hook("Update", handler)
MjsLua.unhook(handle)
```

After a handler is removed, the trampoline remains installed and forwards calls through the remaining chain or to the original function.

## Config bindings

Mods can declare typed, persisted BepInEx-backed configuration.

```lua
local cfg = MjsLua.config("ConfigDemo", {
  Enabled = { default = true, desc = "toggle for the demo" },
  Volume = { default = 0.5, min = 0, max = 1, desc = "0..1 volume slider" },
  MaxItems = { default = 3, type = "int", min = 1, max = 99, desc = "integer count" },
  Greeting = { default = "hello", desc = "Free text" },
  Mode = { default = "fast", choices = { "fast", "slow" }, desc = "mode choice" },
  -- keybinds are still just strings
  Hotkey = { default = "LeftControl+G", keybind = true, desc = "demo hotkey" },
})
```

This will create `BepInEx/config/ConfigDemo.cfg` file with values synced to Lua values.

```lua
-- reflects ConfigEntry value at access time
if cfg.Enabled then ... end
-- sets and persists the entry
cfg.Mode = "slow"
-- bridged ConfigEntry.SettingChanged
MjsLua.onConfigChange(cfg, "Mode", function(v) ... end)
```

## C# API

Lua mods extend the game by wrapping Lua functions.
C# BepInEx plugins can also expose native C# functions to the game's Lua for capabilities outside the Lua sandbox.
The bridge lives in `Mjslib.Lua`.

Your plugin's `BasePlugin.Load` runs before the game's `LuaState` exists, so register inside a `Mjslib.Lua.Ready` callback:

```csharp
using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using LuaInterface;
using Mjslib;

[BepInPlugin("my.guid", "Wallhack", "1.0.0")]
[BepInDependency("vg.mjg.mjslib")]
public class MyPlugin : BasePlugin
{
    public override void Load()
    {
        Mjslib.Lua.Ready(OnLuaReady);
    }

    private void OnLuaReady(LuaInterface.LuaState luaState)
    {
        // pass the managed method directly
        // Lua.LuaNativeFunction is a plain CoreCLR delegate
        Mjslib.Lua.RegisterNativeFunction(luaState, "MyMod", "echo", Echo);
    }

    // reads its first argument off the Lua stack and pushes it back
    private static int Echo(IntPtr L)
    {
        var s = LuaDLL.lua_tostring(L, 1);
        LuaDLL.lua_pushstring(L, s ?? "");
        // number of return values pushed
        return 1;
    }
}
```

From Lua, the function is callable like so:

```lua
MjsLua.log(MyMod.echo("hi"))
```

Dependent DLL mods can use `Mjslib.Lua`, `Mjslib.Assets`, and `Mjslib.Config` to register Lua work, asset replacements, and config entries programmatically.

### Asset swap and Lua hook registration

A `.dll` mod can register everything from C# instead of relying on mjslib to load co-located files.

```csharp
// run an arbitrary Lua chunk, exactly as if it were a scanned mod.lua
// this is how you install a hook from C#: embed the hook Lua and load it
// the handler logic stays Lua
// chunkName defaults to @<your-assembly>
Mjslib.Lua.Load(string source, string chunkName = null);

// evaluate against the live state and return a typed result
T two = Mjslib.Lua.Eval<int>("return 1+1");

// register an asset replacement
// file paths are resolved next to the calling DLL
Mjslib.Assets.Replace("path/to/game_asset", "relative/file.png",
    new Mjslib.AssetOptions { Pivot = (0.5f, 0.5f), Ppu = 100f, Srgb = true });
```

`Lua.Load` chunks have no folder under `BepInEx/plugins`, so they get no namespace of their own.
Embed and register each file or inline them rather than relying on `require` for siblings.

## Future work

- [ ] Settings reloading (we have a callback for mods to react to changed settings, but nothing calls it)
- [ ] Keybind callbacks (so we don't have to rely on hooking `UpdateBeat.__call`)
- [ ] Hot reloading
- [ ] Declarative UI toolkit (it's a lot of messy work, so that one probably won't happen)
