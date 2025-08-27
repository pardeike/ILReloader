using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mono.Cecil;

namespace ILReloaderLib;

public class Reloader
{
	static class PatchClass
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			return instructions.MethodReplacer(
				SymbolExtensions.GetMethodInfo(() => Assembly.LoadFrom("")),
				SymbolExtensions.GetMethodInfo(() => LoadOriginalAssembly(""))
			);
		}

		public static bool Prefix(MethodBase ___method) => ___method.ReflectedType == null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Start()
	{
		var harmony = new Harmony("brrainz.doorstop");
		var original1 = AccessTools.Method("TestApplication.App:RunLoop");
		var transpiler = SymbolExtensions.GetMethodInfo(() => PatchClass.Transpiler(default));
		_ = harmony.Patch(original1, transpiler: new HarmonyMethod(transpiler));
		var original2 = AccessTools.Method("HarmonyLib.MethodBodyReader:HandleNativeMethod");
		var prefix = SymbolExtensions.GetMethodInfo(() => PatchClass.Prefix(default));
		_ = harmony.Patch(original2, prefix: new HarmonyMethod(prefix));
		instance = new Reloader();
		$"reloader started".LogMessage();
	}

	internal static Reloader instance;
	readonly string modsDir;
	static readonly Dictionary<string, MethodBase> reloadableMembers = [];


	static readonly List<FileSystemWatcher> watchers = [];
	static readonly Debouncer changedFiles = new(TimeSpan.FromSeconds(3), basePath =>
	{
		var path = $"{basePath}.dll";
		try
		{
			$"reloading {path}".LogMessage();
			using var readStream = File.OpenRead(path);
			using var assembly = AssemblyDefinition.ReadAssembly(readStream);
			Patch(assembly);
		}
		catch (Exception ex)
		{
			$"error during reloading {path}: {ex}".LogError();
		}
	});

	static int counter = 0;
	static DynamicMethod TranspilerFactory(MethodBase originalMethod)
	{
		var t_cInstr = typeof(IEnumerable<CodeInstruction>);
		var attributes = System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static;
		var dm = new DynamicMethod($"TransientTranspiler{++counter}", attributes, CallingConventions.Standard, t_cInstr, [t_cInstr, typeof(ILGenerator)], typeof(Reloader), true);
		var il = dm.GetILGenerator();
		il.Emit(OpCodes.Ldstr, originalMethod.Id());
		il.Emit(OpCodes.Ldarg_1);
		il.Emit(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => GetInstructions(default, default)));
		il.Emit(OpCodes.Ret);
		return dm;
	}

	static IEnumerable<CodeInstruction> GetInstructions(string methodId, ILGenerator il)
	{
		var harmonyInstructions = CecilConverter.Convert(il, methodId);
		foreach (var instr in harmonyInstructions)
			yield return instr;
	}

	static void Patch(AssemblyDefinition newAssembly)
	{
		var harmony = new Harmony("brrainz.reloader");
		newAssembly.Modules.SelectMany(m => m.Types).SelectMany(Tools.AllReloadableMembers)
			.Do(replacementMethod =>
			{
				try
				{
					var replacementId = replacementMethod.Id();

					if (reloadableMembers.TryGetValue(replacementId, out var originalMethod))
					{
						$"patching {originalMethod.FullDescription()} with {replacementMethod}".LogMessage();
						var originalId = originalMethod.Id();

						if (!string.IsNullOrEmpty(originalId) && replacementMethod != null)
						{
							CecilConverter.Register(originalId, replacementMethod);
							harmony.Unpatch(originalMethod, HarmonyPatchType.Transpiler, harmony.Id);
							var transpilerFactory = new HarmonyMethod(SymbolExtensions.GetMethodInfo(() => TranspilerFactory(default)));
							_ = harmony.Patch(originalMethod, transpiler: transpilerFactory);
						}
						else
						{
							$"cannot patch - originalId: '{originalId}', replacementMethod: {replacementMethod}".LogError();
						}
					}
				}
				catch (Exception ex)
				{
					$"error processing replacement method {replacementMethod}: {ex}".LogError();
				}
			});
	}

	internal Reloader()
	{
		modsDir = Path.Combine(Directory.GetCurrentDirectory(), "Mods");
		watchers.Add(CreateWatcher());
	}

	FileSystemWatcher CreateWatcher()
	{
		var watcher = new FileSystemWatcher(modsDir)
		{
			Filter = $"*Mod.dll",
			IncludeSubdirectories = true,
			EnableRaisingEvents = true
		};
		watcher.Error += (_, e) => e.GetException().ToString().LogError();
		watcher.Changed += (_, e) =>
		{
			var path = e.FullPath;
			if (path.Replace('\\', '/').Contains("/obj/"))
				return;
			changedFiles.Add(path.WithoutFileExtension());
		};
		return watcher;
	}

	internal static Assembly LoadOriginalAssembly(string path)
	{
		$"loading {path}".LogMessage();
		var assembly = Assembly.Load(File.ReadAllBytes(path));
		assembly.GetTypes().SelectMany(type => Tools.AllReloadableMembers(type))
			.Do(member =>
			{
				$"registered: {member.DeclaringType.FullName}.{member.Name}".LogMessage();
				reloadableMembers[member.Id()] = member;
			});
		return assembly;
	}
}