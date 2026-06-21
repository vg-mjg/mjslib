using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using LuaInterface;

namespace Mjslib
{
    public static class Lua
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int LuaNativeFunction(IntPtr luaState);

        private static readonly object Gate = new object();

        private static LuaState? _luaState;
        private static Action<LuaState>? _readyHandlers;

        // lua chunks wait for the consumer load pass
        private static readonly Queue<PendingChunk> PendingChunks = new Queue<PendingChunk>();
        private static bool _drained;

        // keep tolua callbacks alive for native calls
        private static readonly List<LuaNativeFunction> RootedFunctions = new List<LuaNativeFunction>();

        public static bool IsReady
        {
            get { lock (Gate) { return _luaState != null; } }
        }

        public static LuaState? CurrentLuaState
        {
            get { lock (Gate) { return _luaState; } }
        }

        public static void Ready(Action<LuaState> handler)
        {
            if (handler == null) return;

            LuaState? latched;
            lock (Gate)
            {
                _readyHandlers += handler;
                latched = _luaState;
            }

            // fire late subscribers right away
            if (latched != null)
            {
                Invoke(handler, latched);
            }
        }

        public static void Load(string source, string? chunkName = null)
        {
            if (string.IsNullOrEmpty(source)) return;
            chunkName ??= "@" + (Assembly.GetCallingAssembly().GetName().Name ?? "mjslib");

            LuaState? runNow;
            lock (Gate)
            {
                if (!_drained)
                {
                    PendingChunks.Enqueue(new PendingChunk(source, chunkName));
                    return;
                }
                runNow = _luaState;
            }

            RunChunk(runNow!, source, chunkName);
        }

        public static T Eval<T>(string source, string? chunkName = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            LuaState? luaState;
            lock (Gate)
            {
                luaState = _luaState;
            }

            if (luaState == null)
            {
                throw new InvalidOperationException(
                    "Mjslib.Lua.Eval called before the game's LuaState is ready. Guard with " +
                    "Mjslib.Lua.IsReady, or evaluate from inside a Mjslib.Lua.Ready callback.");
            }

            chunkName ??= "@" + (Assembly.GetCallingAssembly().GetName().Name ?? "mjslib-eval");
            return luaState.DoString<T>(source, chunkName);
        }

        public static void RegisterNativeFunction(string module, string name, LuaNativeFunction fn)
        {
            LuaState? luaState;
            lock (Gate)
            {
                luaState = _luaState;
            }

            if (luaState == null)
            {
                throw new InvalidOperationException(
                    "RegisterNativeFunction called before the game's LuaState is ready. " +
                    "Register inside a Mjslib.Lua.Ready callback.");
            }

            RegisterNativeFunction(luaState, module, name, fn);
        }

        public static void RegisterNativeFunction(LuaState luaState, string module, string name, LuaNativeFunction fn)
        {
            if (luaState == null) throw new ArgumentNullException(nameof(luaState));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name must be non-empty", nameof(name));
            if (fn == null) throw new ArgumentNullException(nameof(fn));

            lock (Gate)
            {
                RootedFunctions.Add(fn);
            }

            var fnPtr = Marshal.GetFunctionPointerForDelegate(fn);
            var l = luaState.L;

            // open each module segment before registering the function
            int opened = 0;
            luaState.BeginModule(null);
            opened++;
            try
            {
                if (!string.IsNullOrEmpty(module))
                {
                    foreach (var part in module.Split('.'))
                    {
                        if (part.Length == 0) continue;
                        luaState.BeginModule(part);
                        opened++;
                    }
                }

                LuaDLL.tolua_function(l, name, fnPtr);
            }
            finally
            {
                for (int i = 0; i < opened; i++)
                {
                    luaState.EndModule();
                }
            }
        }

        internal static void RaiseReady(LuaState luaState)
        {
            if (luaState == null) return;

            Delegate[]? handlers;
            lock (Gate)
            {
                if (_luaState != null) return;
                _luaState = luaState;
                handlers = _readyHandlers?.GetInvocationList();
            }

            if (handlers == null) return;
            foreach (var handler in handlers)
            {
                Invoke((Action<LuaState>)handler, luaState);
            }
        }

        internal static void RunQueuedChunks(LuaState luaState)
        {
            if (luaState == null) return;

            while (true)
            {
                PendingChunk chunk;
                lock (Gate)
                {
                    if (PendingChunks.Count == 0)
                    {
                        _drained = true;
                        return;
                    }
                    chunk = PendingChunks.Dequeue();
                }

                RunChunk(luaState, chunk.Source, chunk.ChunkName);
            }
        }

        private static void RunChunk(LuaState luaState, string source, string chunkName)
        {
            try
            {
                luaState.DoString(source, chunkName);
                Plugin.Instance?.Log.LogInfo($"Loaded code-registered Lua chunk: {chunkName}");
            }
            catch (Exception e)
            {
                // keep one bad chunk from stopping the pass
                Plugin.Instance?.Log.LogError($"Failed to load code-registered Lua chunk {chunkName}: {e}");
            }
        }

        private static void Invoke(Action<LuaState> handler, LuaState luaState)
        {
            try
            {
                handler(luaState);
            }
            catch (Exception e)
            {
                // keep one bad ready handler from stopping the pass
                Plugin.Instance?.Log.LogError($"Mjslib.Lua.Ready subscriber threw: {e}");
            }
        }

        private readonly struct PendingChunk
        {
            public PendingChunk(string source, string chunkName)
            {
                Source = source;
                ChunkName = chunkName;
            }

            public string Source { get; }
            public string ChunkName { get; }
        }
    }
}
