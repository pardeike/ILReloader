using HarmonyLib;
using ILReloaderLib;
using System.Reflection;

namespace TestApplication;

public class App
{
	public static void Main(string[] args)
	{
		Reloader.FixAssemblyLoading(SymbolExtensions.GetMethodInfo(() => RunLoop(default)));
		Reloader.Watch(Path.Combine(Directory.GetCurrentDirectory(), "Mods"));

		Console.WriteLine("App started");
		if (args.Length != 1)
		{
			Console.Error.WriteLine("Please provide the path to the mod DLL as a command-line argument");
			return;
		}

		var modToLoad = "Mods" + Path.DirectorySeparatorChar + args[0];
		RunLoop(modToLoad);
	}

	public static void RunLoop(string modToLoad)
	{
		// app loads and locks dlls on purpose to show that ilreloader works around that
		var assembly = Assembly.LoadFrom(modToLoad);

		// find all types implementing IMod
		var type = assembly.GetTypes().FirstOrDefault(t => typeof(IMod).IsAssignableFrom(t) && !t.IsAbstract);
		if (type == null)
		{
			Console.Error.WriteLine("No type implementing IAppDialog found in the assembly");
			return;
		}
		Console.WriteLine($"Found mod: {type.FullName}");
		var mod = (IMod)Activator.CreateInstance(type);

		while (true)
		{
			Console.Write("\nEnter message (or 'bye' to exit): ");
			var message = Console.ReadLine().Trim();
			if (message == "bye")
			{
				Console.WriteLine("Exiting");
				return;
			}
			var dialog = mod.GetDialog();
			dialog.Prepare(new DialogConfig() { message = message });
			dialog.Show();
		}
	}
}