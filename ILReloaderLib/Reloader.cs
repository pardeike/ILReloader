using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;

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
		il.Emit(System.Reflection.Emit.OpCodes.Ldstr, originalMethod.Id());
		il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
		il.Emit(System.Reflection.Emit.OpCodes.Call, SymbolExtensions.GetMethodInfo(() => GetInstructions(default, default)));
		il.Emit(System.Reflection.Emit.OpCodes.Ret);
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
		var cecilInstructions = body.Instructions.ToArray();
		var harmonyInstructions = new CodeInstruction[cecilInstructions.Length];
		var labels = new Dictionary<int, Label>();
		
		// Create labels for all instructions that are branch targets
		var branchTargets = new HashSet<int>();
		for (var i = 0; i < cecilInstructions.Length; i++)
		{
			var cecilInstr = cecilInstructions[i];
			if (cecilInstr.Operand is Mono.Cecil.Cil.Instruction targetInstr)
			{
				var targetIndex = Array.IndexOf(cecilInstructions, targetInstr);
				if (targetIndex >= 0)
					branchTargets.Add(targetIndex);
			}
			else if (cecilInstr.Operand is Mono.Cecil.Cil.Instruction[] switchTargets)
			{
				foreach (var target in switchTargets)
				{
					var targetIndex = Array.IndexOf(cecilInstructions, target);
					if (targetIndex >= 0)
						branchTargets.Add(targetIndex);
				}
			}
		}
		
		// Create labels for exception handler boundaries
		foreach (var handler in body.ExceptionHandlers)
		{
			var tryStartIndex = Array.IndexOf(cecilInstructions, handler.TryStart);
			var tryEndIndex = Array.IndexOf(cecilInstructions, handler.TryEnd);
			var handlerStartIndex = Array.IndexOf(cecilInstructions, handler.HandlerStart);
			var handlerEndIndex = Array.IndexOf(cecilInstructions, handler.HandlerEnd);
			
			if (tryStartIndex >= 0) branchTargets.Add(tryStartIndex);
			if (tryEndIndex >= 0) branchTargets.Add(tryEndIndex);
			if (handlerStartIndex >= 0) branchTargets.Add(handlerStartIndex);
			if (handlerEndIndex >= 0) branchTargets.Add(handlerEndIndex);
			
			if (handler.FilterStart != null)
			{
				var filterStartIndex = Array.IndexOf(cecilInstructions, handler.FilterStart);
				if (filterStartIndex >= 0) branchTargets.Add(filterStartIndex);
			}
		}
		
		// Create Harmony labels for all branch targets
		foreach (var targetIndex in branchTargets)
		{
			labels[targetIndex] = il.DefineLabel();
		}
		
		// Convert instructions
		for (var i = 0; i < cecilInstructions.Length; i++)
		{
			var cecilInstr = cecilInstructions[i];
			var opcode = Tools.ConvertOpcode(cecilInstr.OpCode);
			object operand;
			
			// Handle branch operands first - they use labels instead of conversion
			if (cecilInstr.Operand is Mono.Cecil.Cil.Instruction targetInstr)
			{
				var targetIndex = Array.IndexOf(cecilInstructions, targetInstr);
				if (targetIndex >= 0 && labels.TryGetValue(targetIndex, out var targetLabel))
					operand = targetLabel;
				else
					operand = null; // should not happen if labels were created correctly
			}
			else if (cecilInstr.Operand is Mono.Cecil.Cil.Instruction[] switchTargets)
			{
				var switchLabels = new Label[switchTargets.Length];
				for (var j = 0; j < switchTargets.Length; j++)
				{
					var targetIndex = Array.IndexOf(cecilInstructions, switchTargets[j]);
					if (targetIndex >= 0 && labels.TryGetValue(targetIndex, out var targetLabel))
						switchLabels[j] = targetLabel;
				}
				operand = switchLabels;
			}
			else
			{
				// Convert all other operands
				operand = Tools.ConvertOperand(cecilInstr.Operand, il);
			}
			
			var harmonyInstr = new CodeInstruction(opcode, operand);
			
			// Assign labels to instructions
			if (labels.TryGetValue(i, out var label))
			{
				harmonyInstr.labels.Add(label);
			}
			
			harmonyInstructions[i] = harmonyInstr;
		}
		
		// Handle exception blocks
		foreach (var handler in body.ExceptionHandlers)
		{
			var tryStartIndex = Array.IndexOf(cecilInstructions, handler.TryStart);
			var tryEndIndex = Array.IndexOf(cecilInstructions, handler.TryEnd);
			var handlerStartIndex = Array.IndexOf(cecilInstructions, handler.HandlerStart);
			var handlerEndIndex = Array.IndexOf(cecilInstructions, handler.HandlerEnd);
			
			if (tryStartIndex >= 0 && tryEndIndex >= 0 && handlerStartIndex >= 0 && handlerEndIndex >= 0)
			{
				var blockType = handler.HandlerType switch
				{
					ExceptionHandlerType.Catch => ExceptionBlockType.BeginCatchBlock,
					ExceptionHandlerType.Finally => ExceptionBlockType.BeginFinallyBlock,
					ExceptionHandlerType.Fault => ExceptionBlockType.BeginFaultBlock,
					ExceptionHandlerType.Filter => ExceptionBlockType.BeginExceptFilterBlock,
					_ => ExceptionBlockType.BeginExceptionBlock
				};
				
				// Begin exception block
				var beginBlock = new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock, handler.CatchType != null ? Tools.ResolveType(handler.CatchType) : null);
				harmonyInstructions[tryStartIndex].blocks.Add(beginBlock);
				
				// Begin handler block
				var handlerBlock = new ExceptionBlock(blockType, handler.CatchType != null ? Tools.ResolveType(handler.CatchType) : null);
				harmonyInstructions[handlerStartIndex].blocks.Add(handlerBlock);
				
				// End exception block
				if (handlerEndIndex < harmonyInstructions.Length)
				{
					var endBlock = new ExceptionBlock(ExceptionBlockType.EndExceptionBlock);
					harmonyInstructions[handlerEndIndex].blocks.Add(endBlock);
				}
			}
		}
		
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
							replacementMembers[originalId] = replacementMethod;
							harmony.Unpatch(originalMethod, HarmonyPatchType.Transpiler, harmony.Id);
							var transpilerFactory = new HarmonyMethod(SymbolExtensions.GetMethodInfo(() => TranspilerFactory(default)));
							harmony.Patch(originalMethod, transpiler: transpilerFactory);
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