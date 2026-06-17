-- demo for polling key combos each frame

local function logf(fmt, ...)
	MjsLua.log("[keybinds] " .. string.format(fmt, ...))
end

-- resolve unityengine lazily on the first tick
local Input, KeyCode
local bindings, sequences

local function buildBindings()
	Input = UnityEngine.Input
	local K = UnityEngine.KeyCode
	KeyCode = K

	-- simultaneous combo definitions
	bindings = {
		{
			name = "F8",
			mods = {},
			key = K.F8,
			action = function()
				logf("F8 -> hello")
			end,
		},
		{
			name = "Ctrl+K",
			mods = { { K.LeftControl, K.RightControl } }, -- either control key fills this slot
			key = K.K,
			action = function()
				logf("Ctrl+K -> action A")
			end,
		},
		{
			name = "Ctrl+Shift+R",
			mods = { { K.LeftControl, K.RightControl }, { K.LeftShift, K.RightShift } },
			key = K.R,
			action = function()
				logf("Ctrl+Shift+R -> action B")
			end,
		},
	}

	-- ordered key sequences with a frame timeout
	sequences = {
		{
			name = "Ctrl+K Ctrl+B",
			steps = { { mods = { K.LeftControl }, key = K.K }, { mods = { K.LeftControl }, key = K.B } },
			timeout = 60,
			action = function()
				logf("Ctrl+K Ctrl+B -> action C")
			end,
		},
	}
end

-- check whether all modifier slots are held
local function modsHeld(mods)
	for i = 1, #mods do
		local slot = mods[i]
		if type(slot) == "table" then
			local any = false
			for j = 1, #slot do
				if Input.GetKey(slot[j]) then
					any = true
					break
				end
			end
			if not any then
				return false
			end
		elseif not Input.GetKey(slot) then
			return false
		end
	end
	return true
end

-- check one combo trigger frame
local function triggered(binding)
	return Input.GetKeyDown(binding.key) and modsHeld(binding.mods)
end

-- track sequence progress and timeout
local seqState = {}

local function pumpSequences()
	for i = 1, #sequences do
		local seq = sequences[i]
		local st = seqState[i] or { step = 1, age = 0 }
		seqState[i] = st

		if st.step > 1 then
			st.age = st.age + 1
			if st.age > seq.timeout then
				st.step = 1 -- timeout resets the sequence
			end
		end

		if triggered(seq.steps[st.step]) then
			if st.step == #seq.steps then
				st.step = 1
				st.age = 0
				local ok, err = pcall(seq.action)
				if not ok then
					logf("sequence '%s' error: %s", seq.name, tostring(err))
				end
			else
				st.step = st.step + 1
				st.age = 0
			end
		end
	end
end

local ready = false

MjsLua.hook("UpdateBeat.__call", function(orig, self, ...)
	if self == UpdateBeat then
		if not ready then
			if UnityEngine and UnityEngine.Input then
				buildBindings()
				ready = true
				logf("armed: %d combos, %d sequences", #bindings, #sequences)
			end
		end

		if ready then
			for i = 1, #bindings do
				local b = bindings[i]
				if triggered(b) then
					local ok, err = pcall(b.action)
					if not ok then
						logf("binding '%s' error: %s", b.name, tostring(err))
					end
				end
			end
			pumpSequences()
		end
	end

	return orig(self, ...)
end)

logf("registered UpdateBeat.__call poller")
