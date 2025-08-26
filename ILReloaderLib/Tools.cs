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

	internal static IEnumerable<MethodBase> AllReloadableMembers(this Type type)
	{
		foreach (var member in GetDeclaredMethods(type).Where(IsReflectionReloadable))
			yield return member;
		foreach (var member in GetDeclaredConstructors(type).Where(IsReflectionReloadable))
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

	internal static MethodBase ReflectionOnlyGetMethodByModuleAndToken(string moduleGUID, int token)
	{
		var module = AppDomain.CurrentDomain.ReflectionOnlyGetAssemblies()
			.Where(a => a.FullName.StartsWith("Microsoft.VisualStudio") == false)
			.SelectMany(a => a.GetLoadedModules())
			.First(m => m.ModuleVersionId.ToString() == moduleGUID);
		return module == null ? null : (MethodBase)module.ResolveMethod(token);
	}

	internal static object ConvertOperand(object operand, ILGenerator il)
	{
		if (operand is MethodBase method)
			operand = ResolveMethodBase(method);
		else if (operand is PropertyInfo property)
			operand = ResolveProperty(property);
		else if (operand is FieldInfo field)
			operand = ResolveField(field);
		else if (operand is Type type)
			operand = ResolveType(type);
		else if (operand is LocalBuilder localBuilder)
			operand = il.DeclareLocal(ResolveType(localBuilder.LocalType), localBuilder.IsPinned);
		return operand;
	}

	internal static MethodBase ResolveMethodBase(MethodBase reflectionOnlyMethod)
	{
		var declaringType = ResolveType(reflectionOnlyMethod.DeclaringType);
		var parameters = reflectionOnlyMethod.GetParameters();
		var parameterTypes = new Type[parameters.Length];
		for (var i = 0; i < parameters.Length; i++)
			parameterTypes[i] = ResolveType(parameters[i].ParameterType);
		var generics = reflectionOnlyMethod.GetGenericArguments();
		var genericTypes = new Type[generics.Length];
		for (var i = 0; i < generics.Length; i++)
			genericTypes[i] = ResolveType(generics[i]);
		return DeclaredMethod(declaringType, reflectionOnlyMethod.Name, parameterTypes, genericTypes.Length == 0 ? null : genericTypes);
	}

	internal static PropertyInfo ResolveProperty(PropertyInfo reflectionOnlyProperty)
	{
		var declaringType = ResolveType(reflectionOnlyProperty.DeclaringType);
		return DeclaredProperty(declaringType, reflectionOnlyProperty.Name);
	}

	internal static FieldInfo ResolveField(FieldInfo reflectionOnlyField)
	{
		var declaringType = ResolveType(reflectionOnlyField.DeclaringType);
		return DeclaredField(declaringType, reflectionOnlyField.Name);
	}

	internal static Type ResolveType(Type reflectionOnlyType)
	{
		Type type;
		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			type = asm.GetType(reflectionOnlyType.FullName);
			if (type != null)
			{
				if (type.Assembly.ReflectionOnly)
					throw new TypeLoadException($"Resolved type {reflectionOnlyType.FullName} to assembly {asm.FullName}, but that assembly is reflection-only");
				// $"# {reflectionOnlyType.FullName} -> {asm.FullName}".LogMessage();
				return type;
			}
		}

		type = Type.GetType(reflectionOnlyType.FullName);
		if (type != null)
		{
			if (type.Assembly.ReflectionOnly)
				throw new TypeLoadException($"Resolved type {reflectionOnlyType.FullName} to type within a reflection-only assembly");
			// $"# {reflectionOnlyType.FullName}".LogMessage();
			return type;
		}

		throw new TypeLoadException($"Could not resolve type {reflectionOnlyType.FullName}");
	}
}