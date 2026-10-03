using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.TestSupport.StandIns;

/// <summary>
/// The Navisworks nodes made loadable without Navisworks. <c>Dyncamelo.Navisworks</c> is made of static methods that take and return
/// Autodesk types, so it cannot be loaded on a machine without Navisworks, and a saved graph that uses it opens as a row of
/// "unresolved" placeholders. This builds, from the node catalogue (<c>docs/dyncamelo-nodes.json</c>), a dynamic assembly named
/// <c>Dyncamelo.Navisworks</c> with one public static method per node that the registry does not already know: the same class,
/// method, parameter types, names and defaults, so the node's definition id is the one saved graphs use, with the same
/// <c>[NodeName]</c>, category, description, search tags, <c>[MultiInput]</c> and <c>[MultiReturn]</c>. What the catalogue does not
/// carry (drop-down choices, ranges, name-search buttons, socket kinds, the declared function, earlier ids, retired nodes) is read
/// from the C# source of <c>Dyncamelo.Navisworks</c>. The Autodesk types are empty public classes of the same full name. The method
/// bodies do nothing: the nodes are for drawing and for loading graphs, not for running.
/// </summary>
internal sealed class StandInCatalogue
{
    /// <summary>The description the catalogue gives the single output of a node whose method returns void.</summary>
    private const string VoidOutputDescription = "The first input, passed through (for chaining writes in order).";

