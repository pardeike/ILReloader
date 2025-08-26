using TestApplication;

namespace TestMod;

// Custom attribute to mark methods or constructors as reloadable
[AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method)]
internal class ReloadableAttribute : Attribute { }

public class Mod
{
	static Mod()
	{
		Console.WriteLine("TestMod static constructor");
	}

	public Mod()
	{
		Console.WriteLine("TestMod loaded");
	}
}

public class ModDialog : IAppDialog
{
	private DialogConfig myConfig;

	public void Prepare(DialogConfig config) => myConfig = config;

	[Reloadable]
	public void Show()
	{
		Console.WriteLine("TEST 001"); // for testing, edit this line and build so the dll changes
		Console.WriteLine($"Showing mod dialog with message: {myConfig.message}");
	}
}