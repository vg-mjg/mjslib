if MjsLua then
	return
end

local unpack = table.unpack or unpack
local registry = {}
local wrapped = setmetatable({}, { __mode = "k" })
local pending = {}
local configMeta = setmetatable({}, { __mode = "k" })

local function log(msg)
	print("[MjsLua] " .. msg)
end

local function pack(...)
	return { n = select("#", ...), ... }
end

local function resolvePathFromG(holderName)
	if holderName == "_G" then
		return _G
	end
	local holder = _G
	for part in holderName:gmatch("[^%.]+") do
		if type(holder) ~= "table" then
			return nil
		end
		holder = holder[part]
		if holder == nil then
			return nil
		end
	end
	return holder
end

local function resolveSlot(holder, key)
	if type(holder) ~= "table" then
		return nil
	end

	if key:sub(1, 2) == "__" then
		local mt = getmetatable(holder)
		if type(mt) == "table" and type(rawget(mt, key)) == "function" then
			return mt, key
		end
	end

	return holder, key
end

local function resolveString(target)
	if type(target) ~= "string" or target == "" then
		return nil
	end
	local holderName, key = target:match("^(.*)%.([^%.]+)$")
	if not holderName then
		return _G, target
	end

	local holder
	if holderName == "_G" then
		holder = _G
	else
		holder = resolvePathFromG(holderName)
		if holder == nil and package and package.loaded then
			holder = package.loaded[holderName]
		end
	end

	if type(holder) ~= "table" then
		return nil
	end

	return resolveSlot(holder, key)
end

local function resolveTableSpec(target)
	local key
	local holder

	if type(target.global) == "string" and target.global ~= "" then
		return _G, target.global
	end

	if type(target.table) == "string" and target.table ~= "" then
		key = target.key
		if type(key) ~= "string" or key == "" then
			return nil
		end
		holder = resolvePathFromG(target.table)
		if type(holder) ~= "table" then
			return nil
		end
		return resolveSlot(holder, key)
	end

	if type(target.module) == "string" and target.module ~= "" then
		key = target.key
		if type(key) ~= "string" or key == "" then
			return nil
		end
		if package and package.loaded then
			holder = package.loaded[target.module]
		end
		if type(holder) ~= "table" then
			return nil
		end
		return resolveSlot(holder, key)
	end

	return nil
end

local function resolve(target)
	if type(target) == "string" then
		return resolveString(target)
	elseif type(target) == "table" then
		return resolveTableSpec(target)
	end
	return nil
end

local function describeTarget(target)
	if type(target) == "string" then
		return target
	end
	if type(target) == "table" then
		if type(target.global) == "string" then
			return target.global
		end
		if type(target.table) == "string" and type(target.key) == "string" then
			return target.table .. "." .. target.key
		end
		if type(target.module) == "string" and type(target.key) == "string" then
			return "module " .. target.module .. "." .. target.key
		end
	end
	return tostring(target)
end

local function targetId(target)
	if type(target) == "string" then
		return target
	end
	if type(target) == "table" then
		if type(target.global) == "string" and target.global ~= "" then
			return target.global
		end
		if
			type(target.table) == "string"
			and target.table ~= ""
			and type(target.key) == "string"
			and target.key ~= ""
		then
			return target.table .. "." .. target.key
		end
		if
			type(target.module) == "string"
			and target.module ~= ""
			and type(target.key) == "string"
			and target.key ~= ""
		then
			return "module:" .. target.module .. "." .. target.key
		end
	end
	return nil
end

local function ensureEntry(id, target)
	local entry = registry[id]
	if not entry then
		entry = { handlers = {}, handles = {}, target = target }
		registry[id] = entry
	end
	if entry.target == nil then
		entry.target = target
	end
	return entry
end

local function callChain(handlers, i, orig, ...)
	local h = handlers[i]
	if not h then
		return orig(...)
	end
	local function nextOrig(...)
		return callChain(handlers, i + 1, orig, ...)
	end
	return h(nextOrig, ...)
end

local function traceback(err)
	local msg = tostring(err)
	if debug and type(debug.traceback) == "function" then
		return debug.traceback(msg, 2)
	end
	return msg
end

local function trampoline(orig, id)
	local t = function(...)
		local entry = registry[id]
		if not entry then
			return orig(...)
		end
		if #entry.handlers == 0 then
			return orig(...)
		end
		local res = pack(pcall(callChain, entry.handlers, 1, orig, ...))
		if res[1] then
			return unpack(res, 2, res.n)
		end
		print("[MjsLua] hook '" .. describeTarget(entry.target or id) .. "' error:\n" .. traceback(res[2]))
		return orig(...)
	end
	wrapped[t] = id
	return t
