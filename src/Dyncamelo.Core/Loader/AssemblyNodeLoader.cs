using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Dyncamelo.Core.Loader;

/// <summary>
/// Discovers zero-touch node definitions in an assembly: every public static
/// method of every public class becomes a node, unless it is generic, uses
/// ref/out/pointer/params parameters, involves delegate types, or is hidden
/// with <see cref="IsVisibleInLibraryAttribute"/>.
/// </summary>
public static class AssemblyNodeLoader
{
    /// <summary>Loads node definitions from an assembly file.</summary>
    /// <param name="assemblyPath">Path to a managed assembly.</param>
    /// <returns>All importable definitions.</returns>
    public static List<NodeDefinition> LoadFrom(string assemblyPath)
    {
        if (string.IsNullOrEmpty(assemblyPath))
        {
            throw new ArgumentNullException(nameof(assemblyPath));
        }

        return LoadFrom(Assembly.LoadFrom(assemblyPath));
    }

    /// <summary>
    /// Loads node definitions from a loaded assembly. Broken types (e.g. a
    /// node-pack type referencing a host assembly that is not present) are
    /// skipped so one bad type never aborts the import of the whole pack.
    /// </summary>
    /// <param name="assembly">The assembly to reflect over.</param>
    /// <returns>All importable definitions.</returns>
    public static List<NodeDefinition> LoadFrom(Assembly assembly)
    {
        if (assembly == null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        Type?[] types;
        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Keep whatever loaded; unresolvable entries are null.
            types = ex.Types;
        }

        var definitions = new List<NodeDefinition>();
        foreach (var type in types)
        {
            try
            {
                if (type == null || !type.IsVisible || !type.IsClass || type.IsGenericTypeDefinition || IsHidden(type))
                {
                    continue;
                }

                definitions.AddRange(LoadType(type));
            }
            catch (Exception ex) when (
                ex is TypeLoadException ||
                ex is ReflectionTypeLoadException ||
                ex is BadImageFormatException ||
                ex is System.IO.FileNotFoundException ||
                ex is System.IO.FileLoadException)
            {
                // Skip types whose members cannot be reflected over.
            }
        }

        return definitions;
    }

    /// <summary>Loads node definitions from a single class.</summary>
    /// <param name="type">A public class containing public static node methods.</param>
    /// <returns>All importable definitions declared directly on the class.</returns>
    public static List<NodeDefinition> LoadType(Type type)
    {
        if (type == null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        var definitions = new List<NodeDefinition>();
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (!IsImportable(method))
            {
                continue;
            }

            definitions.Add(CreateDefinition(method));
        }

        return definitions;
    }

    /// <summary>
    /// Builds the stable serialized identity of a method:
    /// <c>Namespace.Class.Method@paramType1,paramType2</c> (no "@" for parameterless methods).
    /// </summary>
    /// <param name="method">The method to mangle.</param>
    public static string GetFunctionSignature(MethodInfo method)
    {
        if (method == null)
        {
            throw new ArgumentNullException(nameof(method));
        }

        var builder = new StringBuilder();
        builder.Append(method.DeclaringType!.FullName).Append('.').Append(method.Name);
        var parameters = method.GetParameters();
        if (parameters.Length > 0)
        {
            builder.Append('@');
            builder.Append(string.Join(",", parameters.Select(p => GetMangledTypeName(p.ParameterType))));
        }

        return builder.ToString();
    }

    private static bool IsImportable(MethodInfo method)
    {
        if (method.IsGenericMethodDefinition || method.IsSpecialName || IsHidden(method))
        {
            return false;
        }

        foreach (var parameter in method.GetParameters())
        {
            var parameterType = parameter.ParameterType;
            if (parameterType.IsByRef || parameterType.IsPointer)
            {
                return false;
            }

            if (typeof(Delegate).IsAssignableFrom(parameterType))
            {
                return false;
            }

            if (parameter.GetCustomAttribute<ParamArrayAttribute>() != null)
            {
                return false;
            }
        }

        if (method.ReturnType.IsByRef || method.ReturnType.IsPointer ||
            typeof(Delegate).IsAssignableFrom(method.ReturnType))
        {
            return false;
        }

        return true;
    }

    private static bool IsHidden(MemberInfo member)
    {
        var visibility = member.GetCustomAttribute<IsVisibleInLibraryAttribute>();
        return visibility != null && !visibility.Visible;
    }

