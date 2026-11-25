using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Emit;

/// <summary>Generates the service collection extension (ServiceCollection.Output.spec).</summary>
internal static class ServiceCollectionExtensionEmitter
{
    private const string Services = "global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions";
    private const string Provider = "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions";
    private const string KeyedProvider = "global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions";
    private const string Holder = "global::SourceGen.Ioc.IocHolder";

    /// <summary>Gets the hint name of the generated file.</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The hint name.</returns>
    public static string HintName(ServiceCollectionExtensionSettings settings) => $"{settings.Name}ServiceCollectionExtensions.g.cs";

    /// <summary>Generates the source of the class that contains the method.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The source.</returns>
    public static string Generate(ServiceCollectionExtensionModel model)
    {
        ServiceCollectionExtensionSettings settings = model.Settings;
        using SourceWriter writer = new();
        GeneratedFile.WriteHeader(writer);
        writer.WriteLine();
        writer.WriteLine($"namespace {settings.Namespace};");
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"public static class {settings.Name}ServiceCollectionExtensions");
        writer.WriteLine('{');
        writer.Indentation++;

        foreach(ClosedDefinition definition in model.Definitions)
        {
            if(HasHolder(definition))
            {
                writer.WriteLine($"private static readonly object {GeneratedNames.For("__k_", definition)} = new();");
            }
        }

        if(model.Definitions.Any(HasHolder))
        {
            writer.WriteLine();
        }

        Dictionary<(TypeRef Type, string Key), List<int>> providers = Analysis.DependencyGraph.Providers(model.Definitions);
        WriteMethod(writer, model);
        foreach(ClosedDefinition definition in model.Definitions)
        {
            writer.WriteLine();
            WriteCreate(writer, definition, providers);
            if(HasHolder(definition))
            {
                WriteGet(writer, definition);
            }
        }

        // The creation methods that dispose the instance when a step after new fails
        if(model.Definitions.Any(static d => d.HasStepsAfterNew && d.Disposal != Disposal.None))
        {
            ContainerEmitter.WriteDisposeLost(writer);
        }