end

local function tryPatch(id, target)
	local holder, key = resolve(target)
	if not holder then
		return false
	end

	local current = holder[key]
	local existing = wrapped[current]
	if existing ~= nil then
		return true, existing
	end

	if type(current) ~= "function" then
		return false
	end

	ensureEntry(id, target)
	holder[key] = trampoline(current, id)
	return true, id
end

local function sweepPending()
	for id, entry in pairs(registry) do
		local target = entry.target or id
		local ok = tryPatch(id, target)
		if ok then
			pending[id] = nil
		else
			pending[id] = target
		end
	end
end

local originalRequire = require
if type(originalRequire) == "function" then
	require = function(...)
		local res = pack(originalRequire(...))
		if next(registry) then
			sweepPending()
		end
		return unpack(res, 1, res.n)
	end
end

local function removeHandler(entry, handle)
	for i = 1, #entry.handlers do
		if entry.handles[i] == handle then
			table.remove(entry.handlers, i)
			table.remove(entry.handles, i)
			return true
		end
	end
	return false
end

MjsLua = {}

-- MjsLua._logNative(level, msg) is registered by the plugin and logs synchronously
-- on the main thread (0 = info, 1 = warn, 2 = error).
function MjsLua.log(msg)
	MjsLua._logNative(0, tostring(msg))
end

function MjsLua.warn(msg)
	MjsLua._logNative(1, tostring(msg))
end

function MjsLua.error(msg)
	MjsLua._logNative(2, tostring(msg))
end

function MjsLua.hook(target, handler)
	if type(target) == "table" and handler == nil then
		handler = target.handler
	end

	local id = targetId(target)
	if type(id) ~= "string" or id == "" then
		log("hook: target must be a non-empty string or a table form")
		return nil
	end
	if handler ~= nil and type(handler) ~= "function" then
		log("hook: handler for '" .. describeTarget(target) .. "' must be a function or nil")
		return nil
	end

	local ok, patchedId = tryPatch(id, target)
	id = patchedId or id
	local entry = ensureEntry(id, target)
	local handle = { id = id, target = target, fn = handler }

	if handler ~= nil then
		local index = #entry.handlers + 1
		entry.handlers[index] = handler
		entry.handles[index] = handle
	end

	if ok then
		pending[id] = nil
	else
		pending[id] = target
	end

	return handle
end

function MjsLua.unhook(handle)
	if type(handle) ~= "table" then
		return false
	end
	local id = handle.id
	local fn = handle.fn
	if type(id) ~= "string" or type(fn) ~= "function" then
		return false
	end
	local entry = registry[id]
	if not entry then
		return false
	end
	return removeHandler(entry, handle)
end

