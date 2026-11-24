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
    private const string Volatile = "global::System.Threading.Volatile";
    private const string Report = "global::SourceGen.Ioc.IocContainerGlobalOptions.OnDisposeException?.Invoke";

    /// <summary>The values of <c>ThreadSafeStrategy</c>.</summary>
    private const int StrategyNone = 0;
    private const int StrategyLock = 1;

    /// <summary>The flags of <c>EagerResolveOptions</c>.</summary>
    private const int EagerSingleton = 1;
    private const int EagerScoped = 2;

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

        WriteAccessors(writer, plan);
        WriteLookup(writer, plan, self);
        WriteServiceProvider(writer, self);
        WriteDisposal(writer, plan);
        WriteHelpers(writer, plan);

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
        bool locks = plan.Container.ThreadSafeStrategy == StrategyLock;

        // The state that the root and all scopes share (Container.Output.spec, "Root and scope"). Each Singleton has
        // its own lock, so that a slow constructor does not block the first creation of the other Singletons.
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("private sealed class __Singletons");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine($"public {self} Root = null!;");
        foreach(DefinitionPlan definition in plan.Definitions.Where(static d => d.Definition.Lifetime == Lifetime.Singleton))
        {
            if(locks)
            {
                writer.WriteLine($"public readonly {Lock} {LockField(definition.Definition)} = new {Lock}();");
            }

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
        if(locks)
        {
            writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
            writer.WriteLine($"private readonly {Lock} __scopedLock = new {Lock}();");
        }

        foreach(DefinitionPlan definition in plan.Definitions.Where(static d => d.Definition.Lifetime == Lifetime.Scoped))
        {
            writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
            writer.WriteLine($"private {TypeNames.CSharp(definition.Definition.Implementation)}? {Field(definition.Definition)};");
        }
    }

    private static void WriteConstructors(SourceWriter writer, ContainerPlan plan, string self)
    {
        string name = plan.Container.Name;
        int eager = plan.Container.EagerResolveOptions;
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"public {name}()");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine("__singletons = new __Singletons();");
        writer.WriteLine("__singletons.Root = this;");
        writer.WriteLine("__isRoot = true;");
        if((eager & EagerSingleton) != 0)
        {
            WriteEager(writer, plan, Lifetime.Singleton);
        }

        writer.Indentation--;
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"private {name}({self} root)");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine("__singletons = root.__singletons;");
        if((eager & EagerScoped) != 0)
        {
            WriteEager(writer, plan, Lifetime.Scoped);
        }

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

    /// <summary>
    /// Creates each closed definition of <paramref name="lifetime"/> in the topological order, through the internal
    /// resolvers. On a failure, it disposes what is already created, in the reverse order, and throws the original exception again
    /// (Container.Output.spec, "Eager resolution").
    /// </summary>
    private static void WriteEager(SourceWriter writer, ContainerPlan plan, Lifetime lifetime)
    {
        ClosedDefinition[] definitions = [.. plan.TopologicalOrder
            .Select(i => plan.Definitions[i].Definition)
            .Where(d => d.Lifetime == lifetime)];
        if(definitions.Length == 0)
        {
            return;
        }

        writer.WriteLine("try");
        writer.WriteLine('{');
        writer.Indentation++;
        foreach(ClosedDefinition definition in definitions)
        {
            writer.WriteLine($"{Get(definition)}();");
        }

        writer.Indentation--;
        writer.WriteLine('}');

        // The caller gets no container, so it cannot call DisposeAsync: an async-only instance starts its disposal
        // without a wait, and each exception goes to the callback (__DisposeLost does both)
        string owner = lifetime == Lifetime.Singleton ? "__singletons." : string.Empty;
        writer.WriteLine("catch");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine($"{Interlocked}.Exchange(ref __disposed, 1);");
        int variable = 0;
        foreach(ClosedDefinition definition in Enumerable.Reverse(definitions).Where(static d => d.Disposal != Disposal.None))
        {
            string name = $"i{variable++}";
            writer.WriteLine($"if({Interlocked}.Exchange(ref {owner}{Field(definition)}, null) is {{ }} {name})");
            writer.WriteLine('{');
            writer.WriteLine($"    __DisposeLost({name});");
            writer.WriteLine('}');
        }

        writer.WriteLine();
        writer.WriteLine("throw;");
        writer.Indentation--;
        writer.WriteLine('}');
    }

    private static void WriteResolver(SourceWriter writer, ContainerPlan plan, int index, string self)
    {
        ClosedDefinition definition = plan.Definitions[index].Definition;
        string implementation = TypeNames.CSharp(definition.Implementation);
        writer.WriteLine();
        if(definition.Lifetime == Lifetime.Transient)
        {
            writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
            writer.WriteLine($"private {implementation} {Get(definition)}()");
            writer.WriteLine('{');
            writer.Indentation++;
            writer.WriteLine($"{self} c = this;");
            WriteCreation(writer, plan, index, declare: true);
            writer.WriteLine("return instance;");
            writer.Indentation--;
            writer.WriteLine('}');
            return;
        }

        bool singleton = definition.Lifetime == Lifetime.Singleton;
        string owner = singleton ? "s." : string.Empty;
        string field = $"{owner}{Field(definition)}";
        int strategy = plan.Container.ThreadSafeStrategy;

        // The fast path is one field read and one null check, separate from the creation path
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"private {implementation} {Get(definition)}() => {(singleton ? "__singletons." : string.Empty)}{Field(definition)} ?? {Create(definition)}();");
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"private {implementation} {Create(definition)}()");
        writer.WriteLine('{');
        writer.Indentation++;
        if(singleton)
        {
            writer.WriteLine("__Singletons s = __singletons;");
        }
        else
        {
            writer.WriteLine("if(__isRoot)");
            writer.WriteLine('{');
            writer.WriteLine($"    throw new {InvalidOperation}(\"Cannot resolve scoped service '{definition.Implementation.SortName}' from the root container.\");");
            writer.WriteLine('}');
            writer.WriteLine();
        }

        if(strategy == StrategyLock)
        {
            writer.WriteLine($"{implementation} instance;");
            writer.WriteLine(singleton ? $"lock(s.{LockField(definition)})" : "lock(__scopedLock)");
            writer.WriteLine('{');
            writer.Indentation++;
            WriteExisting(writer, field);
            WriteContext(writer, singleton, self);
            WriteCreation(writer, plan, index, declare: false);
            WriteStore(writer, field);
            writer.Indentation--;
            writer.WriteLine('}');
        }
        else if(strategy == StrategyNone)
        {
            // No guarantee: concurrent first resolution can create more than one instance
            WriteExisting(writer, field);
            WriteContext(writer, singleton, self);
            WriteCreation(writer, plan, index, declare: true);
            WriteStore(writer, field);
        }
        else
        {
            // CompareExchange keeps the first instance. A losing instance is disposed at once
            WriteContext(writer, singleton, self);
            WriteCreation(writer, plan, index, declare: true);
            writer.WriteLine($"if({Interlocked}.CompareExchange(ref {field}, instance, null) is {{ }} winner)");
            writer.WriteLine('{');
            if((definition.Disposal & Disposal.Sync) != 0)
            {
                writer.WriteLine("    __DisposeLoser(instance);");
            }

            writer.WriteLine("    return winner;");
            writer.WriteLine('}');
        }

        // Check one more time after the store: a disposal that started meanwhile and this side compete for the
        // instance, so that it is disposed exactly one time (Container.Output.spec, "After disposal, and concurrency")
        writer.WriteLine();
        writer.WriteLine($"if({Volatile}.Read(ref {(singleton ? "s.Root.__disposed" : "__disposed")}) != 0)");
        writer.WriteLine('{');
        if(definition.Disposal != Disposal.None)
        {
            writer.WriteLine($"    if(global::System.Object.ReferenceEquals({Interlocked}.CompareExchange(ref {field}, null, instance), instance))");
            writer.WriteLine("    {");
            writer.WriteLine("        __DisposeLost(instance);");
            writer.WriteLine("    }");
            writer.WriteLine();
        }

        writer.WriteLine($"    throw new global::System.ObjectDisposedException(\"{plan.Container.Name}\");");
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine("return instance;");
        writer.Indentation--;
        writer.WriteLine('}');
    }

    /// <summary>
    /// Stores the instance with a full fence. The check of the disposed flag follows, and a plain store could be
    /// reordered after that read, so a concurrent disposal could miss the instance.
    /// </summary>
    private static void WriteStore(SourceWriter writer, string field)
        => writer.WriteLine($"{Interlocked}.Exchange(ref {field}, instance);");

    private static void WriteExisting(SourceWriter writer, string field)
    {
        writer.WriteLine($"if({field} is {{ }} existing)");
        writer.WriteLine('{');
        writer.WriteLine("    return existing;");
        writer.WriteLine('}');
        writer.WriteLine();
    }

    /// <summary>Writes the local <c>c</c>: the container that resolves the dependencies. A Singleton uses the root.</summary>
    private static void WriteContext(SourceWriter writer, bool singleton, string self)
        => writer.WriteLine(singleton ? $"{self} c = s.Root;" : $"{self} c = this;");

    /// <summary>Writes <c>instance = new ...</c> with each argument resolved on <c>c</c>.</summary>
    private static void WriteCreation(SourceWriter writer, ContainerPlan plan, int index, bool declare)
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

        string target = declare ? $"{implementation} instance" : "instance";
        if(parameters.Length == 0)
        {
            writer.WriteLine($"{target} = new {implementation}();");
            return;
        }

        writer.WriteLine($"{target} = new {implementation}(");
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
        ConstructorParameter parameter = definition.Definition.ConstructorParameters[index];
        string type = TypeNames.CSharp(parameter.Type);
        ArgumentPlan argument = definition.Arguments[index];
        return argument.Kind switch
        {
            ArgumentKind.Self => $"({type})c",
            ArgumentKind.Root => $"({type})c.__singletons.Root",
            ArgumentKind.Default => $"({type})({DefaultValue(parameter)})",
            _ => $"({type})c.{Get(plan.Definitions[argument.Target].Definition)}()",
        };
    }

    /// <summary>
    /// The default value of a missing optional dependency, before the cast to the parameter type. A <c>null</c> of a
    /// reference type gets <c>!</c>: the parameter type has no nullable annotation in the generated code.
    /// </summary>
    internal static string DefaultValue(ConstructorParameter parameter)
        => parameter.DefaultValue is { } value and not "default" ? value
            : parameter.IsValueType ? "default" : "default!";

    /// <summary>
    /// The implementation parts of the partial accessors. Each one calls the internal resolver directly, without the
    /// lookup table (Container.Output.spec, "Partial accessors").
    /// </summary>
    private static void WriteAccessors(SourceWriter writer, ContainerPlan plan)
    {
        foreach(AccessorPlan accessor in plan.Accessors)
        {
            PartialAccessor member = accessor.Accessor;
            writer.WriteLine();
            if(!member.HasGeneratedCode)
            {
                writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
            }

            // A keyword needs "@" as an identifier (@class)
            writer.WriteLine($"{member.Modifiers} {member.ReturnType} {(SyntaxFacts.GetKeywordKind(member.Name) == SyntaxKind.None ? string.Empty : "@")}{member.Name}{(member.IsProperty ? string.Empty : "()")}");
            writer.WriteLine('{');
            writer.Indentation++;
            if(member.IsProperty)
            {
                writer.WriteLine("get");
                writer.WriteLine('{');
                writer.Indentation++;
            }

            writer.WriteLine("__ThrowIfDisposed();");
            switch(accessor.Argument.Kind)
            {
                case ArgumentKind.Self:
                    writer.WriteLine($"return ({member.ReturnType})this;");
                    break;
                case ArgumentKind.Root:
                    writer.WriteLine($"return ({member.ReturnType})__singletons.Root;");
                    break;
                case ArgumentKind.Default:
                    writer.WriteLine("return null;");
                    break;
                default:
                    writer.WriteLine($"return ({member.ReturnType}){Get(plan.Definitions[accessor.Argument.Target].Definition)}();");
                    break;
            }

            if(member.IsProperty)
            {
                writer.Indentation--;
                writer.WriteLine('}');
            }

            writer.Indentation--;
            writer.WriteLine('}');
        }
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
    /// Writes <c>Dispose</c>, <c>DisposeAsync</c>, and <c>__DisposeInstances</c>: the instances of one lifetime in the
    /// reverse topological order, each one time, with the exceptions collected (Container.Output.spec, "Disposal").
    /// </summary>
    private static void WriteDisposal(SourceWriter writer, ContainerPlan plan)
    {
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public void Dispose()");
        writer.WriteLine('{');
        writer.WriteLine($"    if({Interlocked}.Exchange(ref __disposed, 1) == 0)");
        writer.WriteLine("    {");
        writer.WriteLine("        __ThrowIfAny(__DisposeInstances());");
        writer.WriteLine("    }");
        writer.WriteLine('}');

        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"private {ExceptionList}? __DisposeInstances()");
        writer.WriteLine('{');
        writer.Indentation++;
        WriteDisposalBody(writer, plan, isAsync: false);
        writer.WriteLine("return errors;");
        writer.Indentation--;
        writer.WriteLine('}');

        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public async global::System.Threading.Tasks.ValueTask DisposeAsync()");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine($"if({Interlocked}.Exchange(ref __disposed, 1) != 0)");
        writer.WriteLine('{');
        writer.WriteLine("    return;");
        writer.WriteLine('}');
        writer.WriteLine();
        WriteDisposalBody(writer, plan, isAsync: true);
        writer.WriteLine("__ThrowIfAny(errors);");
        writer.Indentation--;
        writer.WriteLine('}');
    }

    private static void WriteDisposalBody(SourceWriter writer, ContainerPlan plan, bool isAsync)
    {
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

            // The exchange is the right to dispose: a creation that runs at the same time competes for it
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

    private static void WriteHelpers(SourceWriter writer, ContainerPlan plan)
    {
        ContainerModel container = plan.Container;
        DefinitionPlan[] stored = [.. plan.Definitions.Where(static d => d.Definition.Lifetime != Lifetime.Transient)];

        // A scope also stops after the disposal of the root starts: the root disposes the Singletons, and a scope
        // would otherwise create one again that nothing disposes
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("private void __ThrowIfDisposed()");
        writer.WriteLine('{');
        writer.WriteLine($"    if({Volatile}.Read(ref __disposed) != 0 || {Volatile}.Read(ref __singletons.Root.__disposed) != 0)");
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

        if(stored.Any(static d => d.Definition.Disposal != Disposal.None))
        {
            WriteDisposeLost(writer);
        }

        if(container.ThreadSafeStrategy is not (StrategyNone or StrategyLock) && stored.Any(static d => (d.Definition.Disposal & Disposal.Sync) != 0))
        {
            WriteDisposeLoser(writer);
        }
    }

    /// <summary>
    /// An instance that is created while the container is disposed. The resolution must not block, and its
    /// <c>ObjectDisposedException</c> takes the place of an exception of this disposal.
    /// </summary>
    private static void WriteDisposeLost(SourceWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("private static void __DisposeLost(object instance)");
        writer.WriteLine('{');
        writer.WriteLine("    try");
        writer.WriteLine("    {");
        writer.WriteLine("        if(instance is global::System.IDisposable disposable)");
        writer.WriteLine("        {");
        writer.WriteLine("            disposable.Dispose();");
        writer.WriteLine("        }");
        writer.WriteLine("        else if(instance is global::System.IAsyncDisposable asyncDisposable)");
        writer.WriteLine("        {");
        writer.WriteLine("            global::System.Threading.Tasks.ValueTask disposal = asyncDisposable.DisposeAsync();");
        writer.WriteLine("            if(disposal.IsCompletedSuccessfully)");
        writer.WriteLine("            {");
        writer.WriteLine("                disposal.GetAwaiter().GetResult();");
        writer.WriteLine("            }");
        writer.WriteLine("            else");
        writer.WriteLine("            {");
        writer.WriteLine("                _ = disposal.AsTask().ContinueWith(");
        writer.WriteLine("                    static task =>");
        writer.WriteLine("                    {");
        writer.WriteLine("                        foreach(global::System.Exception error in task.Exception!.InnerExceptions)");
        writer.WriteLine("                        {");
        writer.WriteLine($"                            {Report}(error);");
        writer.WriteLine("                        }");
        writer.WriteLine("                    },");
        writer.WriteLine("                    global::System.Threading.CancellationToken.None,");
        writer.WriteLine("                    global::System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted | global::System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously,");
        writer.WriteLine("                    global::System.Threading.Tasks.TaskScheduler.Default);");
        writer.WriteLine("            }");
        writer.WriteLine("        }");
        writer.WriteLine("    }");
        writer.WriteLine($"    catch({Exception} e)");
        writer.WriteLine("    {");
        writer.WriteLine($"        {Report}(e);");
        writer.WriteLine("    }");
        writer.WriteLine('}');
    }

    /// <summary>A losing instance of <c>CompareExchange</c>: only a synchronous disposal, and it does not throw.</summary>
    private static void WriteDisposeLoser(SourceWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("private static void __DisposeLoser(global::System.IDisposable instance)");
        writer.WriteLine('{');
        writer.WriteLine("    try");
        writer.WriteLine("    {");
        writer.WriteLine("        instance.Dispose();");
        writer.WriteLine("    }");
        writer.WriteLine($"    catch({Exception} e)");
        writer.WriteLine("    {");
        writer.WriteLine($"        {Report}(e);");
        writer.WriteLine("    }");
        writer.WriteLine('}');
    }
}
