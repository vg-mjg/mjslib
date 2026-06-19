-- consumer hook for the native bridge demo

if type(Demo) ~= "table" then
	MjsLua.log("[native-demo] Demo module missing -- is MjslibNativeDemo.dll installed and loaded?")
	return
end

MjsLua.log("[native-demo] Demo.echo('hello from lua') -> " .. tostring(Demo.echo("hello from lua")))
MjsLua.log("[native-demo] Demo.add(2, 3) -> " .. tostring(Demo.add(2, 3)))
