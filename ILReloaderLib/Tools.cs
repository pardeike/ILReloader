using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using static HarmonyLib.AccessTools;

namespace ILReloaderLib;

[AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method)]
internal class ReloadableAttribute : Attribute
{
}

//static class Logger
//{
//	public static void Log(this string message)
//	{
//		using var logFile = new StreamWriter("Doorstop.log.txt", true);
//		logFile.WriteLine($"[{DateTime.Now}] {message}");
//	}
//}

internal static class Tools
{
	static readonly string reloadableTypeName = typeof(ReloadableAttribute).Name;

	internal delegate void DetourMethodDelegate(MethodBase method, MethodBase replacement);
	internal static readonly DetourMethodDelegate DetourMethod = MethodDelegate<DetourMethodDelegate>(Method("HarmonyLib.PatchTools:DetourMethod"));

	internal static void LogMessage(this string log) => Console.WriteLine($"[Info] {log}");
	internal static void LogWarning(this string log) => Console.WriteLine($"[Warn] {log}");
	internal static void LogError(this string log) => Console.WriteLine($"[Error] {log}");

	internal static bool IsReflectionReloadable(this MethodBase method) => method.GetCustomAttributesData().Any(d => d.AttributeType.Name == reloadableTypeName);
	internal static bool IsCecilReloadable(this MethodDefinition method) => method.CustomAttributes.Any(a => a.AttributeType.Name == reloadableTypeName);

	internal static string WithoutFileExtension(this string filePath)
	{
		var directory = Path.GetDirectoryName(filePath);
		var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
		return Path.Combine(directory, fileNameWithoutExtension);
	}

	internal static string Id(this MethodBase member)
	{
		var sb = new StringBuilder(128);
		sb.Append(member.DeclaringType.FullName);
		sb.Append('.');
		sb.Append(member.Name);
		sb.Append('(');
		sb.Append(string.Join(", ", member.GetParameters().Select(p => p.ParameterType.FullName)));
		sb.Append(')');
		return sb.ToString();
	}

	internal static string Id(this MethodDefinition member)
	{
		var sb = new StringBuilder(128);
		sb.Append(member.DeclaringType.FullName);
		sb.Append('.');
		sb.Append(member.Name);
		sb.Append('(');
		sb.Append(string.Join(", ", member.Parameters.Select(p => p.ParameterType.FullName)));
		sb.Append(')');
		return sb.ToString();
	}

	internal static IEnumerable<MethodBase> AllReloadableMembers(this Type type)
	{
		foreach (var member in GetDeclaredMethods(type).Where(IsReflectionReloadable))
			yield return member;
		foreach (var member in GetDeclaredConstructors(type).Where(IsReflectionReloadable))
			yield return member;
	}

	internal static IEnumerable<MethodDefinition> AllReloadableMembers(this TypeDefinition type)
	{
		foreach (var member in type.GetMethods().Where(IsCecilReloadable))
			yield return member;
		foreach (var member in type.GetConstructors().Where(IsCecilReloadable))
			yield return member;
	}

	internal static bool NamesMatch(AssemblyName a, AssemblyName b)
	{
		if (!a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase))
			return false;

		// If the request specifies a public key token, require a match
		var at = a.GetPublicKeyToken();
		var bt = b.GetPublicKeyToken();
		if (bt != null && bt.Length > 0)
		{
			if (!TokenEquals(at, bt))
				return false;
		}

		// If the request specifies Culture, require a match
		if (!string.IsNullOrEmpty(b.CultureName) && !string.Equals(a.CultureName ?? "", b.CultureName, StringComparison.OrdinalIgnoreCase))
			return false;

		// Version: be flexible. If the request pins a version, try to match major/minor/build/revision.
		// You can tighten this if needed.
		if (b.Version != null)
		{
			// Accept equal or higher? For games/mods, equal is usually safest:
			if (!Equals(a.Version, b.Version))
			{ /* keep loose by default */ }
		}

