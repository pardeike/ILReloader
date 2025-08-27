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
		AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += OnReflectionOnlyAssemblyResolve;
		$"reloader started".LogMessage();
	}

	internal static Reloader instance;
	readonly string modsDir;
	static readonly Dictionary<string, MethodBase> reloadableMembers = [];
	static readonly Dictionary<string, MethodDefinition> replacementMembers = [];

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

	static Assembly OnReflectionOnlyAssemblyResolve(object sender, ResolveEventArgs e)
	{
		var requested = new AssemblyName(e.Name);
		var simple = requested.Name;

		var execLoaded = AppDomain.CurrentDomain.GetAssemblies()
			.FirstOrDefault(a =>
			{
				if (string.IsNullOrEmpty(a.Location)) // Dynamic/byte-load have Location == "" – skip those
					return false;
				return Tools.NamesMatch(a.GetName(), requested) || a.GetName().Name.Equals(simple, StringComparison.OrdinalIgnoreCase);
			});

		if (execLoaded != null && !string.IsNullOrEmpty(execLoaded.Location) && File.Exists(execLoaded.Location))
		{
			var assembly = Assembly.ReflectionOnlyLoad(File.ReadAllBytes(execLoaded.Location));
			$"resolved new reflection assembly: {assembly}".LogWarning();
			return assembly;
		}

		$"failed to resolve assembly: {simple}".LogError();
		return null;
	}

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
		if (replacementMembers.TryGetValue(methodId, out var replacementMethod) == false)
		{
			$"could not find replacement method".LogError();
			yield break;
		}
		var body = replacementMethod.Body;
		var instructions = body.Instructions.Select(i =>
		{
			var opcode = Tools.ConvertOpcode(i.OpCode);
			var operand = Tools.ConvertOperand(i, il);
			return new CodeInstruction(opcode, operand);
		});
		// TODO set instructions[i].labels
		// TODO set instructions[i].blocks
		foreach (var instr in instructions)
			yield return instr;
	}

	static void Patch(AssemblyDefinition newAssembly)
	{
		var harmony = new Harmony("brrainz.reloader");
		newAssembly.Modules.SelectMany(m => m.Types).SelectMany(Tools.AllReloadableMembers)
			.Do(replacementMethod =>
			{
				if (reloadableMembers.TryGetValue(replacementMethod.Id(), out var originalMethod))
				{
					$"patching {originalMethod.FullDescription()} with {replacementMethod}".LogMessage();
					replacementMembers[originalMethod.Id()] = replacementMethod;
					harmony.Unpatch(originalMethod, HarmonyPatchType.Transpiler, harmony.Id);
					var transpilerFactory = new HarmonyMethod(SymbolExtensions.GetMethodInfo(() => TranspilerFactory(default)));
					harmony.Patch(originalMethod, transpiler: transpilerFactory);
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