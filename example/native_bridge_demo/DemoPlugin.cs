using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using LuaInterface;
using Mjslib;

namespace MjslibNativeDemo
{
    [BepInPlugin(Guid, "mjslib native bridge demo", "0.1.0")]
    [BepInDependency("vg.mjg.mjslib")]
    public class DemoPlugin : BasePlugin
    {
        public const string Guid = "vg.mjg.mjslib.native-demo";

        public override void Load()
        {
            Log.LogInfo("native-bridge demo loading; deferring registration to Lua.Ready");
            Lua.Ready(OnLuaReady);
        }

        private void OnLuaReady(LuaState luaState)
        {
            Lua.RegisterNativeFunction(luaState, "Demo", "echo", Echo);
            Lua.RegisterNativeFunction(luaState, "Demo", "add", Add);
            Log.LogInfo("registered Demo.echo and Demo.add");
        }

        private static int Echo(IntPtr L)
        {
            var s = LuaDLL.lua_tostring(L, 1);
            LuaDLL.lua_pushstring(L, s ?? string.Empty);
            return 1;
        }

        private static int Add(IntPtr L)
        {
            var a = LuaDLL.lua_tonumber(L, 1);
            var b = LuaDLL.lua_tonumber(L, 2);
            LuaDLL.lua_pushnumber(L, a + b);
            return 1;
        }
    }
}