		return true;
	}

	internal static bool TokenEquals(byte[] a, byte[] b)
	{
		if (a == null || b == null)
			return a == b;
		if (a.Length != b.Length)
			return false;
		for (int i = 0; i < a.Length; i++)
			if (a[i] != b[i])
				return false;
		return true;
	}

	static readonly Dictionary<short, System.Reflection.Emit.OpCode> opcodeCache = CreateOpcodeCache();

	static Dictionary<short, System.Reflection.Emit.OpCode> CreateOpcodeCache()
	{
		var cache = new Dictionary<short, System.Reflection.Emit.OpCode>();
		
		// Get all System.Reflection.Emit OpCodes and index them by their value
		var emitOpcodeFields = typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(f => f.FieldType == typeof(System.Reflection.Emit.OpCode));
		
		foreach (var field in emitOpcodeFields)
		{
			var opcode = (System.Reflection.Emit.OpCode)field.GetValue(null);
			cache[opcode.Value] = opcode;
		}
		
		return cache;
	}

	internal static System.Reflection.Emit.OpCode ConvertOpcode(Mono.Cecil.Cil.OpCode opcode)
	{
		if (opcodeCache.TryGetValue(opcode.Value, out var emitOpcode))
			return emitOpcode;
		
		throw new NotSupportedException($"Opcode {opcode.Name} (0x{opcode.Value:X4}) is not supported");
	}

	internal static object ConvertOperand(object operand, ILGenerator il)
	{
		if (operand is MethodDefinition method)
			operand = ResolveMethodBase(method);
		else if (operand is MethodReference methodRef)
			operand = ResolveMethodBase(methodRef);
		else if (operand is PropertyReference property)
			operand = ResolveProperty(property);
		else if (operand is FieldReference field)
			operand = ResolveField(field);
		else if (operand is TypeReference type)
			operand = ResolveType(type);
		else if (operand is VariableDefinition variable)
			operand = il.DeclareLocal(ResolveType(variable.VariableType), variable.IsPinned);
		return operand;
	}

	internal static MethodBase ResolveMethodBase(MethodDefinition methodDefinition)
	{
		var declaringType = ResolveType(methodDefinition.DeclaringType);
		var parameters = methodDefinition.Parameters.ToArray();
		var parameterTypes = new Type[parameters.Length];
		for (var i = 0; i < parameters.Length; i++)
			parameterTypes[i] = ResolveType(parameters[i].ParameterType);
		var generics = methodDefinition.GenericParameters.ToArray();
		var genericTypes = new Type[generics.Length];
		for (var i = 0; i < generics.Length; i++)
			genericTypes[i] = ResolveType(generics[i]);
		return DeclaredMethod(declaringType, methodDefinition.Name, parameterTypes, genericTypes.Length == 0 ? null : genericTypes);
	}

	internal static MethodBase ResolveMethodBase(MethodReference methodReference)
	{
		var declaringType = ResolveType(methodReference.DeclaringType);
		var parameters = methodReference.Parameters.ToArray();
		var parameterTypes = new Type[parameters.Length];
		for (var i = 0; i < parameters.Length; i++)
			parameterTypes[i] = ResolveType(parameters[i].ParameterType);
		
		// Handle generic method references
		if (methodReference is GenericInstanceMethod genericMethod)
		{
			var genericTypes = new Type[genericMethod.GenericArguments.Count];
			for (var i = 0; i < genericMethod.GenericArguments.Count; i++)
				genericTypes[i] = ResolveType(genericMethod.GenericArguments[i]);
			return DeclaredMethod(declaringType, methodReference.Name, parameterTypes, genericTypes);
		}
		
		return DeclaredMethod(declaringType, methodReference.Name, parameterTypes);
	}

	internal static PropertyInfo ResolveProperty(PropertyReference property)
	{
		var declaringType = ResolveType(property.DeclaringType);
		return DeclaredProperty(declaringType, property.Name);
	}

	internal static FieldInfo ResolveField(FieldReference field)
	{
		var declaringType = ResolveType(field.DeclaringType);
		return DeclaredField(declaringType, field.Name);
	}

	internal static Type ResolveType(TypeReference typeReference)
	{
		Type type;
		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			type = asm.GetType(typeReference.FullName);
			if (type != null)
			{
				if (type.Assembly.ReflectionOnly)
					throw new TypeLoadException($"Resolved type {typeReference.FullName} to assembly {asm.FullName}, but that assembly is reflection-only");
				// $"# {typeDefinition.FullName} -> {asm.FullName}".LogMessage();
				return type;
			}
		}

		type = Type.GetType(typeReference.FullName);
		if (type != null)
		{
			if (type.Assembly.ReflectionOnly)
				throw new TypeLoadException($"Resolved type {typeReference.FullName} to type within a reflection-only assembly");
			// $"# {typeDefinition.FullName}".LogMessage();
			return type;
		}

		throw new TypeLoadException($"Could not resolve type {typeReference.FullName}");
	}
}