    private readonly List<string> _problems = new List<string>();
    private readonly List<NodeDefinition> _definitions = new List<NodeDefinition>();
    private readonly List<CatalogueNode> _interactive = new List<CatalogueNode>();
    private readonly Dictionary<string, Assembly> _assemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);
    private Type _modelItemType = typeof(object);

    private StandInCatalogue()
    {
    }

    /// <summary>Everything that did not come out as the catalogue says (an empty list is the goal; tests assert it).</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>The stand-in definitions, one per node.</summary>
    public IReadOnlyList<NodeDefinition> Definitions => _definitions;

    /// <summary>Stand-ins for interactive nodes (the "Captured Selection" input).</summary>
    public IReadOnlyList<CatalogueNode> InteractiveNodes => _interactive;

    /// <summary>The dynamic assemblies, by name.</summary>
    public IReadOnlyDictionary<string, Assembly> Assemblies => _assemblies;

    /// <summary>The stand-in types of the host application (full name to type).</summary>
    public IReadOnlyDictionary<string, Type> HostTypes { get; private set; } = new Dictionary<string, Type>();

    /// <summary>Retired nodes among the definitions (graphs saved by earlier versions still use them; the library does not offer them).</summary>
    public IEnumerable<NodeDefinition> RetiredDefinitions => _definitions.Where(d => d.IsDeprecated);

    /// <summary>
    /// Builds the stand-ins for this repository: every catalogue node the <paramref name="existing"/> registry cannot make, plus the
    /// retired Navisworks nodes found in the source.
    /// </summary>
    public static StandInCatalogue Create(NodeRegistry existing)
    {
        return Create(NodeCatalogue.Load(), SourceScan.ReadNavisworks(), existing);
    }

    /// <summary>Builds the stand-ins from a catalogue and (optionally) the source scan.</summary>
    public static StandInCatalogue Create(NodeCatalogue catalogue, SourceScan? source, NodeRegistry existing)
    {
        var result = new StandInCatalogue();
        result.Build(catalogue, source, existing);
        return result;
    }

    /// <summary>Registers the stand-ins (and the interactive stand-in node types) in a registry.</summary>
    public void RegisterInto(NodeRegistry registry)
    {
        registry.RegisterDefinitions(_definitions);
        foreach (var node in _interactive)
        {
            var captured = node;
            var itemsType = typeof(IEnumerable<>).MakeGenericType(_modelItemType);
            registry.RegisterNodeType(captured.Id, () => new CapturedSelectionNode(captured, itemsType));
        }
    }

    private void Build(NodeCatalogue catalogue, SourceScan? source, NodeRegistry existing)
    {
        var plans = new List<NodePlan>();
        var wanted = catalogue.Nodes.Where(n => n.IsZeroTouch && !IsRegistered(existing, n)).ToList();
        var assemblyNames = wanted.Select(n => n.Assembly).Distinct(StringComparer.Ordinal).ToList();
        var modules = new Dictionary<string, ModuleBuilder>(StringComparer.Ordinal);
        var typesByAssembly = new Dictionary<string, StandInTypes>(StringComparer.Ordinal);
        foreach (var name in assemblyNames)
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);
            var module = assembly.DefineDynamicModule(name);
            modules[name] = module;
            typesByAssembly[name] = new StandInTypes(module);
            _assemblies[name] = assembly;
        }

        // The host types are shared by every stand-in assembly, so they live in the first (the Navisworks) one.
        var hostTypes = assemblyNames.Count > 0 ? typesByAssembly[assemblyNames[0]] : null;
        foreach (var node in catalogue.Nodes.Where(n => n.IsZeroTouch))
        {
            foreach (var types in typesByAssembly.Values)
            {
                types.Learn(node.Id);
            }
        }

        foreach (var node in wanted)
        {
            var plan = PlanFromCatalogue(node, typesByAssembly[node.Assembly], source);
            if (plan != null)
            {
                plans.Add(plan);
            }
        }

        // Retired Navisworks nodes: not in the catalogue, but samples and older graphs still carry their ids.
        if (source != null && typesByAssembly.TryGetValue("Dyncamelo.Navisworks", out var navisworksTypes))
        {
            var known = new HashSet<string>(plans.Select(p => p.ExpectedId), StringComparer.Ordinal);
            foreach (var method in source.Methods.Where(m => m.IsDeprecated))
            {
                var plan = PlanFromRetiredSource(method, navisworksTypes);
                if (plan != null && known.Add(plan.ExpectedId))
                {
                    plans.Add(plan);
                }
            }
        }

        // Emit one static class per class name, then let the real loader read the result.
        foreach (var group in plans.GroupBy(p => p.AssemblyName + "|" + p.ClassFullName))
        {
            var first = group.First();
            var module = modules[first.AssemblyName];
            var type = module.DefineType(first.ClassFullName, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class);
            foreach (var plan in group)
            {
                try
                {
                    Emit(type, plan, typesByAssembly[plan.AssemblyName]);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is NotSupportedException)
                {
                    _problems.Add(plan.ExpectedId + ": " + ex.Message);
                }
            }

            var created = type.CreateType()!;
            foreach (var definition in AssemblyNodeLoader.LoadType(created))
            {
                var plan = group.FirstOrDefault(p => p.ExpectedId == definition.Id);
                if (plan == null)
                {
                    _problems.Add("The stand-in assembly produced an unexpected definition id: " + definition.Id);
                    continue;
                }

                Finish(definition, plan);
                _definitions.Add(definition);
            }
        }

        foreach (var plan in plans)
        {
            if (!_definitions.Any(d => d.Id == plan.ExpectedId))
            {
                _problems.Add("No definition came out for " + plan.ExpectedId);
            }
        }

        if (hostTypes != null)
        {
            var all = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var types in typesByAssembly.Values)
            {
                foreach (var pair in types.StandIns)
                {
                    all[pair.Key] = pair.Value;
                }
            }

            all.TryGetValue("Autodesk.Navisworks.Api.ModelItem", out var modelItem);
            _modelItemType = modelItem ?? typeof(object);
            RegisterConverters(hostTypes, all);
            HostTypes = all;
        }

        foreach (var node in catalogue.Nodes.Where(n => n.Interactive && n.Id.Length > 0 && existing.CreateNode(n.Id) == null))
        {
            if (node.Id == CapturedSelectionNode.TypeName)
            {
                _interactive.Add(node);
            }
            else
            {
                _problems.Add("The interactive node '" + node.Name + "' (" + node.Id + ") is not registered and has no stand-in.");
            }
        }
    }

    /// <summary>
    /// The conversions the real <c>NavisworksTypeConverters</c> registers between Dyncamelo's own value types and the Navisworks ones
    /// (colour, point, bounding box, a picked element), so that a wire between a general node and a stand-in is judged exactly as the
    /// real one is. Nothing is converted here (the stand-ins never run), only the pairs are made known.
    /// </summary>
    private static void RegisterConverters(StandInTypes types, Dictionary<string, Type> host)
    {
        Type Host(string name)
        {
            var full = "Autodesk.Navisworks.Api." + name;
            var type = types.FromFullName(full);
            host[full] = type;
            return type;
        }

        Func<object, object?> none = value => value;
        var colour = Host("Color");
        var point = Host("Point3D");
        var box = Host("BoundingBox3D");
        var item = Host("ModelItem");
        var collection = Host("ModelItemCollection");
        Dyncamelo.Core.Types.TypeCoercion.RegisterConverter(typeof(Dyncamelo.Nodes.DyncameloColor), colour, none);
        Dyncamelo.Core.Types.TypeCoercion.RegisterConverter(typeof(string), colour, none);
        Dyncamelo.Core.Types.TypeCoercion.RegisterConverter(typeof(string), item, none);
        Dyncamelo.Core.Types.TypeCoercion.RegisterConverter(typeof(string), typeof(List<>).MakeGenericType(item), none);
        Dyncamelo.Core.Types.TypeCoercion.RegisterConverter(typeof(string), collection, none);
        Dyncamelo.Core.Types.TypeCoercion.RegisterConverter(typeof(Dyncamelo.Nodes.DyncameloPoint), point, none);
        Dyncamelo.Core.Types.TypeCoercion.RegisterConverter(point, typeof(Dyncamelo.Nodes.DyncameloPoint), none);
        Dyncamelo.Core.Types.TypeCoercion.RegisterConverter(box, typeof(Dyncamelo.Nodes.DyncameloBoundingBox), none);
        Dyncamelo.Core.Types.TypeCoercion.RegisterConverter(typeof(Dyncamelo.Nodes.DyncameloBoundingBox), box, none);
        Dyncamelo.Core.Types.TypeCoercion.RegisterConverter(item, typeof(Dyncamelo.Nodes.DyncameloBoundingBox), none);
    }

    private static bool IsRegistered(NodeRegistry registry, CatalogueNode node)
    {
        return registry.TryGetDefinition(node.Id, out _);
    }

    // ---------------------------------------------------------------- planning

    private NodePlan? PlanFromCatalogue(CatalogueNode node, StandInTypes types, SourceScan? source)
    {
        var idTypes = node.IdParameterTypes;
        if (idTypes.Count != node.Inputs.Count)
        {
            _problems.Add(node.Id + ": the id names " + idTypes.Count + " parameter types but the catalogue lists " + node.Inputs.Count + " inputs.");
            return null;
        }

        var path = node.IdPath;
        var lastDot = path.LastIndexOf('.');
        var scanned = source?.Find(path, node.Inputs.Select(i => i.Name));
        if (source != null && scanned == null)
        {
            _problems.Add(node.Id + ": no method with these parameter names was found in the source of " + node.Assembly + ".");
        }

        var plan = new NodePlan
        {
            AssemblyName = node.Assembly,
            ClassFullName = path.Substring(0, lastDot),
            MethodName = path.Substring(lastDot + 1),
            ExpectedId = node.Id,
            Name = node.Name,
            Category = node.Category,
            Description = node.Description,
            Tags = node.Tags.ToArray(),
            Source = scanned,
            Catalogue = node,
        };

        for (var i = 0; i < node.Inputs.Count; i++)
        {
            var input = node.Inputs[i];
            var parameter = new ParameterPlan
            {
                Name = input.Name,
                Type = types.FromId(idTypes[i]),
                MultiInput = input.MultiInput,
                Description = input.Description,
                Source = scanned?.Parameters[i],
            };

            if (input.HasDefault)
            {
                parameter.Optional = true;
                if (TypeNames.TryParseDefault(input.DefaultText!, parameter.Type, out var value))
                {
                    parameter.Default = value;
                }
                else
                {
                    _problems.Add(node.Id + ": the default '" + input.DefaultText + "' of '" + input.Name + "' could not be read as " + parameter.Type.Name + ".");
                }
            }

            plan.Parameters.Add(parameter);
        }

        // The outputs: the declared keys of a multi-output node, else one port named by the catalogue.
        var multiKeys = scanned?.Attribute("MultiReturn")?.Strings();
        if (multiKeys == null && scanned == null && node.Outputs.Count > 1)
        {
            multiKeys = node.Outputs.Select(o => o.Name).ToArray();
        }

        if (multiKeys != null && multiKeys.Length > 0)
        {
            plan.MultiKeys = multiKeys;
            plan.ReturnType = typeof(Dictionary<string, object>);
            if (!multiKeys.SequenceEqual(node.Outputs.Select(o => o.Name)))
            {
                _problems.Add(node.Id + ": the catalogue's outputs [" + string.Join(", ", node.Outputs.Select(o => o.Name)) + "] differ from the [MultiReturn] keys in the source.");
            }

            var kinds = scanned?.Attribute("PortKinds")?.Strings();
            if (kinds == null && scanned == null)
            {
                kinds = node.Outputs.Select(o => Dyncamelo.Core.Editing.PortKinds.ToHint(Dyncamelo.Core.Editing.PortKinds.FromTypeName(o.Type))).ToArray();
            }

            plan.Kinds = kinds;
        }
        else
        {
            plan.OutputName = scanned?.ReturnAttribute("NodeName")?.Strings().FirstOrDefault()
                ?? (node.Outputs.Count > 0 ? node.Outputs[0].Name : "result");
            if (scanned != null)
            {
                plan.ReturnType = types.FromSource(scanned.ReturnText);
            }
            else if (node.Outputs.Count == 0 || node.Outputs[0].Description == VoidOutputDescription)
            {
                plan.ReturnType = typeof(void);
            }
            else
            {
                plan.ReturnType = types.FromCatalogue(node.Outputs[0].Type);
            }
        }

        return plan;
    }

    private NodePlan? PlanFromRetiredSource(SourceMethod method, StandInTypes types)
    {
        try
        {
            var plan = new NodePlan
            {
                AssemblyName = "Dyncamelo.Navisworks",
                ClassFullName = method.Namespace + "." + method.ClassName,
                MethodName = method.MethodName,
                Source = method,
                Name = method.Attribute("NodeName")?.Strings().FirstOrDefault() ?? method.ClassName + "." + method.MethodName,
                Description = method.Attribute("NodeDescription")?.Strings().FirstOrDefault()
                    ?? method.ClassAttributes.FirstOrDefault(a => a.Name == "NodeDescription")?.Strings().FirstOrDefault()
                    ?? string.Empty,
                Tags = method.Attribute("NodeSearchTags")?.Strings() ?? new string[0],
            };

            var ns = method.Namespace;
            if (ns.StartsWith("Dyncamelo.Navisworks", StringComparison.Ordinal))
            {
                ns = ns.Substring("Dyncamelo.Navisworks".Length).TrimStart('.');
            }

            plan.Category = method.Attribute("NodeCategory")?.Strings().FirstOrDefault()
                ?? method.ClassAttributes.FirstOrDefault(a => a.Name == "NodeCategory")?.Strings().FirstOrDefault()
                ?? (ns.Length == 0 ? method.ClassName : ns + "." + method.ClassName);

            foreach (var p in method.Parameters)
            {
                var parameter = new ParameterPlan { Name = p.Name, Type = types.FromSource(p.TypeText), Source = p, Optional = p.IsOptional };
                if (p.IsOptional)
                {
                    if (TypeNames.TryParseDefault(p.DefaultText!, parameter.Type, out var value))
                    {
                        parameter.Default = value;
                    }
                    else
                    {
                        _problems.Add(method.Path + ": the default '" + p.DefaultText + "' of '" + p.Name + "' could not be read.");
                    }
                }

                parameter.MultiInput = p.Attribute("MultiInput") != null;
                plan.Parameters.Add(parameter);
            }

            plan.ExpectedId = method.Path + (plan.Parameters.Count > 0 ? "@" + string.Join(",", plan.Parameters.Select(p => TypeNames.Mangle(p.Type))) : string.Empty);
            var multiKeys = method.Attribute("MultiReturn")?.Strings();
            if (multiKeys != null && multiKeys.Length > 0)
            {
                plan.MultiKeys = multiKeys;
                plan.Kinds = method.Attribute("PortKinds")?.Strings();
                plan.ReturnType = typeof(Dictionary<string, object>);
            }
            else
            {
                plan.OutputName = method.ReturnAttribute("NodeName")?.Strings().FirstOrDefault() ?? "result";
                plan.ReturnType = types.FromSource(method.ReturnText);
            }

            return plan;
        }
        catch (Exception ex) when (ex is FormatException || ex is NotSupportedException || ex is ArgumentException)
        {
            _problems.Add(method.Path + " (retired): " + ex.Message);
            return null;
        }
    }

    // ---------------------------------------------------------------- emitting

    private void Emit(TypeBuilder type, NodePlan plan, StandInTypes types)
    {
        var parameterTypes = plan.Parameters.Select(p => p.Type).ToArray();
        var method = type.DefineMethod(
            plan.MethodName,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            plan.ReturnType,
            parameterTypes);

        method.SetCustomAttribute(OneString(typeof(NodeNameAttribute), plan.Name));
        method.SetCustomAttribute(OneString(typeof(NodeCategoryAttribute), plan.Category));
        if (plan.Description.Length > 0)
        {
            method.SetCustomAttribute(OneString(typeof(NodeDescriptionAttribute), plan.Description));
        }

        if (plan.Tags.Length > 0)
        {
            method.SetCustomAttribute(Strings(typeof(NodeSearchTagsAttribute), plan.Tags));
        }

        if (plan.MultiKeys != null)
        {
            method.SetCustomAttribute(Strings(typeof(MultiReturnAttribute), plan.MultiKeys));
            if (plan.Kinds != null && plan.Kinds.Length > 0)
            {
                method.SetCustomAttribute(Strings(typeof(PortKindsAttribute), plan.Kinds));
            }
        }
        else if (plan.OutputName != null && plan.ReturnType != typeof(void))
        {
            var returnValue = method.DefineParameter(0, ParameterAttributes.None, null);
            returnValue.SetCustomAttribute(OneString(typeof(NodeNameAttribute), plan.OutputName));
        }

        var source = plan.Source;
        var function = source?.Attribute("NodeFunction");
        if (function != null && function.Positional.Count > 0)
        {
            var member = function.Positional[0].Trim();
            member = member.Substring(member.LastIndexOf('.') + 1);
            if (Enum.TryParse(member, out NodeFunction value))
            {
                method.SetCustomAttribute(new CustomAttributeBuilder(typeof(NodeFunctionAttribute).GetConstructor(new[] { typeof(NodeFunction) })!, new object[] { value }));
            }
        }

        var aliases = source?.Attribute("NodeAliases")?.Strings();
        if (aliases != null && aliases.Length > 0)
        {
            method.SetCustomAttribute(Strings(typeof(NodeAliasesAttribute), aliases));
        }

        var deprecated = source?.Attribute("NodeDeprecated");
        if (deprecated != null)
        {
            method.SetCustomAttribute(OneString(typeof(NodeDeprecatedAttribute), deprecated.Strings().FirstOrDefault() ?? string.Empty));
        }

        for (var i = 0; i < plan.Parameters.Count; i++)
        {
            var parameter = plan.Parameters[i];
            var attributes = ParameterAttributes.None;
            if (parameter.Optional)
            {
                attributes |= ParameterAttributes.Optional;
                if (parameter.Default != null || !parameter.Type.IsValueType)
                {
                    attributes |= ParameterAttributes.HasDefault;
                }
            }

            var builder = method.DefineParameter(i + 1, attributes, parameter.Name);
            if ((attributes & ParameterAttributes.HasDefault) != 0)
            {
                builder.SetConstant(parameter.Default);
            }

            if (parameter.MultiInput)
            {
                builder.SetCustomAttribute(new CustomAttributeBuilder(typeof(MultiInputAttribute).GetConstructor(Type.EmptyTypes)!, new object[0]));
            }

            EmitParameterAttributes(builder, parameter, types);
        }

        var il = method.GetILGenerator();
        if (plan.ReturnType != typeof(void))
        {
            var local = il.DeclareLocal(plan.ReturnType);
            il.Emit(OpCodes.Ldloc, local);
        }

        il.Emit(OpCodes.Ret);
    }

    private static void EmitParameterAttributes(ParameterBuilder builder, ParameterPlan parameter, StandInTypes types)
    {
        var source = parameter.Source;
        if (source == null)
        {
            return;
        }

        var choices = source.Attribute("NodeChoices");
        if (choices != null)
        {
            builder.SetCustomAttribute(Strings(typeof(NodeChoicesAttribute), choices.Strings()));
        }

        var fromEnum = source.Attribute("NodeChoicesFromEnum");
        if (fromEnum != null && fromEnum.Positional.Count > 0)
        {
            var text = fromEnum.Positional[0].Trim();
            if (text.StartsWith("typeof(", StringComparison.Ordinal) && text.EndsWith(")", StringComparison.Ordinal))
            {
                var enumName = text.Substring(7, text.Length - 8).Trim();
                var simpleName = enumName.Substring(enumName.LastIndexOf('.') + 1);
                var enumType = types.StandIns.Values.FirstOrDefault(t => t.IsEnum && t.Name == simpleName);
                if (enumType != null)
                {
                    // The attribute itself would name the stand-in enum in its blob, which the runtime cannot load by name; its effect is the
                    // list of choices, so the list is written out.
                    var extra = fromEnum.Positional.Skip(1).Select(TypeNames.ParseStringLiteralExpression).Where(s => s != null).Select(s => s!);
                    builder.SetCustomAttribute(Strings(typeof(NodeChoicesAttribute), extra.Concat(Enum.GetNames(enumType)).ToArray()));
                }
            }
        }

        var range = source.Attribute("NodeRange");
        var min = range?.Number(0);
        var max = range?.Number(1);
        if (range != null && min.HasValue && max.HasValue)
        {
            var names = new List<PropertyInfo>();
            var values = new List<object>();
            foreach (var name in new[] { "SoftMin", "SoftMax", "Step" })
            {
                var number = range.NamedNumber(name);
                if (number.HasValue)
                {
                    names.Add(typeof(NodeRangeAttribute).GetProperty(name)!);
                    values.Add(number.Value);
                }
            }

            var unit = range.NamedString("Unit");
            if (unit != null)
            {
                names.Add(typeof(NodeRangeAttribute).GetProperty("Unit")!);
                values.Add(unit);
            }

            builder.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(NodeRangeAttribute).GetConstructor(new[] { typeof(double), typeof(double) })!,
                new object[] { min.Value, max.Value },
                names.ToArray(),
                values.ToArray()));
        }

        var tab = source.Attribute("NodeTabChoice");
        if (tab != null && tab.Strings().Length > 0)
        {
            builder.SetCustomAttribute(WithAncestors(typeof(NodeTabChoiceAttribute), tab, tab.Strings()[0]));
        }

        var property = source.Attribute("NodePropertyChoice");
        if (property != null && property.Strings().Length > 1)
        {
            builder.SetCustomAttribute(WithAncestors(typeof(NodePropertyChoiceAttribute), property, property.Strings()[0], property.Strings()[1]));
        }

        var panel = source.Attribute("NodePanel");
        if (panel != null && panel.Strings().Length > 0)
        {
            builder.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(NodePanelAttribute).GetConstructor(new[] { typeof(string) })!,
                new object[] { panel.Strings()[0] },
                new[] { typeof(NodePanelAttribute).GetProperty("DefaultOpen")! },
                new object[] { panel.NamedBool("DefaultOpen") }));
        }

        var kinds = source.Attribute("PortKinds");
        if (kinds != null)
        {
            builder.SetCustomAttribute(Strings(typeof(PortKindsAttribute), kinds.Strings()));
        }
    }

    private static CustomAttributeBuilder WithAncestors(Type attribute, SourceAttribute source, params string[] arguments)
    {
        var constructor = attribute.GetConstructor(arguments.Select(_ => typeof(string)).ToArray())!;
        return new CustomAttributeBuilder(
            constructor,
            arguments.Cast<object>().ToArray(),
            new[] { attribute.GetProperty("IncludeAncestors")! },
            new object[] { source.NamedBool("IncludeAncestors") });
    }

    private static CustomAttributeBuilder OneString(Type attribute, string value)
    {
        return new CustomAttributeBuilder(attribute.GetConstructor(new[] { typeof(string) })!, new object[] { value });
    }

    private static CustomAttributeBuilder Strings(Type attribute, string[] values)
    {
        return new CustomAttributeBuilder(attribute.GetConstructor(new[] { typeof(string[]) })!, new object[] { values });
    }

    // ---------------------------------------------------------------- finishing

    private static void Finish(NodeDefinition definition, NodePlan plan)
    {
        // Port descriptions come from the XML documentation of the real assembly; the catalogue carries the same text.
        if (plan.Catalogue != null)
        {
            foreach (var input in definition.Inputs)
            {
                var described = plan.Catalogue.Inputs.FirstOrDefault(i => i.Name == input.Name);
                if (described != null)
                {
                    input.Description = described.Description;
                }
            }

            if (plan.MultiKeys == null && definition.Outputs.Count == 1 && plan.Catalogue.Outputs.Count == 1 && definition.Outputs[0].Name == plan.Catalogue.Outputs[0].Name
                && plan.Catalogue.Outputs[0].Description != VoidOutputDescription)
            {
                definition.Outputs[0].Description = plan.Catalogue.Outputs[0].Description;
            }
        }
    }

    private sealed class ParameterPlan
    {
        public string Name { get; set; } = string.Empty;

        public Type Type { get; set; } = typeof(object);

        public bool Optional { get; set; }

        public object? Default { get; set; }

        public bool MultiInput { get; set; }

        public string Description { get; set; } = string.Empty;

        public SourceParameter? Source { get; set; }
    }

    private sealed class NodePlan
    {
        public string AssemblyName { get; set; } = string.Empty;

        public string ClassFullName { get; set; } = string.Empty;

        public string MethodName { get; set; } = string.Empty;

        public string ExpectedId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string[] Tags { get; set; } = new string[0];

        public List<ParameterPlan> Parameters { get; } = new List<ParameterPlan>();

        public Type ReturnType { get; set; } = typeof(object);

        public string? OutputName { get; set; }

        public string[]? MultiKeys { get; set; }

        public string[]? Kinds { get; set; }

        public SourceMethod? Source { get; set; }

        public CatalogueNode? Catalogue { get; set; }
    }
}
