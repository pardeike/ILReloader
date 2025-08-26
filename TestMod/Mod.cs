using TestApplication;

namespace TestMod;

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

	public void Show()
	{
		Console.WriteLine("TEST 004");
		Console.WriteLine($"Showing mod dialog with message: {myConfig.message}");
	}
}