        writer.Indentation--;
        writer.WriteLine('}');
        return writer.ToString();
    }

    private static bool HasHolder(ClosedDefinition definition) => definition.Lifetime != Lifetime.Transient;

    private static void WriteMethod(SourceWriter writer, ServiceCollectionExtensionModel model)
    {
        ServiceCollectionExtensionSettings settings = model.Settings;
        writer.WriteLine($"public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add{settings.Name}(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, params global::System.ReadOnlySpan<string> tags)");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine($"global::SourceGen.Ioc.IocRegistrationMarker.Enter(services, \"{settings.Namespace}.{settings.Name}ServiceCollectionExtensions.Add{settings.Name}\", \"{settings.AssemblyName}\");");

        Dictionary<string, List<(TypeRef Wrapper, KeyRef? Key)>> wrappers = WrapperDemands(model);
        foreach(ClosedDefinition definition in model.Definitions)
        {
            WriteRegistrations(writer, definition);
            WriteWrapperRegistrations(writer, definition, wrappers);
        }

        WriteCollectionRegistrations(writer, model);
        writer.WriteLine("return services;");
        writer.Indentation--;
        writer.WriteLine('}');
    }

    private static void WriteRegistrations(SourceWriter writer, ClosedDefinition definition)
    {
        string implementation = TypeNames.CSharp(definition.Implementation);
        string lifetime = definition.Lifetime.ToString();
        writer.WriteLine();
        if(HasHolder(definition))
        {
            writer.WriteLine($"{Services}.AddKeyed{lifetime}<{Holder}<{implementation}>>(services, {GeneratedNames.For("__k_", definition)}, static (sp, _) => {Holder}.Own({GeneratedNames.For("__Create_", definition)}(sp)));");
        }

        // A service type with a key uses the keyed registration methods (ServiceCollection.Output.spec, "Registration for each lifetime")
        string source = HasHolder(definition) ? GeneratedNames.For("__Get_", definition) : GeneratedNames.For("__Create_", definition);
        foreach(TypeRef serviceType in definition.ServiceTypes)
        {
            string service = TypeNames.CSharp(serviceType);
            string cast = serviceType.Equals(definition.Implementation) ? string.Empty : $"({service})";
            if(definition.Key is { } key)
            {
                writer.WriteLine($"{Services}.AddKeyed{lifetime}<{service}>(services, {KeyNames.Expression(key)}, static (sp, _) => {cast}{source}(sp));");
            }
            else
            {
                writer.WriteLine($"{Services}.Add{lifetime}<{service}>(services, static sp => {cast}{source}(sp));");
            }
        }
    }

    /// <summary>
    /// The <c>Lazy</c> and <c>Func</c> wrapper types that the dependency sites ask for, alone or as the element of a
    /// collection, by the canonical string of the service, with the key of each demand. A <c>Task&lt;S&gt;</c> alone is
    /// not registered (ServiceCollection.Output.spec, "Wrapper types and collections").
    /// </summary>
    private static Dictionary<string, List<(TypeRef Wrapper, KeyRef? Key)>> WrapperDemands(ServiceCollectionExtensionModel model)
    {
        Dictionary<string, List<(TypeRef, KeyRef?)>> demands = new(StringComparer.Ordinal);
        foreach(ClosedDefinition definition in model.Definitions)
        {
            foreach(Dependency dependency in definition.Dependencies.Where(static d => d.KeyMode != SiteKeyMode.ServiceKey))
            {
                if(WrapperRequest.Parse(dependency.Type) is not { Deferred: not DeferredKind.None } request)
                {
                    continue;
                }

                KeyRef? key = dependency.KeyIn(definition);
                TypeRef? wrapper = request.IsSingleWrapper ? dependency.Type : request.IsSimpleCollection ? WrapperRequest.ElementOf(dependency.Type) : null;
                if(wrapper is not null)
                {
                    if(!demands.TryGetValue(request.Core.CanonicalString, out List<(TypeRef, KeyRef?)>? list))
                    {
                        demands[request.Core.CanonicalString] = list = [];
                    }

                    list.Add((wrapper, key));
                }
            }
        }

        return demands;
    }

    /// <summary>
    /// One registration of each wrapper type that a dependency site asks for, for each service type of the closed
    /// definition, with the key of the definition. It points to that closed definition, so the last registration wins
    /// and MS.DI collects them in the order of the definitions. <c>Lazy</c> has the lifetime of the definition. <c>Func</c>
    /// is Singleton for a Singleton, and Transient otherwise (ServiceCollection.Output.spec, "Wrapper types and collections").
    /// A demand matches the definition when the keys have the same identity, when it asks for all keyed registrations,
    /// or when the keys can be equal at run time: MS.DI then compares them with <c>Equals</c>, as for the service itself.
    /// </summary>
    private static void WriteWrapperRegistrations(SourceWriter writer, ClosedDefinition definition, Dictionary<string, List<(TypeRef Wrapper, KeyRef? Key)>> demands)
    {
        string source = HasHolder(definition) ? GeneratedNames.For("__Get_", definition) : GeneratedNames.For("__Create_", definition);
        foreach(TypeRef serviceType in definition.ServiceTypes)
        {
            if(!demands.TryGetValue(serviceType.CanonicalString, out List<(TypeRef Wrapper, KeyRef? Key)>? list))
            {
                continue;
            }

            SortedDictionary<string, TypeRef> types = new(StringComparer.Ordinal);
            foreach((TypeRef wrapper, _) in list.Where(d => Matches(d.Key, definition.Key)))
            {
                types[wrapper.CanonicalString] = wrapper;
            }

            foreach(TypeRef type in types.Values)
            {
                WrapperRequest request = WrapperRequest.Parse(type)!;
                Lifetime lifetime = request.Deferred == DeferredKind.Func && definition.Lifetime != Lifetime.Singleton ? Lifetime.Transient : definition.Lifetime;
                string value = WrapperExpression(request, $"({TypeNames.CSharp(serviceType)}){source}(sp)", cached: lifetime != Lifetime.Transient);
                string wrapper = TypeNames.CSharp(type);
                if(definition.Key is { } registered)
                {
                    writer.WriteLine($"{Services}.AddKeyed{lifetime}<{wrapper}>(services, {KeyNames.Expression(registered)}, static (sp, _) => {value});");
                }
                else
                {
                    writer.WriteLine($"{Services}.Add{lifetime}<{wrapper}>(services, static sp => {value});");
                }
            }
        }
    }

    /// <summary>Whether a demand with the key <paramref name="demand"/> can ask for a registration with the key <paramref name="registered"/>.</summary>
    private static bool Matches(KeyRef? demand, KeyRef? registered) => (demand, registered) switch
    {
        (null, null) => true,
        ({ IsAnyKey: true }, not null) => true,
        (not null, not null) => demand.Equals(registered) || Analysis.ContainerAnalysis.MayBeEqual(demand, registered),
        _ => false,
    };

    /// <summary>
    /// The expression that creates a wrapper around <paramref name="service"/>, from the inside to the outside. A
    /// synchronous service in <c>Task&lt;T&gt;</c> is a completed task. A cached <c>Lazy</c> does not keep an exception, as
    /// in the container (Container.Output.spec, "Wrapper types").
    /// </summary>
    private static string WrapperExpression(WrapperRequest request, string service, bool cached)
    {
        string core = TypeNames.CSharp(request.Core);
        string value = request.IsTask ? $"global::System.Threading.Tasks.Task.FromResult<{core}>({service})" : service;
        string result = request.IsTask ? $"global::System.Threading.Tasks.Task<{core}>" : core;
        return request.Deferred switch
        {
            DeferredKind.Lazy when cached => $"new global::System.Lazy<{result}>(() => {value}, global::System.Threading.LazyThreadSafetyMode.PublicationOnly)",
            DeferredKind.Lazy => $"new global::System.Lazy<{result}>(() => {value}, global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication)",
            DeferredKind.Func => $"new global::System.Func<{result}>(() => {value})",
            _ => value,
        };
    }

    /// <summary>
    /// The collection types other than <c>IEnumerable&lt;T&gt;</c> that a dependency site asks for: each one is registered
    /// as one Transient service, an array of the services that MS.DI collects (ServiceCollection.Output.spec, "Wrapper
    /// types and collections"). A collection of <c>Task&lt;S&gt;</c> is registered also as <c>IEnumerable</c>, because
    /// <c>Task&lt;S&gt;</c> has no registration. A dependency site with <c>AnyKey</c> builds its array itself: MS.DI cannot
    /// resolve a single service with <c>AnyKey</c>.
    /// </summary>
    private static void WriteCollectionRegistrations(SourceWriter writer, ServiceCollectionExtensionModel model)
    {
        SortedDictionary<string, (TypeRef Type, WrapperRequest Request, KeyRef? Key)> demands = new(StringComparer.Ordinal);
        foreach(ClosedDefinition definition in model.Definitions)
        {
            foreach(Dependency dependency in definition.Dependencies.Where(static d => d.KeyMode != SiteKeyMode.ServiceKey))
            {
                KeyRef? key = dependency.KeyIn(definition);
                if(Collection(dependency.Type) is { } request
                    && (dependency.Type is not NamedTypeRef { MetadataName: WrapperRequest.Enumerable } || IsTaskElement(request))
                    && key is not { IsAnyKey: true })
                {
                    demands[$"{dependency.Type.CanonicalString}|{KeyRef.CanonicalOf(key)}"] = (dependency.Type, request, key);
                }
            }
        }

        if(demands.Count > 0)
        {
            writer.WriteLine();
        }

        foreach((TypeRef type, WrapperRequest request, KeyRef? key) in demands.Values)
        {
            string collection = TypeNames.CSharp(type);
            if(key is null)
            {
                writer.WriteLine($"{Services}.AddTransient<{collection}>(services, static sp => {Elements(type, request, null)});");
            }
            else
            {
                writer.WriteLine($"{Services}.AddKeyedTransient<{collection}>(services, {KeyNames.Expression(key)}, static (sp, key) => {Elements(type, request, "key")});");
            }
        }
    }

    /// <summary>
    /// The array of the elements that MS.DI collects. A <c>Task&lt;S&gt;</c> wraps each service <c>S</c>.
    /// </summary>
    /// <param name="type">The collection type.</param>
    /// <param name="request">The collection type, taken apart by the grammar.</param>
    /// <param name="key">The expression of the key, or <see langword="null"/>.</param>
    private static string Elements(TypeRef type, WrapperRequest request, string? key)
    {
        TypeRef element = IsTaskElement(request) ? request.Core : WrapperRequest.ElementOf(type)!;
        string all = key is null
            ? $"{Provider}.GetServices<{TypeNames.CSharp(element)}>(sp)"
            : $"{KeyedProvider}.GetKeyedServices<{TypeNames.CSharp(element)}>(sp, {key})";
        return IsTaskElement(request)
            ? $"global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select({all}, static s => global::System.Threading.Tasks.Task.FromResult(s)))"
            : $"global::System.Linq.Enumerable.ToArray({all})";
    }

    /// <summary>A requested collection whose element is the service itself or a wrapper of it, or <see langword="null"/>.</summary>
    private static WrapperRequest? Collection(TypeRef type) => WrapperRequest.Parse(type) is { IsSimpleCollection: true } request ? request : null;

    /// <summary>Whether the element of a collection is <c>Task&lt;S&gt;</c>, which has no registration.</summary>
    private static bool IsTaskElement(WrapperRequest request) => request is { IsTask: true, Deferred: DeferredKind.None };

    /// <summary>
    /// The expression of a collection. It is never missing, so an optional one is resolved the same way (Analysis.Model.spec,
    /// "Multiple services").
    /// </summary>
    private static string CollectionArgument(TypeRef type, WrapperRequest request, KeyRef? key)
    {
        string service = TypeNames.CSharp(WrapperRequest.ElementOf(type)!);
        if(type is NamedTypeRef { MetadataName: WrapperRequest.Enumerable } && !IsTaskElement(request))
        {
            return key is null ? $"{Provider}.GetServices<{service}>(sp)" : $"{KeyedProvider}.GetKeyedServices<{service}>(sp, {KeyNames.Expression(key)})";
        }

        string collection = TypeNames.CSharp(type);
        if(key is { IsAnyKey: true })
        {
            return Elements(type, request, KeyNames.Expression(key));
        }

        return key is null ? $"{Provider}.GetRequiredService<{collection}>(sp)" : $"{KeyedProvider}.GetRequiredKeyedService<{collection}>(sp, {KeyNames.Expression(key)})";
    }

    /// <summary>
    /// The expression of a <c>Lazy</c>, <c>Func</c> or <c>Task</c> that the dependency site creates itself, or
    /// <see langword="null"/> when it resolves a registered wrapper. A <c>Task&lt;S&gt;</c> is never registered. A
    /// <c>Lazy&lt;S&gt;</c> or <c>Func&lt;S&gt;</c> whose <c>S</c> has no candidate in the assembly view is created when no
    /// registration of it exists (ServiceCollection.Output.spec, "Dependency resolution"; ADR IocRewrite/0012). Inside the
    /// wrapper, an optional site resolves <c>S</c> with <c>GetService</c>, so the value can be <see langword="null"/>.
    /// </summary>
    private static string? WrapperArgument(ClosedDefinition owner, Dependency parameter, Dictionary<(TypeRef Type, string Key), List<int>> providers)
    {
        if(WrapperRequest.Parse(parameter.Type) is not { IsSingleWrapper: true } request)
        {
            return null;
        }

        KeyRef? key = parameter.KeyIn(owner);
        if(request.Deferred != DeferredKind.None && providers.ContainsKey((request.Core, KeyRef.CanonicalOf(key))))
        {
            return null;
        }

        string core = TypeNames.CSharp(request.Core);
        string service = (key, parameter.Optional) switch
        {
            (null, OptionalMode.Required) => $"{Provider}.GetRequiredService<{core}>(sp)",
            (null, _) => $"{Provider}.GetService<{core}>(sp)!",
            (_, OptionalMode.Required) => $"{KeyedProvider}.GetRequiredKeyedService<{core}>(sp, {KeyNames.Expression(key)})",
            _ => $"{KeyedProvider}.GetKeyedService<{core}>(sp, {KeyNames.Expression(key)})!",
        };
        string created = WrapperExpression(request, service, cached: false);
        if(request.Deferred == DeferredKind.None)
        {
            return created;
        }

        string type = TypeNames.CSharp(parameter.Type);
        return key is null
            ? $"{Provider}.GetService<{type}>(sp) ?? {created}"
            : $"{KeyedProvider}.GetKeyedService<{type}>(sp, {KeyNames.Expression(key)}) ?? {created}";
    }

    private static void WriteCreate(SourceWriter writer, ClosedDefinition definition, Dictionary<(TypeRef Type, string Key), List<int>> providers)
    {
        string implementation = TypeNames.CSharp(definition.Implementation);
        writer.WriteLine($"private static {implementation} {GeneratedNames.For("__Create_", definition)}(global::System.IServiceProvider sp)");
        ImmutableEquatableArray<Dependency> parameters = definition.ConstructorParameters;
        if(definition.Members.Length > 0)
        {
            writer.WriteLine('{');
            writer.Indentation++;
            CreationWriter.Write(
                writer,
                definition,
                declare: true,
                i => Argument(definition, definition.Dependencies[i], i, providers),
                (i, member) => Assignment(definition, i, member, providers),
                definition.Disposal != Disposal.None);
            writer.WriteLine("return instance;");
            writer.Indentation--;
            writer.WriteLine('}');
            return;
        }

        if(parameters.Length == 0)
        {
            writer.Indentation++;
            writer.WriteLine($"=> new {implementation}();");
            writer.Indentation--;
            return;
        }

        // A parameter by reference gets a local variable, passed with "in". This needs a block body
        bool hasLocals = parameters.Any(static p => p.ByReference);
        if(hasLocals)
        {
            writer.WriteLine('{');
            writer.Indentation++;
            for(int i = 0; i < parameters.Length; i++)
            {
                if(parameters[i].ByReference)
                {
                    writer.WriteLine($"{TypeNames.CSharp(parameters[i].Type)} arg{i} = {Argument(definition, parameters[i], i, providers)};");
                }
            }

            writer.WriteLine($"return new {implementation}(");
        }
        else
        {
            writer.Indentation++;
            writer.WriteLine($"=> new {implementation}(");
        }

        writer.Indentation++;
        for(int i = 0; i < parameters.Length; i++)
        {
            if(parameters[i].ByReference)
            {
                writer.WriteLine($"in arg{i}{(i < parameters.Length - 1 ? "," : ");")}");
            }
            else
            {
                writer.WriteLine($"{Argument(definition, parameters[i], i, providers)}{(i < parameters.Length - 1 ? "," : ");")}");
            }
        }

        writer.Indentation -= 2;
        if(hasLocals)
        {
            writer.WriteLine('}');
        }
    }

    /// <summary>
    /// The assignment of an injected field or property. A member that keeps its initializer is assigned only when the
    /// provider returns a service (ServiceCollection.Output.spec, "Dependency resolution"). A collection and a wrapper that
    /// the site creates itself are always assigned.
    /// </summary>
    private static string Assignment(ClosedDefinition owner, int index, string member, Dictionary<(TypeRef Type, string Key), List<int>> providers)
    {
        Dependency dependency = owner.Dependencies[index];
        if(dependency.Optional != OptionalMode.KeepInitializer || Collection(dependency.Type) is not null)
        {
            return $"instance.{member} = {Argument(owner, dependency, index, providers)};";
        }

        if(WrapperArgument(owner, dependency, providers) is { } wrapper)
        {
            return $"instance.{member} = {wrapper};";
        }

        KeyRef? key = dependency.KeyIn(owner);
        return key is null && !dependency.IsValueType
            ? $"if({Provider}.GetService<{TypeNames.CSharp(dependency.Type)}>(sp) is {{ }} v{index}) instance.{member} = v{index};"
            : ContainerEmitter.KeepInitializer(ContainerEmitter.FallbackGet("sp", dependency, key), dependency, index, member);
    }

    /// <summary>The expression that resolves the argument of <paramref name="parameter"/> (ServiceCollection.Output.spec, "Dependency resolution").</summary>
    /// <param name="owner">The closed definition that owns the parameter, for <c>InheritKey</c> and <c>[ServiceKey]</c>.</param>
    /// <param name="parameter">The parameter.</param>
    /// <param name="index">The index of the parameter, for the name of a pattern variable.</param>
    /// <param name="providers">The providers of each service type in the assembly view.</param>
    private static string Argument(ClosedDefinition owner, Dependency parameter, int index, Dictionary<(TypeRef Type, string Key), List<int>> providers)
    {
        string type = TypeNames.CSharp(parameter.Type);
        if(parameter.KeyMode == SiteKeyMode.ServiceKey)
        {
            return owner.Key is null ? $"({type})({ContainerEmitter.DefaultValue(parameter)})" : ContainerEmitter.ServiceKeyArgument(owner.Key);
        }

        if(Collection(parameter.Type) is { } collection)
        {
            return CollectionArgument(parameter.Type, collection, parameter.KeyIn(owner));
        }

        if(WrapperArgument(owner, parameter, providers) is { } wrapper)
        {
            return wrapper;
        }

        // A keyed dependency uses the keyed methods of the provider, as the fallback provider of a container does
        if(parameter.KeyIn(owner) is { } key)
        {
            return ContainerEmitter.FallbackArgument("sp", parameter, index, key);
        }

        if(BuiltInServices.IsServiceProvider(parameter.Type))
        {
            return "sp";
        }

        switch(parameter.Optional)
        {
            case OptionalMode.Default when parameter.IsValueType:
                // The generic GetService<X>() throws for a value type without a registration. A pattern cannot use
                // Nullable<U>, so the test is on U and the conversion to U? is implicit
                string test = parameter.Type is NamedTypeRef { MetadataName: "System.Nullable`1", Arguments: [TypeRef underlying] } ? TypeNames.CSharp(underlying) : type;
                return $"sp.GetService(typeof({type})) is {test} v{index} ? ({type})v{index} : ({type})({ContainerEmitter.DefaultValue(parameter)})";
            case OptionalMode.Default when ContainerEmitter.IsNullDefault(parameter):
                // The parameter type can be non-nullable (IRepo repo = null!), so the result has "!"
                return $"{Provider}.GetService<{type}>(sp)!";
            case OptionalMode.Default:
                return $"{Provider}.GetService<{type}>(sp) ?? ({type})({ContainerEmitter.DefaultValue(parameter)})";
            default:
                return $"{Provider}.GetRequiredService<{type}>(sp)";
        }
    }

    private static void WriteGet(SourceWriter writer, ClosedDefinition definition)
    {
        string implementation = TypeNames.CSharp(definition.Implementation);
        writer.WriteLine();
        writer.WriteLine($"private static {implementation} {GeneratedNames.For("__Get_", definition)}(global::System.IServiceProvider sp)");
        writer.Indentation++;
        writer.WriteLine($"=> {KeyedProvider}.GetRequiredKeyedService<{Holder}<{implementation}>>(sp, {GeneratedNames.For("__k_", definition)}).Value;");
        writer.Indentation--;
    }
}
