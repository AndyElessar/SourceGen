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

        string source = HasHolder(definition) ? GeneratedNames.For("__Get_", definition) : GeneratedNames.For("__Create_", definition);
        foreach(TypeRef serviceType in definition.ServiceTypes)
        {
            if(serviceType.Equals(definition.Implementation))
            {
                writer.WriteLine($"{Services}.Add{lifetime}<{implementation}>(services, static sp => {source}(sp));");
            }
            else
            {
                string service = TypeNames.CSharp(serviceType);
                writer.WriteLine($"{Services}.Add{lifetime}<{service}>(services, static sp => ({service}){source}(sp));");
            }
        }
    }

    private static void WriteCreate(SourceWriter writer, ClosedDefinition definition)
    {
        string implementation = TypeNames.CSharp(definition.Implementation);
        writer.WriteLine($"private static {implementation} {GeneratedNames.For("__Create_", definition)}(global::System.IServiceProvider sp)");
        ImmutableEquatableArray<ConstructorParameter> parameters = definition.ConstructorParameters;
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
                    writer.WriteLine($"{TypeNames.CSharp(parameters[i].Type)} arg{i} = {Argument(parameters[i])};");
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
                writer.WriteLine($"{Argument(parameters[i])}{(i < parameters.Length - 1 ? "," : ");")}");
            }
        }

        writer.Indentation -= 2;
        if(hasLocals)
        {
            writer.WriteLine('}');
        }
    }

    /// <summary>The expression that resolves the argument of <paramref name="parameter"/> (ServiceCollection.Output.spec, "Dependency resolution").</summary>
    private static string Argument(ConstructorParameter parameter)
        => BuiltInServices.IsServiceProvider(parameter.Type)
            ? "sp"
            : $"{Provider}.GetRequiredService<{TypeNames.CSharp(parameter.Type)}>(sp)";

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
