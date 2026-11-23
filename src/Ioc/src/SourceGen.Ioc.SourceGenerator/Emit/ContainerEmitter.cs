using SourceGen.Ioc.Analysis;
using SourceGen.Ioc.Model;
using SourceGen.Ioc.Parse;

namespace SourceGen.Ioc.Emit;

/// <summary>Generates the container (Container.Output.spec) without a fallback provider.</summary>
internal static class ContainerEmitter
{
    private const string Lock = "global::System.Threading.Lock";
    private const string Exception = "global::System.Exception";
    private const string ExceptionList = "global::System.Collections.Generic.List<global::System.Exception>";
    private const string InvalidOperation = "global::System.InvalidOperationException";
    private const string Interlocked = "global::System.Threading.Interlocked";

    /// <summary>Generates the source of the partial container class.</summary>
    /// <param name="plan">The plan.</param>
    /// <returns>The source.</returns>
    public static string Generate(ContainerPlan plan)
    {
        ContainerModel container = plan.Container;
        string self = TypeNames.CSharp(container.Type);
        using SourceWriter writer = new();
        GeneratedFile.WriteHeader(writer);
        writer.WriteLine();
        if(container.Namespace is not null)
        {
            writer.WriteLine($"namespace {container.Namespace};");
            writer.WriteLine();
        }

        foreach(ContainingType outer in container.ContainingTypes)
        {
            writer.WriteLine($"partial {outer.Keyword} {outer.Name}");
            writer.WriteLine('{');
            writer.Indentation++;
        }

        writer.WriteLine($"partial class {container.Name} : global::SourceGen.Ioc.IIocContainer, global::Microsoft.Extensions.DependencyInjection.IServiceScope");
        writer.WriteLine('{');
        writer.Indentation++;

        WriteState(writer, plan, self);
        WriteConstructors(writer, plan, self);
        for(int i = 0; i < plan.Definitions.Length; i++)
        {
            WriteResolver(writer, plan, i, self);
        }

        WriteLookup(writer, plan, self);
        WriteServiceProvider(writer, self);
        WriteDisposal(writer, plan, isAsync: false);
        WriteDisposal(writer, plan, isAsync: true);
        WriteHelpers(writer, container);

        writer.Indentation--;
        writer.WriteLine('}');
        foreach(ContainingType _ in container.ContainingTypes)
        {
            writer.Indentation--;
            writer.WriteLine('}');
        }

        return writer.ToString();
    }

    private static string Field(ClosedDefinition definition) => GeneratedNames.For("__f_", definition);

    private static string LockField(ClosedDefinition definition) => GeneratedNames.For("__lock_", definition);

    private static string Get(ClosedDefinition definition) => GeneratedNames.For("__Get_", definition);

    private static string Create(ClosedDefinition definition) => GeneratedNames.For("__Create_", definition);

