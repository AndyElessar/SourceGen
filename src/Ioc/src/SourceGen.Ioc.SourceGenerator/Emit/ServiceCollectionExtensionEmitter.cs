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

        WriteMethod(writer, model);
        foreach(ClosedDefinition definition in model.Definitions)
        {
            writer.WriteLine();
            WriteCreate(writer, definition);
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

        foreach(ClosedDefinition definition in model.Definitions)
        {
            WriteRegistrations(writer, definition);
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
    /// The collection types other than <c>IEnumerable&lt;T&gt;</c> that a dependency site asks for: each one is registered
    /// as one Transient service, an array of the services that MS.DI collects (ServiceCollection.Output.spec, "Wrapper
    /// types and collections"). A dependency site with <c>AnyKey</c> builds its array itself: MS.DI cannot resolve a
    /// single service with <c>AnyKey</c>.
    /// </summary>
    private static void WriteCollectionRegistrations(SourceWriter writer, ServiceCollectionExtensionModel model)
    {
        SortedDictionary<string, (TypeRef Type, TypeRef Element, KeyRef? Key)> demands = new(StringComparer.Ordinal);
        foreach(ClosedDefinition definition in model.Definitions)
        {
            foreach(Dependency dependency in definition.Dependencies.Where(static d => d.KeyMode != SiteKeyMode.ServiceKey))
            {
                KeyRef? key = dependency.KeyIn(definition);
                if(CollectionElement(dependency.Type) is { } element
                    && dependency.Type is not NamedTypeRef { MetadataName: WrapperRequest.Enumerable }
                    && key is not { IsAnyKey: true })
                {
                    demands[$"{dependency.Type.CanonicalString}|{KeyRef.CanonicalOf(key)}"] = (dependency.Type, element, key);
                }
            }
        }

        if(demands.Count > 0)
        {
            writer.WriteLine();
        }

        foreach((TypeRef type, TypeRef element, KeyRef? key) in demands.Values)
        {
            string collection = TypeNames.CSharp(type);
            string service = TypeNames.CSharp(element);
            if(key is null)
            {
                writer.WriteLine($"{Services}.AddTransient<{collection}>(services, static sp => global::System.Linq.Enumerable.ToArray({Provider}.GetServices<{service}>(sp)));");
            }
            else
            {
                writer.WriteLine($"{Services}.AddKeyedTransient<{collection}>(services, {KeyNames.Expression(key)}, static (sp, key) => global::System.Linq.Enumerable.ToArray({KeyedProvider}.GetKeyedServices<{service}>(sp, key)));");
            }
        }
    }

    /// <summary>The element of a requested collection whose element is the service itself, or <see langword="null"/>.</summary>
    private static TypeRef? CollectionElement(TypeRef type) => WrapperRequest.Parse(type) is { IsPlainCollection: true } request ? request.Core : null;

    /// <summary>
    /// The expression of a collection whose element is the service itself. It is never missing, so an optional one is
    /// resolved the same way (Analysis.Model.spec, "Multiple services").
    /// </summary>
    private static string CollectionArgument(TypeRef type, TypeRef element, KeyRef? key)
    {
        string service = TypeNames.CSharp(element);
        if(type is NamedTypeRef { MetadataName: WrapperRequest.Enumerable })
        {
            return key is null ? $"{Provider}.GetServices<{service}>(sp)" : $"{KeyedProvider}.GetKeyedServices<{service}>(sp, {KeyNames.Expression(key)})";
        }

        string collection = TypeNames.CSharp(type);
        if(key is { IsAnyKey: true })
        {
            return $"global::System.Linq.Enumerable.ToArray({KeyedProvider}.GetKeyedServices<{service}>(sp, {KeyNames.Expression(key)}))";
        }

        return key is null ? $"{Provider}.GetRequiredService<{collection}>(sp)" : $"{KeyedProvider}.GetRequiredKeyedService<{collection}>(sp, {KeyNames.Expression(key)})";
    }

    private static void WriteCreate(SourceWriter writer, ClosedDefinition definition)
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
                i => Argument(definition, definition.Dependencies[i], i),
                (i, member) => Assignment(definition, i, member),
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
                    writer.WriteLine($"{TypeNames.CSharp(parameters[i].Type)} arg{i} = {Argument(definition, parameters[i], i)};");
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
                writer.WriteLine($"{Argument(definition, parameters[i], i)}{(i < parameters.Length - 1 ? "," : ");")}");
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
    /// provider returns a service (ServiceCollection.Output.spec, "Dependency resolution").
    /// </summary>
    private static string Assignment(ClosedDefinition owner, int index, string member)
    {
        Dependency dependency = owner.Dependencies[index];
        if(dependency.Optional != OptionalMode.KeepInitializer || CollectionElement(dependency.Type) is not null)
        {
            return $"instance.{member} = {Argument(owner, dependency, index)};";
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
    private static string Argument(ClosedDefinition owner, Dependency parameter, int index)
    {
        string type = TypeNames.CSharp(parameter.Type);
        if(parameter.KeyMode == SiteKeyMode.ServiceKey)
        {
            return owner.Key is null ? $"({type})({ContainerEmitter.DefaultValue(parameter)})" : ContainerEmitter.ServiceKeyArgument(owner.Key);
        }

        if(CollectionElement(parameter.Type) is { } element)
        {
            return CollectionArgument(parameter.Type, element, parameter.KeyIn(owner));
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
