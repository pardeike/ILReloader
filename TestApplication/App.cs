using ILReloaderLib;
using System.Reflection;

namespace TestApplication;

public class App
{
	public static void Main(string[] args)
	{
		Reloader.Start();
		Console.WriteLine("App started");
		if (args.Length != 1)
		{
			Console.Error.WriteLine("Please provide the path to the mod DLL as a command-line argument");
			return;
		}

		var modToLoad = "Mods" + Path.DirectorySeparatorChar + args[0];
		Console.WriteLine($"Loading mod from: {modToLoad}");
		RunLoop(modToLoad);
	}

	public static void RunLoop(string modToLoad)
	{
		// app loads and locks dlls on purpose to show that ilreloader works around that
		var assembly = Assembly.LoadFrom(modToLoad);

		// find all types implementing IAppDialog
		var type = assembly.GetTypes().FirstOrDefault(t => typeof(IAppDialog).IsAssignableFrom(t) && !t.IsAbstract);
		if (type == null)
		{
			Console.Error.WriteLine("No type implementing IAppDialog found in the assembly");
			return;
		}
		Console.WriteLine($"Found dialog: {type.FullName}");

		while (true)
		{
			Console.WriteLine("Enter message (or 'bye' to exit):");
			var message = Console.ReadLine().Trim();
			if (message == "bye")
			{
				Console.WriteLine("Exiting");
				return;
			}
			var dialog = (IAppDialog)Activator.CreateInstance(type);
			dialog.Prepare(new DialogConfig() { message = message });
			dialog.Show();
		}
	}
}