-- declare typed config backed by BepInEx entries
--
-- each schema entry becomes one persisted key in BepInEx/config/ConfigDemo.cfg
-- reads return the current saved value, including hand edits to the cfg file
-- writes persist the new value and notify change handlers
--
-- defaults infer the type: bool, number, int, string, choice, or keybind
-- min and max validate numeric ranges
-- malformed entries are logged and skipped while valid entries still register

local cfg = MjsLua.config("ConfigDemo", {
	Enabled = { default = true, desc = "Master toggle for the demo" },
	Volume = { default = 0.5, min = 0, max = 1, desc = "A 0..1 volume slider" },
	MaxItems = { default = 3, type = "int", min = 1, max = 99, desc = "An integer count" },
	Greeting = { default = "hello", desc = "Free text" },
	Mode = { default = "fast", choices = { "fast", "slow" }, desc = "A mode choice" },
	Hotkey = { default = "LeftControl+G", keybind = true, desc = "A demo hotkey" },
})

local function logf(fmt, ...)
	MjsLua.log("[config_demo] " .. string.format(fmt, ...))
end

-- keybind entries read back as display strings
logf(
	"Enabled=%s Volume=%s MaxItems=%s Greeting=%q Mode=%s Hotkey=%s",
	tostring(cfg.Enabled),
	tostring(cfg.Volume),
	tostring(cfg.MaxItems),
	cfg.Greeting,
	cfg.Mode,
	cfg.Hotkey
)

-- react to edits from the cfg file, ui, or another mod
MjsLua.onConfigChange(cfg, "Mode", function(value)
	logf("Mode changed to %s", tostring(value))
end)

-- poll cfg.Hotkey inside a frame hook when handling input
-- see example/keybinds for the full input polling pattern

-- uncomment to persist a new mode and fire the handler when the value changes
-- cfg.Mode = "slow"