    private static void WriteState(SourceWriter writer, ContainerPlan plan, string self)
    {
        // The state that the root and all scopes share (Container.Output.spec, "Root and scope")
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("private sealed class __Singletons");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine($"public {self} Root = null!;");
        foreach(DefinitionPlan definition in plan.Definitions.Where(static d => d.Definition.Lifetime == Lifetime.Singleton))
        {
            writer.WriteLine($"public readonly {Lock} {LockField(definition.Definition)} = new {Lock}();");
            writer.WriteLine($"public {TypeNames.CSharp(definition.Definition.Implementation)}? {Field(definition.Definition)};");
        }

        writer.Indentation--;
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("private readonly __Singletons __singletons;");
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("private readonly bool __isRoot;");
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("private int __disposed;");
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"private readonly {Lock} __scopedLock = new {Lock}();");
        foreach(DefinitionPlan definition in plan.Definitions.Where(static d => d.Definition.Lifetime == Lifetime.Scoped))
        {
            writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
            writer.WriteLine($"private {TypeNames.CSharp(definition.Definition.Implementation)}? {Field(definition.Definition)};");
        }
    }

    private static void WriteConstructors(SourceWriter writer, ContainerPlan plan, string self)
    {
        string name = plan.Container.Name;
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"public {name}()");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine("__singletons = new __Singletons();");
        writer.WriteLine("__singletons.Root = this;");
        writer.WriteLine("__isRoot = true;");
        writer.Indentation--;
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"private {name}({self} root)");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine("__singletons = root.__singletons;");
        writer.Indentation--;
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"public {self} CreateScope()");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine("__ThrowIfDisposed();");
        writer.WriteLine($"return new {self}(__singletons.Root);");
        writer.Indentation--;
        writer.WriteLine('}');
    }

    private static void WriteResolver(SourceWriter writer, ContainerPlan plan, int index, string self)
    {
        ClosedDefinition definition = plan.Definitions[index].Definition;
        string implementation = TypeNames.CSharp(definition.Implementation);
        writer.WriteLine();
        switch(definition.Lifetime)
        {
            case Lifetime.Singleton:
                // The fast path is one field read and one null check, separate from the creation path
                writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
                writer.WriteLine($"private {implementation} {Get(definition)}() => __singletons.{Field(definition)} ?? {Create(definition)}();");
                writer.WriteLine();
                writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
                writer.WriteLine($"private {implementation} {Create(definition)}()");
                writer.WriteLine('{');
                writer.Indentation++;
                writer.WriteLine("__Singletons s = __singletons;");
                writer.WriteLine($"lock(s.{LockField(definition)})");
                writer.WriteLine('{');
                writer.Indentation++;
                writer.WriteLine($"if(s.{Field(definition)} is {{ }} existing)");
                writer.WriteLine('{');
                writer.WriteLine("    return existing;");
                writer.WriteLine('}');
                writer.WriteLine();
                writer.WriteLine("// The dependencies of a Singleton are resolved on the root");
                writer.WriteLine($"{self} c = s.Root;");
                WriteCreation(writer, plan, index);
                writer.WriteLine($"s.{Field(definition)} = instance;");
                writer.WriteLine("return instance;");
                writer.Indentation--;
                writer.WriteLine('}');
                writer.Indentation--;
                writer.WriteLine('}');
                break;
            case Lifetime.Scoped:
                writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
                writer.WriteLine($"private {implementation} {Get(definition)}() => {Field(definition)} ?? {Create(definition)}();");
                writer.WriteLine();
                writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
                writer.WriteLine($"private {implementation} {Create(definition)}()");
                writer.WriteLine('{');
                writer.Indentation++;
                writer.WriteLine("if(__isRoot)");
                writer.WriteLine('{');
                writer.WriteLine($"    throw new {InvalidOperation}(\"Cannot resolve scoped service '{definition.Implementation.SortName}' from the root container.\");");
                writer.WriteLine('}');
                writer.WriteLine();
                writer.WriteLine("lock(__scopedLock)");
                writer.WriteLine('{');
                writer.Indentation++;
                writer.WriteLine($"if({Field(definition)} is {{ }} existing)");
                writer.WriteLine('{');
                writer.WriteLine("    return existing;");
                writer.WriteLine('}');
                writer.WriteLine();
                writer.WriteLine($"{self} c = this;");
                WriteCreation(writer, plan, index);
                writer.WriteLine($"{Field(definition)} = instance;");
                writer.WriteLine("return instance;");
                writer.Indentation--;
                writer.WriteLine('}');
                writer.Indentation--;
                writer.WriteLine('}');
                break;
            default:
                writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
                writer.WriteLine($"private {implementation} {Get(definition)}()");
                writer.WriteLine('{');
                writer.Indentation++;
                writer.WriteLine($"{self} c = this;");
                WriteCreation(writer, plan, index);
                writer.WriteLine("return instance;");
                writer.Indentation--;
                writer.WriteLine('}');
                break;
        }
    }

    /// <summary>Writes the local <c>instance</c>: <c>new</c> with each argument resolved on <c>c</c>.</summary>
    private static void WriteCreation(SourceWriter writer, ContainerPlan plan, int index)
    {
        DefinitionPlan definition = plan.Definitions[index];
        string implementation = TypeNames.CSharp(definition.Definition.Implementation);
        ImmutableEquatableArray<ConstructorParameter> parameters = definition.Definition.ConstructorParameters;
        for(int i = 0; i < parameters.Length; i++)
        {
            if(parameters[i].ByReference)
            {
                writer.WriteLine($"{TypeNames.CSharp(parameters[i].Type)} arg{i} = {Argument(plan, definition, i)};");
            }
        }

        if(parameters.Length == 0)
        {
            writer.WriteLine($"{implementation} instance = new {implementation}();");
            return;
        }

        writer.WriteLine($"{implementation} instance = new {implementation}(");
        writer.Indentation++;
        for(int i = 0; i < parameters.Length; i++)
        {
            if(parameters[i].ByReference)
            {
                writer.WriteLine($"in arg{i}{(i < parameters.Length - 1 ? "," : ");")}");
            }
            else
            {
                writer.WriteLine($"{Argument(plan, definition, i)}{(i < parameters.Length - 1 ? "," : ");")}");
            }
        }

        writer.Indentation--;
    }

    /// <summary>The expression of one argument, with a static cast to the parameter type.</summary>
    private static string Argument(ContainerPlan plan, DefinitionPlan definition, int index)
    {
        string type = TypeNames.CSharp(definition.Definition.ConstructorParameters[index].Type);
        ArgumentPlan argument = definition.Arguments[index];
        return argument.Kind switch
        {
            ArgumentKind.Self => $"({type})c",
            ArgumentKind.Root => $"({type})c.__singletons.Root",
            _ => $"({type})c.{Get(plan.Definitions[argument.Target].Definition)}()",
        };
    }

    private static void WriteLookup(SourceWriter writer, ContainerPlan plan, string self)
    {
        string function = $"global::System.Func<{self}, object>";
        string dictionary = $"global::System.Collections.Generic.Dictionary<global::System.Type, {function}>";
        writer.WriteLine();
        writer.WriteLine("// The lookup table (Container.Output.spec, \"Lookup by Type\")");
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"private static readonly global::System.Collections.Frozen.FrozenDictionary<global::System.Type, {function}> __lookup =");
        writer.Indentation++;
        writer.WriteLine($"global::System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(new {dictionary}");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine("{ typeof(global::System.IServiceProvider), static c => c },");
        writer.WriteLine("{ typeof(global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory), static c => c.__singletons.Root },");
        writer.WriteLine("{ typeof(global::Microsoft.Extensions.DependencyInjection.IServiceProviderIsService), static c => c.__singletons.Root },");
        writer.WriteLine("{ typeof(global::Microsoft.Extensions.DependencyInjection.IServiceProviderIsKeyedService), static c => c.__singletons.Root },");
        if(!plan.Lookup.Any(entry => entry.ServiceType.Equals(plan.Container.Type)))
        {
            writer.WriteLine($"{{ typeof({self}), static c => c }},");
        }

        foreach(LookupEntry entry in plan.Lookup)
        {
            string service = TypeNames.CSharp(entry.ServiceType);
            if(entry.Candidates.Length == 1)
            {
                writer.WriteLine($"{{ typeof({service}), static c => ({service})c.{Get(plan.Definitions[entry.Candidates[0]].Definition)}() }},");
            }
            else
            {
                string names = string.Join(", ", entry.Candidates.Select(i => plan.Definitions[i].Definition.Implementation.SortName));
                writer.WriteLine($"{{ typeof({service}), static c => throw new {InvalidOperation}(\"More than one registration provides '{TypeNames.Display(entry.ServiceType)}': {names}.\") }},");
            }

            writer.WriteLine($"{{ typeof(global::System.Collections.Generic.IEnumerable<{service}>), static c => new {service}[] {{ {string.Join(", ", entry.Candidates.Select(i => $"({service})c.{Get(plan.Definitions[i].Definition)}()"))} }} }},");
        }

        writer.Indentation--;
        writer.WriteLine("});");
        writer.Indentation--;
    }

    private static void WriteServiceProvider(SourceWriter writer, string self)
    {
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public object? GetService(global::System.Type serviceType)");
        writer.WriteLine('{');
        writer.WriteLine("    __ThrowIfDisposed();");
        writer.WriteLine($"    return __lookup.TryGetValue(serviceType, out global::System.Func<{self}, object>? factory) ? factory(this) : null;");
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public object GetRequiredService(global::System.Type serviceType)");
        writer.WriteLine($"    => GetService(serviceType) ?? throw new {InvalidOperation}(\"No service for type '\" + serviceType + \"' has been registered.\");");
        writer.WriteLine();
        writer.WriteLine("// Keyed registrations come in a later slice. Until then a key finds nothing");
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public object? GetKeyedService(global::System.Type serviceType, object? serviceKey)");
        writer.WriteLine('{');
        writer.WriteLine("    __ThrowIfDisposed();");
        writer.WriteLine("    return serviceKey is null ? GetService(serviceType) : null;");
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public object GetRequiredKeyedService(global::System.Type serviceType, object? serviceKey)");
        writer.WriteLine($"    => GetKeyedService(serviceType, serviceKey) ?? throw new {InvalidOperation}(\"No service for type '\" + serviceType + \"' has been registered.\");");
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public bool IsService(global::System.Type serviceType) => __lookup.ContainsKey(serviceType);");
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public bool IsKeyedService(global::System.Type serviceType, object? serviceKey) => serviceKey is null && IsService(serviceType);");
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public T? GetService<T>() where T : class => (T?)GetService(typeof(T));");
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public T GetRequiredService<T>() where T : notnull => (T)GetRequiredService(typeof(T));");
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public global::System.Collections.Generic.IEnumerable<T> GetServices<T>()");
        writer.WriteLine("    => (global::System.Collections.Generic.IEnumerable<T>?)GetService(typeof(global::System.Collections.Generic.IEnumerable<T>)) ?? global::System.Array.Empty<T>();");
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public T? GetKeyedService<T>(object? serviceKey) where T : class => (T?)GetKeyedService(typeof(T), serviceKey);");
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public T GetRequiredKeyedService<T>(object? serviceKey) where T : notnull => (T)GetRequiredKeyedService(typeof(T), serviceKey);");
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public global::System.Collections.Generic.IEnumerable<T> GetKeyedServices<T>(object? serviceKey)");
        writer.WriteLine("    => (global::System.Collections.Generic.IEnumerable<T>?)GetKeyedService(typeof(global::System.Collections.Generic.IEnumerable<T>), serviceKey) ?? global::System.Array.Empty<T>();");
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("global::Microsoft.Extensions.DependencyInjection.IServiceScope global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory.CreateScope() => CreateScope();");
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("global::System.IServiceProvider global::Microsoft.Extensions.DependencyInjection.IServiceScope.ServiceProvider => this;");
    }

    /// <summary>
    /// Writes <c>Dispose</c> or <c>DisposeAsync</c>: the instances of one lifetime in the reverse topological order, each
    /// one time, with the exceptions collected (Container.Output.spec, "Disposal").
    /// </summary>
    private static void WriteDisposal(SourceWriter writer, ContainerPlan plan, bool isAsync)
    {
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine(isAsync ? "public async global::System.Threading.Tasks.ValueTask DisposeAsync()" : "public void Dispose()");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine($"if({Interlocked}.Exchange(ref __disposed, 1) != 0)");
        writer.WriteLine('{');
        writer.WriteLine("    return;");
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine($"{ExceptionList}? errors = null;");
        writer.WriteLine("if(__isRoot)");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine("__Singletons s = __singletons;");
        WriteDisposals(writer, plan, Lifetime.Singleton, "s.", isAsync);
        writer.Indentation--;
        writer.WriteLine('}');
        writer.WriteLine("else");
        writer.WriteLine('{');
        writer.Indentation++;
        WriteDisposals(writer, plan, Lifetime.Scoped, string.Empty, isAsync);
        writer.Indentation--;
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine("__ThrowIfAny(errors);");
        writer.Indentation--;
        writer.WriteLine('}');
    }

    private static void WriteDisposals(SourceWriter writer, ContainerPlan plan, Lifetime lifetime, string owner, bool isAsync)
    {
        int variable = 0;
        foreach(int index in plan.TopologicalOrder.Reverse())
        {
            ClosedDefinition definition = plan.Definitions[index].Definition;
            if(definition.Lifetime != lifetime || definition.Disposal == Disposal.None)
            {
                continue;
            }

            string name = $"i{variable++}";
            writer.WriteLine($"if({Interlocked}.Exchange(ref {owner}{Field(definition)}, null) is {{ }} {name})");
            writer.WriteLine('{');
            writer.Indentation++;
            if(!isAsync && definition.Disposal == Disposal.Async)
            {
                // MS.DI does the same for an object that implements only IAsyncDisposable
                writer.WriteLine($"(errors ??= new {ExceptionList}()).Add(new {InvalidOperation}(\"'{TypeNames.Display(definition.Implementation)}' type only implements IAsyncDisposable. Use DisposeAsync to dispose the container.\"));");
            }
            else
            {
                writer.WriteLine("try");
                writer.WriteLine('{');
                if(isAsync && (definition.Disposal & Disposal.Async) != 0)
                {
                    writer.WriteLine($"    await ((global::System.IAsyncDisposable){name}).DisposeAsync().ConfigureAwait(false);");
                }
                else
                {
                    writer.WriteLine($"    ((global::System.IDisposable){name}).Dispose();");
                }

                writer.WriteLine('}');
                writer.WriteLine($"catch({Exception} e)");
                writer.WriteLine('{');
                writer.WriteLine($"    (errors ??= new {ExceptionList}()).Add(e);");
                writer.WriteLine('}');
            }

            writer.Indentation--;
            writer.WriteLine('}');
        }
    }

    private static void WriteHelpers(SourceWriter writer, ContainerModel container)
    {
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("private void __ThrowIfDisposed()");
        writer.WriteLine('{');
        writer.WriteLine("    if(global::System.Threading.Volatile.Read(ref __disposed) != 0)");
        writer.WriteLine("    {");
        writer.WriteLine($"        throw new global::System.ObjectDisposedException(\"{container.Name}\");");
        writer.WriteLine("    }");
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"private static void __ThrowIfAny({ExceptionList}? errors)");
        writer.WriteLine('{');
        writer.WriteLine("    if(errors is null)");
        writer.WriteLine("    {");
        writer.WriteLine("        return;");
        writer.WriteLine("    }");
        writer.WriteLine();
        writer.WriteLine("    if(errors.Count == 1)");
        writer.WriteLine("    {");
        writer.WriteLine("        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(errors[0]);");
        writer.WriteLine("    }");
        writer.WriteLine();
        writer.WriteLine("    throw new global::System.AggregateException(errors);");
        writer.WriteLine('}');
    }
}