    private static NodeDefinition CreateDefinition(MethodInfo method)
    {
        var type = method.DeclaringType!;
        var signature = GetFunctionSignature(method);
        var assemblyName = type.Assembly.GetName().Name ?? string.Empty;

        var definition = new NodeDefinition(signature, assemblyName, method)
        {
            Name = method.GetCustomAttribute<NodeNameAttribute>()?.Name ?? type.Name + "." + method.Name,
            Category = ResolveCategory(method, type, assemblyName),
            Description = method.GetCustomAttribute<NodeDescriptionAttribute>()?.Description
                ?? type.GetCustomAttribute<NodeDescriptionAttribute>()?.Description
                ?? string.Empty,
            SearchTags = method.GetCustomAttribute<NodeSearchTagsAttribute>()?.Tags ?? Array.Empty<string>(),
            Inputs = method.GetParameters().Select(CreateInputDescriptor).ToList(),
        };

        definition.MultiReturnKeys = ResolveMultiReturnKeys(method);
        definition.Outputs = CreateOutputDescriptors(method, definition.MultiReturnKeys);
        return definition;
    }

    private static string ResolveCategory(MethodInfo method, Type type, string assemblyName)
    {
        var attribute = method.GetCustomAttribute<NodeCategoryAttribute>()
            ?? type.GetCustomAttribute<NodeCategoryAttribute>();
        if (attribute != null)
        {
            return attribute.Category;
        }

        // Derive from namespace with the assembly-name prefix stripped, then the class name.
        var ns = type.Namespace ?? string.Empty;
        if (ns.StartsWith(assemblyName, StringComparison.Ordinal))
        {
            ns = ns.Substring(assemblyName.Length).TrimStart('.');
        }

        return string.IsNullOrEmpty(ns) ? type.Name : ns + "." + type.Name;
    }

    private static PortDescriptor CreateInputDescriptor(ParameterInfo parameter)
    {
        var descriptor = new PortDescriptor(parameter.Name ?? "arg", parameter.ParameterType);
        if (parameter.IsOptional)
        {
            descriptor.HasDefault = true;
            descriptor.DefaultValue = parameter.RawDefaultValue == DBNull.Value ? null : parameter.RawDefaultValue;
        }

        return descriptor;
    }

    private static IReadOnlyList<string>? ResolveMultiReturnKeys(MethodInfo method)
    {
        var attribute = method.GetCustomAttribute<MultiReturnAttribute>();
        if (attribute == null || attribute.Keys.Length == 0)
        {
            return null;
        }

        if (!typeof(System.Collections.Generic.IDictionary<string, object>).IsAssignableFrom(method.ReturnType))
        {
            // MultiReturn requires Dictionary<string, object>; ignore the attribute otherwise.
            return null;
        }

        return attribute.Keys;
    }

    private static IReadOnlyList<PortDescriptor> CreateOutputDescriptors(MethodInfo method, IReadOnlyList<string>? multiReturnKeys)
    {
        if (multiReturnKeys != null)
        {
            return multiReturnKeys.Select(k => new PortDescriptor(k, typeof(object))).ToList();
        }

        if (method.ReturnType == typeof(void))
        {
            // Void nodes pass their first input through, enabling write-sequencing chains.
            var parameters = method.GetParameters();
            var passThroughType = parameters.Length > 0 ? parameters[0].ParameterType : typeof(object);
            return new List<PortDescriptor> { new PortDescriptor("result", passThroughType) };
        }

        var name = method.ReturnParameter?.GetCustomAttribute<NodeNameAttribute>()?.Name ?? "result";
        return new List<PortDescriptor> { new PortDescriptor(name, method.ReturnType) };
    }

    private static string GetMangledTypeName(Type type)
    {
        if (type.IsArray)
        {
            return GetMangledTypeName(type.GetElementType()!) + "[]";
        }

        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable != null)
        {
            return GetMangledTypeName(nullable) + "?";
        }

        if (type.IsGenericType)
        {
            var definitionName = type.GetGenericTypeDefinition().FullName ?? type.Name;
            var backtick = definitionName.IndexOf('`');
            if (backtick >= 0)
            {
                definitionName = definitionName.Substring(0, backtick);
            }

            var arguments = string.Join(",", type.GetGenericArguments().Select(GetMangledTypeName));
            return definitionName + "<" + arguments + ">";
        }

        if (type == typeof(bool)) return "bool";
        if (type == typeof(byte)) return "byte";
        if (type == typeof(sbyte)) return "sbyte";
        if (type == typeof(char)) return "char";
        if (type == typeof(short)) return "short";
        if (type == typeof(ushort)) return "ushort";
        if (type == typeof(int)) return "int";
        if (type == typeof(uint)) return "uint";
        if (type == typeof(long)) return "long";
        if (type == typeof(ulong)) return "ulong";
        if (type == typeof(float)) return "float";
        if (type == typeof(double)) return "double";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(string)) return "string";
        if (type == typeof(object)) return "object";

        return type.FullName ?? type.Name;
    }
}