function MjsLua._parseConfigSchema(schema, defaultSection)
	local entries = {}
	local errors = {}
	if type(schema) ~= "table" then
		errors[#errors + 1] = "schema must be a table"
		return entries, errors
	end

	local keys = {}
	for k in pairs(schema) do
		keys[#keys + 1] = k
	end
	table.sort(keys, function(a, b)
		return tostring(a) < tostring(b)
	end)

	for _, key in ipairs(keys) do
		local spec = schema[key]
		local err = nil
		local entry = nil

		if type(key) ~= "string" or key == "" then
			err = "key must be a non-empty string"
		elseif type(spec) ~= "table" then
			err = "entry '" .. tostring(key) .. "': spec must be a table"
		elseif spec.default == nil then
			err = "entry '" .. key .. "': missing 'default'"
		else
			local default = spec.default
			local dtype = type(default)
			local etype = nil

			if spec.type ~= nil then
				if spec.type ~= "int" then
					err = "entry '"
						.. key
						.. "': unknown type override '"
						.. tostring(spec.type)
						.. "' (only 'int' is allowed)"
				elseif dtype ~= "number" then
					err = "entry '" .. key .. "': type='int' requires a number default"
				else
					etype = "int"
				end
			end

			if not err and spec.keybind ~= nil then
				if spec.keybind ~= true then
					err = "entry '" .. key .. "': 'keybind' must be true when present"
				elseif spec.type ~= nil then
					err = "entry '" .. key .. "': 'keybind' cannot combine with a type override"
				elseif spec.choices ~= nil then
					err = "entry '" .. key .. "': 'keybind' cannot combine with 'choices'"
				elseif dtype ~= "string" then
					err = "entry '" .. key .. "': keybind requires a string default like 'F9' or 'LeftControl+F9'"
				else
					etype = "keybind"
				end
			end

			if not err and not etype then
				if dtype == "boolean" then
					etype = "bool"
				elseif dtype == "number" then
					etype = "number"
				elseif dtype == "string" then
					etype = spec.choices ~= nil and "choices" or "string"
				else
					err = "entry '" .. key .. "': unsupported default type '" .. dtype .. "'"
				end
			end

			if not err and spec.choices ~= nil then
				if etype ~= "choices" then
					err = "entry '" .. key .. "': 'choices' is only valid for a string default"
				elseif type(spec.choices) ~= "table" or #spec.choices == 0 then
					err = "entry '" .. key .. "': 'choices' must be a non-empty array"
				else
					for _, c in ipairs(spec.choices) do
						if type(c) ~= "string" then
							err = "entry '" .. key .. "': every 'choices' value must be a string"
							break
						end
					end
				end
			end

			if not err and (spec.min ~= nil or spec.max ~= nil) then
				if etype ~= "number" and etype ~= "int" then
					err = "entry '" .. key .. "': 'min'/'max' is only valid for a number default"
				elseif
					(spec.min ~= nil and type(spec.min) ~= "number")
					or (spec.max ~= nil and type(spec.max) ~= "number")
				then
					err = "entry '" .. key .. "': 'min'/'max' must be numbers"
				elseif spec.min ~= nil and spec.max ~= nil and spec.min > spec.max then
					err = "entry '" .. key .. "': 'min' must be <= 'max'"
				end
			end

			if not err then
				entry = {
					key = key,
					type = etype,
					default = default,
					desc = type(spec.desc) == "string" and spec.desc or "",
					section = (type(spec.section) == "string" and spec.section ~= "") and spec.section
						or defaultSection,
				}
				if etype == "choices" then
					entry.choices = {}
					for i = 1, #spec.choices do
						entry.choices[i] = spec.choices[i]
					end
				end
				if etype == "number" or etype == "int" then
					entry.min = spec.min
					entry.max = spec.max
				end
			end
		end

		if err then
			errors[#errors + 1] = err
		elseif entry then
			entries[#entries + 1] = entry
		end
	end

	return entries, errors
end

function MjsLua.config(modName, schema)
	if type(modName) ~= "string" or modName == "" then
		log("config: modName must be a non-empty string")
		return setmetatable({}, {})
	end

	local entries, errors = MjsLua._parseConfigSchema(schema, modName)
	for i = 1, #errors do
		log("config[" .. modName .. "]: " .. errors[i])
	end

	local validKeys = {}
	for i = 1, #entries do
		local e = entries[i]
		validKeys[e.key] = true
		if type(MjsLua._configBind) == "function" then
			local ok, err = pcall(MjsLua._configBind, modName, e)
			if not ok then
				log("config[" .. modName .. "]: failed to bind '" .. e.key .. "': " .. tostring(err))
			end
		end
	end

	local proxy = setmetatable({}, {
		__index = function(_, k)
			if not validKeys[k] then
				return nil
			end
			if type(MjsLua._configGet) ~= "function" then
				return nil
			end
			return MjsLua._configGet(modName, k)
		end,
		__newindex = function(_, k, v)
			if not validKeys[k] then
				log("config[" .. modName .. "]: cannot set unknown key '" .. tostring(k) .. "'")
				return
			end
			if type(MjsLua._configSet) == "function" then
				MjsLua._configSet(modName, k, v)
			end
		end,
	})
	configMeta[proxy] = { mod = modName, keys = validKeys }
	return proxy
end

function MjsLua.onConfigChange(cfg, key, fn)
	local meta = configMeta[cfg]
	if not meta then
		log("onConfigChange: first argument is not a config table from MjsLua.config")
		return
	end
	if type(key) ~= "string" or not meta.keys[key] then
		log("onConfigChange[" .. meta.mod .. "]: unknown key '" .. tostring(key) .. "'")
		return
	end
	if type(fn) ~= "function" then
		log("onConfigChange[" .. meta.mod .. "]: handler must be a function")
		return
	end
	if type(MjsLua._onConfigChange) == "function" then
		MjsLua._onConfigChange(meta.mod, key, fn)
	end
end

return MjsLua
