local count = 0
local EVERY = 300

local function demoLog(msg)
	MjsLua.log("[demo] " .. msg)
end

local handle = MjsLua.hook("UpdateBeat.__call", function(orig, self, ...)
	if self == UpdateBeat then
		count = count + 1
		if count == 1 or count % EVERY == 0 then
			demoLog("UpdateBeat.__call hook fired (call #" .. count .. ")")
		end
	end
	return orig(self, ...)
end)

if handle then
	demoLog("hooked UpdateBeat.__call via MjsLua")
else
	demoLog("failed to hook UpdateBeat.__call via MjsLua")
end
