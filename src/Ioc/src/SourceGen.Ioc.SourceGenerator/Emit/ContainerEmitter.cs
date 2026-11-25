using SourceGen.Ioc.Analysis;
using SourceGen.Ioc.Model;
using SourceGen.Ioc.Parse;

namespace SourceGen.Ioc.Emit;

/// <summary>Generates the container (Container.Output.spec).</summary>
internal static class ContainerEmitter
{
    private const string ServiceProvider = "global::System.IServiceProvider";
    private const string DependencyInjection = "global::Microsoft.Extensions.DependencyInjection";
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

        foreach(CollectionPlan collection in plan.Collections)
        {
            WriteCollection(writer, plan, collection);
        }

        WriteAccessors(writer, plan);
        WriteLookup(writer, plan, self);
        WriteServiceProvider(writer, self, plan.Container.IntegrateServiceProvider);
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

    private static string All(CollectionPlan collection) => GeneratedNames.For("__All_", collection);

    /// <summary>
    /// The resolver of a collection: an array of the candidates in their ordering, merged with the services of the fallback
    /// provider, which come first. A collection without a merge and without a Transient element is cached
    /// (Container.Output.spec, "Collections and dictionaries"). Concurrent first resolutions keep one array.
    /// </summary>
    private static void WriteCollection(SourceWriter writer, ContainerPlan plan, CollectionPlan collection)
    {
        string element = TypeNames.CSharp(collection.Element);
        string elements = Elements(plan, collection.Candidates, element, "");
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        if(plan.Container.IntegrateServiceProvider)
        {
            if(collection.AnyKey)
            {
                writer.WriteLine($"private {element}[] {All(collection)}() => __MergeKeyed(__Fallback(), {DependencyInjection}.KeyedService.AnyKey, {elements});");
            }
            else if(collection.Key is { } key)
            {
                writer.WriteLine($"private {element}[] {All(collection)}() => __MergeKeyed(__Fallback(), {KeyNames.Expression(key)}, {elements});");
            }
            else
            {
                writer.WriteLine($"private {element}[] {All(collection)}() => __Merge(__Fallback(), {elements});");
            }

            return;
        }

        if(collection.Cache == CollectionCache.None)
        {
            writer.WriteLine($"private {element}[] {All(collection)}() => {elements};");
            return;
        }

        string field = $"{(collection.Cache == CollectionCache.Root ? "__singletons." : string.Empty)}{GeneratedNames.For("__all_", collection)}";
        writer.WriteLine($"private {element}[] {All(collection)}() => {field} ?? {GeneratedNames.For("__CreateAll_", collection)}();");
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"private {element}[] {GeneratedNames.For("__CreateAll_", collection)}()");
        writer.WriteLine('{');
        writer.WriteLine($"    {element}[] all = {elements};");
        writer.WriteLine($"    return {Interlocked}.CompareExchange(ref {field}, all, null) ?? all;");
        writer.WriteLine('}');
    }

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
        if(plan.Container.IntegrateServiceProvider)
        {
            writer.WriteLine($"public {ServiceProvider} Fallback = null!;");
            writer.WriteLine("public bool OwnsFallback;");
        }

        foreach(DefinitionPlan definition in plan.Definitions.Where(static d => d.Definition.Lifetime == Lifetime.Singleton))
        {
            if(locks)
            {
                writer.WriteLine($"public readonly {Lock} {LockField(definition.Definition)} = new {Lock}();");
            }

            writer.WriteLine($"public {TypeNames.CSharp(definition.Definition.Implementation)}? {Field(definition.Definition)};");
        }

        foreach(CollectionPlan collection in plan.Collections.Where(static c => c.Cache == CollectionCache.Root))
        {
            writer.WriteLine($"public {TypeNames.CSharp(collection.Element)}[]? {GeneratedNames.For("__all_", collection)};");
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
        if(plan.Container.IntegrateServiceProvider)
        {
            // The fallback scope of a scope, created at its first use (Container.Output.spec, "Which provider resolves")
            writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
            writer.WriteLine($"private {DependencyInjection}.IServiceScope? __fallbackScope;");
        }
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

        foreach(CollectionPlan collection in plan.Collections.Where(static c => c.Cache == CollectionCache.Scope))
        {
            writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
            writer.WriteLine($"private {TypeNames.CSharp(collection.Element)}[]? {GeneratedNames.For("__all_", collection)};");
        }
    }

    private static void WriteConstructors(SourceWriter writer, ContainerPlan plan, string self)
    {
        string name = plan.Container.Name;
        int eager = plan.Container.EagerResolveOptions;
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        if(plan.Container.IntegrateServiceProvider)
        {
            // Only the constructor with the fallback provider: each missing dependency goes to it (Container.Output.spec,
            // "Constructor"). The private one lets ServiceProviderFactory give the root the provider that it creates.
            writer.WriteLine($"public {name}({ServiceProvider} fallbackProvider)");
            writer.WriteLine("    : this(fallbackProvider, false)");
            writer.WriteLine('{');
            writer.WriteLine('}');
            writer.WriteLine();
            writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
            writer.WriteLine($"private {name}({ServiceProvider} fallbackProvider, bool ownsFallback)");
        }
        else
        {
            writer.WriteLine($"public {name}()");
        }

        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine("__singletons = new __Singletons();");
        writer.WriteLine("__singletons.Root = this;");
        if(plan.Container.IntegrateServiceProvider)
        {
            writer.WriteLine("__singletons.Fallback = fallbackProvider ?? throw new global::System.ArgumentNullException(nameof(fallbackProvider));");
            writer.WriteLine("__singletons.OwnsFallback = ownsFallback;");
        }

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
            writer.WriteLine($"    throw new {InvalidOperation}({SymbolDisplay.FormatLiteral($"Cannot resolve scoped service '{KeyNames.Definition(definition)}' from the root container.", quote: true)});");
            writer.WriteLine('}');
            writer.WriteLine();
        }

        if(strategy == StrategyLock)
        {
            writer.WriteLine($"{implementation} instance;");
            if(singleton)
            {
                writer.WriteLine($"lock(s.{LockField(definition)})");
            }
            else
            {
                writer.WriteLine("lock(__scopedLock)");
            }

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
        => writer.WriteLine($"{self} c = {(singleton ? "s.Root" : "this")};");

    /// <summary>
    /// Writes <c>instance = new ...</c> and the injection steps with each argument resolved on <c>c</c>. A Transient has
    /// no failure handling: a disposable Transient is an error of the container (Container.Output.spec, "Failure during
    /// creation").
    /// </summary>
    private static void WriteCreation(SourceWriter writer, ContainerPlan plan, int index, bool declare)
    {
        DefinitionPlan definition = plan.Definitions[index];
        ClosedDefinition closed = definition.Definition;
        CreationWriter.Write(
            writer,
            closed,
            declare,
            i => Argument(plan, definition, i),
            (i, member) => Assignment(plan, definition, i, member),
            closed.Lifetime != Lifetime.Transient && closed.Disposal != Disposal.None);
    }

    /// <summary>
    /// The assignment of an injected field or property. A member that keeps its initializer is assigned only when a
    /// service is found (Semantics.spec, "Dependency resolution").
    /// </summary>
    private static string? Assignment(ContainerPlan plan, DefinitionPlan definition, int index, string member)
    {
        Dependency dependency = definition.Definition.Dependencies[index];
        ArgumentPlan argument = definition.Arguments[index];
        if(dependency.Optional != OptionalMode.KeepInitializer || argument.Kind is not (ArgumentKind.Default or ArgumentKind.Fallback))
        {
            return $"instance.{member} = {Argument(plan, definition, index)};";
        }

        return argument.Kind == ArgumentKind.Fallback
            ? KeepInitializer(FallbackGet("c.__Fallback()", dependency, argument.Key), dependency, index, member)
            : null;
    }

    /// <summary>Assigns a service that a provider returns, and keeps the initializer when it returns <see langword="null"/>.</summary>
    /// <param name="get">The expression that returns the service as <see cref="object"/>.</param>
    /// <param name="dependency">The member.</param>
    /// <param name="index">The index of the dependency, for the name of a pattern variable.</param>
    /// <param name="member">The name of the member.</param>
    internal static string KeepInitializer(string get, Dependency dependency, int index, string member)
        => $"if({get} is {PatternType(dependency)} v{index}) instance.{member} = ({TypeNames.CSharp(dependency.Type)})v{index};";

    /// <summary>The expression of one argument, with a static cast to the parameter type.</summary>
    private static string Argument(ContainerPlan plan, DefinitionPlan definition, int index)
    {
        Dependency parameter = definition.Definition.Dependencies[index];
        string type = TypeNames.CSharp(parameter.Type);
        ArgumentPlan argument = definition.Arguments[index];
        return argument.Kind switch
        {
            ArgumentKind.Self => $"({type})c",
            ArgumentKind.Root => $"({type})c.__singletons.Root",
            ArgumentKind.Default => $"({type})({DefaultValue(parameter)})",
            ArgumentKind.Fallback => FallbackArgument("c.__Fallback()", parameter, index, argument.Key),
            ArgumentKind.ServiceKey => ServiceKeyArgument(definition.Definition.Key!),
            ArgumentKind.Collection => $"({type})c.{All(plan.Collections[argument.Target])}()",
            _ => $"({type})c.{Get(plan.Definitions[argument.Target].Definition)}()",
        };
    }

    /// <summary>
    /// The expression that resolves a missing dependency from the fallback provider (Container.Output.spec, "Behavior for
    /// each request"). A required one throws when the result is <see langword="null"/>. An optional one uses its default
    /// value. A value type is unboxed: a pattern cannot use <c>Nullable&lt;U&gt;</c>, so the test is on <c>U</c>.
    /// </summary>
    /// <param name="provider">The expression of the fallback provider.</param>
    /// <param name="parameter">The dependency.</param>
    /// <param name="index">The index of the dependency, for the name of a pattern variable.</param>
    /// <param name="key">The key that the dependency asks for, or <see langword="null"/>.</param>
    internal static string FallbackArgument(string provider, Dependency parameter, int index, KeyRef? key)
    {
        string type = TypeNames.CSharp(parameter.Type);
        string keyed = $"{DependencyInjection}.ServiceProviderKeyedServiceExtensions";
        if(parameter.Optional == OptionalMode.Required)
        {
            return key is null
                ? $"({type}){DependencyInjection}.ServiceProviderServiceExtensions.GetRequiredService({provider}, typeof({type}))"
                : $"({type}){keyed}.GetRequiredKeyedService({provider}, typeof({type}), {KeyNames.Expression(key)})";
        }

        string get = FallbackGet(provider, parameter, key);
        if(!parameter.IsValueType)
        {
            if(parameter.Optional == OptionalMode.Nullable)
            {
                return $"({type}?){get}";
            }

            // The parameter type can be non-nullable (IRepo repo = null!), so a null default has "!"
            return IsNullDefault(parameter)
                ? $"({type}){get}!"
                : $"({type}?){get} ?? ({type})({DefaultValue(parameter)})";
        }

        return $"{get} is {PatternType(parameter)} v{index} ? ({type})v{index} : ({type})({DefaultValue(parameter)})";
    }

    /// <summary>The expression that asks a provider for an optional dependency, as <see cref="object"/>.</summary>
    /// <param name="provider">The expression of the provider.</param>
    /// <param name="dependency">The dependency.</param>
    /// <param name="key">The key that the dependency asks for, or <see langword="null"/>.</param>
    internal static string FallbackGet(string provider, Dependency dependency, KeyRef? key)
        => key is null
            ? $"{provider}.GetService(typeof({TypeNames.CSharp(dependency.Type)}))"
            : $"{DependencyInjection}.ServiceProviderKeyedServiceExtensions.GetKeyedService({provider}, typeof({TypeNames.CSharp(dependency.Type)}), {KeyNames.Expression(key)})";

    /// <summary>Whether an optional reference type has <see langword="null"/> as its default value: it needs no <c>??</c>.</summary>
    internal static bool IsNullDefault(Dependency dependency)
        => !dependency.IsValueType && dependency.Optional == OptionalMode.Default && dependency.DefaultValue is null or "default";

    /// <summary>The type that a pattern tests: a pattern cannot use <c>Nullable&lt;U&gt;</c>, so the test is on <c>U</c>.</summary>
    internal static string PatternType(Dependency dependency)
        => TypeNames.CSharp(dependency.Type is NamedTypeRef { MetadataName: "System.Nullable`1", Arguments: [TypeRef underlying] } ? underlying : dependency.Type);

    /// <summary>
    /// The argument of a <c>[ServiceKey]</c> parameter: the key expression of the closed definition, without a cast. When
    /// the parameter type is not compatible with the key, the compiler reports it (Annotations.spec, "Key specification").
    /// </summary>
    internal static string ServiceKeyArgument(KeyRef key) => KeyNames.Expression(key);

    /// <summary>
    /// The default value of a missing optional dependency, before the cast to the parameter type. A <c>null</c> of a
    /// reference type gets <c>!</c>: the parameter type has no nullable annotation in the generated code.
    /// </summary>
    internal static string DefaultValue(Dependency parameter)
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
                case ArgumentKind.Collection:
                    writer.WriteLine($"return ({member.ReturnType}){All(plan.Collections[accessor.Argument.Target])}();");
                    break;
                case ArgumentKind.Fallback:
                    Dependency dependency = new(member.Type, member.Name, false, member.IsOptional ? OptionalMode.Nullable : OptionalMode.Required);
                    writer.WriteLine($"return {FallbackArgument("__Fallback()", dependency, 0, accessor.Argument.Key)};");
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

    /// <summary>
    /// The lookup tables (Container.Output.spec, "Lookup by Type"): <c>__lookup</c> without a key, <c>__keyedLookup</c>
    /// by type and key, and <c>__collections</c>, which gives the collection of a known service type for a key that has no
    /// registration. The keyed table is built when the container type is initialized: it reads each symbol key one time,
    /// and two keys that are equal at run time on the same type throw. Each collection type of a collection resolves
    /// through the resolver of that collection.
    /// </summary>
    private static void WriteLookup(SourceWriter writer, ContainerPlan plan, string self)
    {
        bool fallback = plan.Container.IntegrateServiceProvider;
        string function = $"global::System.Func<{self}, object>";
        string keyedFunction = $"global::System.Func<{self}, object, object>";
        string typeAndKey = "(global::System.Type, object)";
        LookupEntry[] keyed = [.. plan.Lookup.Where(static e => e.Key is not null)];
        CollectionPlan[] keyedCollections = [.. plan.Collections.Where(static c => c.Key is not null)];
        CollectionPlan[] demandOnly = [.. keyedCollections.Where(c => !keyed.Any(e => e.ServiceType.Equals(c.Element) && e.Key!.Equals(c.Key)))];

        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"private static readonly global::System.Collections.Frozen.FrozenDictionary<global::System.Type, {function}> __lookup =");
        writer.Indentation++;
        writer.WriteLine($"global::System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(new global::System.Collections.Generic.Dictionary<global::System.Type, {function}>");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine("{ typeof(global::System.IServiceProvider), static c => c },");
        writer.WriteLine("{ typeof(global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory), static c => c.__singletons.Root },");
        writer.WriteLine("{ typeof(global::Microsoft.Extensions.DependencyInjection.IServiceProviderIsService), static c => c.__singletons.Root },");
        writer.WriteLine("{ typeof(global::Microsoft.Extensions.DependencyInjection.IServiceProviderIsKeyedService), static c => c.__singletons.Root },");
        if(!plan.Lookup.Any(entry => entry.Key is null && entry.ServiceType.Equals(plan.Container.Type)))
        {
            writer.WriteLine($"{{ typeof({self}), static c => c }},");
        }

        // By service type: the service, then its collection types. Only the registrations without a key
        foreach(CollectionPlan collection in plan.Collections.Where(static c => c.Key is null && !c.AnyKey))
        {
            string service = TypeNames.CSharp(collection.Element);
            if(plan.Lookup.FirstOrDefault(e => e.Key is null && e.ServiceType.Equals(collection.Element)) is { } entry)
            {
                writer.WriteLine($"{{ typeof({service}), static c => {Single(plan, entry, service)} }},");
            }

            foreach(string type in CollectionTypes(collection))
            {
                writer.WriteLine($"{{ typeof({type}), static c => c.{All(collection)}() }},");
            }
        }

        writer.Indentation--;
        writer.WriteLine("});");
        writer.Indentation--;

        // A known collection type with a key that has no registration: an empty collection, merged with the fallback provider
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"private static readonly global::System.Collections.Frozen.FrozenDictionary<global::System.Type, {keyedFunction}> __collections =");
        writer.Indentation++;
        writer.WriteLine($"global::System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(new global::System.Collections.Generic.Dictionary<global::System.Type, {keyedFunction}>");
        writer.WriteLine('{');
        writer.Indentation++;
        foreach(IGrouping<TypeRef, CollectionPlan> group in plan.Collections.GroupBy(static c => c.Element))
        {
            string service = TypeNames.CSharp(group.Key);
            foreach(string type in group.SelectMany(CollectionTypes).Distinct())
            {
                if(fallback)
                {
                    writer.WriteLine($"{{ typeof({type}), static (c, key) => __MergeKeyed(c.__Fallback(), key, global::System.Array.Empty<{service}>()) }},");
                }
                else
                {
                    writer.WriteLine($"{{ typeof({type}), static (c, _) => global::System.Array.Empty<{service}>() }},");
                }
            }
        }

        writer.Indentation--;
        writer.WriteLine("});");
        writer.Indentation--;

        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"private static readonly global::System.Collections.Frozen.FrozenDictionary<{typeAndKey}, {keyedFunction}> __keyedLookup = __BuildKeyedLookup();");
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"private static global::System.Collections.Frozen.FrozenDictionary<{typeAndKey}, {keyedFunction}> __BuildKeyedLookup()");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine($"global::System.Collections.Generic.Dictionary<{typeAndKey}, {keyedFunction}> table = new global::System.Collections.Generic.Dictionary<{typeAndKey}, {keyedFunction}>();");
        bool hasKeys = keyed.Length > 0 || keyedCollections.Length > 0;
        if(hasKeys)
        {
            writer.WriteLine($"global::System.Collections.Generic.HashSet<{typeAndKey}> keys = new global::System.Collections.Generic.HashSet<{typeAndKey}>();");

            // Each key is read one time
            Dictionary<string, string> variables = [];
            foreach(KeyRef key in keyed.Select(static e => e.Key!).Concat(keyedCollections.Select(static c => c.Key!)))
            {
                if(!variables.ContainsKey(key.Canonical))
                {
                    string variable = $"k{variables.Count}";
                    variables[key.Canonical] = variable;
                    writer.WriteLine($"object {variable} = {KeyNames.Expression(key)};");
                }
            }

            foreach(LookupEntry entry in keyed)
            {
                string service = TypeNames.CSharp(entry.ServiceType);
                writer.WriteLine($"__AddKeyed(table, keys, typeof({service}), {variables[entry.Key!.Canonical]}, static (c, _) => {Single(plan, entry, service)});");
                if(keyedCollections.FirstOrDefault(c => c.Element.Equals(entry.ServiceType) && c.Key!.Equals(entry.Key)) is { } collection)
                {
                    WriteKeyedCollection(collection, variables[entry.Key!.Canonical]);
                }
            }

            // The keyed collections that only a dependency site asks for, after all registered keys. Only registered keys
            // throw (Semantics.spec, "Keyed services"). A key that is equal at run time to a key of the table uses the
            // collection of that key, as a lookup with Equals does (Container.Output.spec, "Lookup rules")
            foreach(CollectionPlan collection in demandOnly)
            {
                string enumerable = $"global::System.Collections.Generic.IEnumerable<{TypeNames.CSharp(collection.Element)}>";
                foreach(string type in CollectionTypes(collection))
                {
                    writer.WriteLine($"__TryAddKeyed(table, keys, typeof({type}), typeof({enumerable}), {variables[collection.Key!.Canonical]}, static (c, _) => c.{All(collection)}());");
                }
            }

            // AnyKey: all registrations of the type that have a key, in the ordering. One service with AnyKey throws, as in MS.DI
            foreach(IGrouping<TypeRef, LookupEntry> group in keyed.GroupBy(static e => e.ServiceType))
            {
                writer.WriteLine($"table.Add((typeof({TypeNames.CSharp(group.Key)}), {DependencyInjection}.KeyedService.AnyKey), static (_, _) => throw new {InvalidOperation}({SymbolDisplay.FormatLiteral($"KeyedService.AnyKey cannot be used to resolve a single service of type '{TypeNames.Display(group.Key)}'.", quote: true)}));");
            }
        }

        foreach(CollectionPlan collection in plan.Collections.Where(static c => c.AnyKey))
        {
            foreach(string type in CollectionTypes(collection))
            {
                writer.WriteLine($"table.Add((typeof({type}), {DependencyInjection}.KeyedService.AnyKey), static (c, _) => c.{All(collection)}());");
            }
        }

        writer.WriteLine("return global::System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(table);");
        writer.Indentation--;
        writer.WriteLine('}');

        if(hasKeys)
        {
            writer.WriteLine();
            writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
            writer.WriteLine($"private static void __AddKeyed(global::System.Collections.Generic.Dictionary<{typeAndKey}, {keyedFunction}> table, global::System.Collections.Generic.HashSet<{typeAndKey}> keys, global::System.Type type, object key, {keyedFunction} factory)");
            writer.WriteLine('{');
            writer.WriteLine($"    if(keys.TryGetValue((type, key), out {typeAndKey} existing))");
            writer.WriteLine("    {");
            writer.WriteLine($"        throw new {InvalidOperation}(\"Two keys of '\" + type + \"' are equal at run time: '\" + existing.Item2 + \"' and '\" + key + \"'.\");");
            writer.WriteLine("    }");
            writer.WriteLine();
            writer.WriteLine("    keys.Add((type, key));");
            writer.WriteLine("    table.Add((type, key), factory);");
            writer.WriteLine('}');
        }

        if(demandOnly.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
            writer.WriteLine($"private static void __TryAddKeyed(global::System.Collections.Generic.Dictionary<{typeAndKey}, {keyedFunction}> table, global::System.Collections.Generic.HashSet<{typeAndKey}> keys, global::System.Type type, global::System.Type enumerable, object key, {keyedFunction} factory)");
            writer.WriteLine('{');
            writer.WriteLine("    if(!keys.Add((type, key)))");
            writer.WriteLine("    {");
            writer.WriteLine("        return;");
            writer.WriteLine("    }");
            writer.WriteLine();
            writer.WriteLine("    // Each collection resolver returns an array of the element, which implements each collection type");
            writer.WriteLine($"    table.Add((type, key), table.TryGetValue((enumerable, key), out {keyedFunction}? existing) ? existing : factory);");
            writer.WriteLine('}');
        }

        void WriteKeyedCollection(CollectionPlan collection, string variable)
        {
            foreach(string type in CollectionTypes(collection))
            {
                writer.WriteLine($"__AddKeyed(table, keys, typeof({type}), {variable}, static (c, _) => c.{All(collection)}());");
            }
        }
    }

    /// <summary>The collection types that the lookup table gives for a collection: <c>IEnumerable&lt;T&gt;</c> first.</summary>
    private static IEnumerable<string> CollectionTypes(CollectionPlan collection)
        => collection.Types.Select(TypeNames.CSharp).Prepend($"global::System.Collections.Generic.IEnumerable<{TypeNames.CSharp(collection.Element)}>");

    /// <summary>The expression of one service: the candidate, or an exception when more than one candidate stays.</summary>
    private static string Single(ContainerPlan plan, LookupEntry entry, string service)
    {
        if(entry.Candidates.Length == 1)
        {
            return $"({service})c.{Get(plan.Definitions[entry.Candidates[0]].Definition)}()";
        }

        string names = string.Join(", ", entry.Candidates.Select(i => KeyNames.Definition(plan.Definitions[i].Definition)));
        string display = entry.Key is null ? TypeNames.Display(entry.ServiceType) : $"{TypeNames.Display(entry.ServiceType)} (Key: {KeyNames.Display(entry.Key)})";
        return $"throw new {InvalidOperation}({SymbolDisplay.FormatLiteral($"More than one registration provides '{display}': {names}.", quote: true)})";
    }

    /// <summary>The array of the candidates, in their ordering, each resolved on <paramref name="owner"/>.</summary>
    private static string Elements(ContainerPlan plan, IEnumerable<int> candidates, string service, string owner)
        => candidates.Any()
            ? $"new {service}[] {{ {string.Join(", ", candidates.Select(i => $"({service}){owner}{Get(plan.Definitions[i].Definition)}()"))} }}"
            : $"global::System.Array.Empty<{service}>()";

    /// <summary>
    /// The lookup by <c>Type</c>. A type that the table does not have goes to the fallback provider when the container
    /// has one (Container.Output.spec, "Lookup rules" and "Behavior for each request").
    /// </summary>
    private static void WriteServiceProvider(SourceWriter writer, string self, bool fallback)
    {
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public object? GetService(global::System.Type serviceType)");
        writer.WriteLine('{');
        writer.WriteLine("    __ThrowIfDisposed();");
        writer.WriteLine($"    return __lookup.TryGetValue(serviceType, out global::System.Func<{self}, object>? factory) ? factory(this) : {(fallback ? "__Fallback().GetService(serviceType)" : "null")};");
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public object GetRequiredService(global::System.Type serviceType)");
        writer.WriteLine($"    => GetService(serviceType) ?? throw new {InvalidOperation}(\"No service for type '\" + serviceType + \"' has been registered.\");");
        writer.WriteLine();
        // Keys compare with Equals at run time, as in MS.DI (Container.Output.spec, "Lookup rules")
        string keyedFunction = $"global::System.Func<{self}, object, object>";
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public object? GetKeyedService(global::System.Type serviceType, object? serviceKey)");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine("__ThrowIfDisposed();");
        writer.WriteLine("if(serviceKey is null)");
        writer.WriteLine('{');
        writer.WriteLine("    return GetService(serviceType);");
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine($"if(__keyedLookup.TryGetValue((serviceType, serviceKey), out {keyedFunction}? factory))");
        writer.WriteLine('{');
        writer.WriteLine("    return factory(this, serviceKey);");
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine($"if(__collections.TryGetValue(serviceType, out {keyedFunction}? collection))");
        writer.WriteLine('{');
        writer.WriteLine("    return collection(this, serviceKey);");
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine("// One service with AnyKey: MS.DI throws too");
        writer.WriteLine($"if(global::System.Object.ReferenceEquals(serviceKey, {DependencyInjection}.KeyedService.AnyKey) && __lookup.ContainsKey(serviceType))");
        writer.WriteLine('{');
        writer.WriteLine($"    throw new {InvalidOperation}(\"KeyedService.AnyKey cannot be used to resolve a single service of type '\" + serviceType + \"'.\");");
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine(fallback
            ? $"return __Fallback() is {DependencyInjection}.IKeyedServiceProvider keyed ? keyed.GetKeyedService(serviceType, serviceKey) : null;"
            : "return null;");
        writer.Indentation--;
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("public object GetRequiredKeyedService(global::System.Type serviceType, object? serviceKey)");
        writer.WriteLine($"    => GetKeyedService(serviceType, serviceKey) ?? throw new {InvalidOperation}(\"No service for type '\" + serviceType + \"' has been registered.\");");
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        if(fallback)
        {
            // MS.DI supplies these interfaces as services: its provider does not implement them
            writer.WriteLine("public bool IsService(global::System.Type serviceType)");
            writer.WriteLine($"    => __lookup.ContainsKey(serviceType) || (__singletons.Fallback.GetService(typeof({DependencyInjection}.IServiceProviderIsService)) is {DependencyInjection}.IServiceProviderIsService fallback && fallback.IsService(serviceType));");
            writer.WriteLine();
            writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
            writer.WriteLine("public bool IsKeyedService(global::System.Type serviceType, object? serviceKey)");
            writer.WriteLine("    => serviceKey is null");
            writer.WriteLine("        ? IsService(serviceType)");
            writer.WriteLine($"        : __keyedLookup.ContainsKey((serviceType, serviceKey)) || __collections.ContainsKey(serviceType) || (__singletons.Fallback.GetService(typeof({DependencyInjection}.IServiceProviderIsKeyedService)) is {DependencyInjection}.IServiceProviderIsKeyedService fallback && fallback.IsKeyedService(serviceType, serviceKey));");
        }
        else
        {
            writer.WriteLine("public bool IsService(global::System.Type serviceType) => __lookup.ContainsKey(serviceType);");
            writer.WriteLine();
            writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
            writer.WriteLine("public bool IsKeyedService(global::System.Type serviceType, object? serviceKey)");
            writer.WriteLine("    => serviceKey is null ? IsService(serviceType) : __keyedLookup.ContainsKey((serviceType, serviceKey)) || __collections.ContainsKey(serviceType);");
        }
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
        if(plan.Container.IntegrateServiceProvider)
        {
            // The last step: the provider that ServiceProviderFactory created. The root never disposes a provider that
            // the user passes (Container.Output.spec, "Disposal of the fallback scope and the fallback provider").
            writer.WriteLine("if(s.OwnsFallback)");
            writer.WriteLine('{');
            writer.Indentation++;
            WriteFallbackDisposal(writer, "s.Fallback", isAsync);
            writer.Indentation--;
            writer.WriteLine('}');
        }

        writer.Indentation--;
        writer.WriteLine('}');
        writer.WriteLine("else");
        writer.WriteLine('{');
        writer.Indentation++;
        WriteDisposals(writer, plan, Lifetime.Scoped, string.Empty, isAsync);
        if(plan.Container.IntegrateServiceProvider)
        {
            // The last step: the fallback scope. The exchange is the right to dispose it
            writer.WriteLine($"if({Interlocked}.Exchange(ref __fallbackScope, null) is {{ }} fallbackScope)");
            writer.WriteLine('{');
            writer.Indentation++;
            WriteFallbackDisposal(writer, "fallbackScope", isAsync);
            writer.Indentation--;
            writer.WriteLine('}');
        }

        writer.Indentation--;
        writer.WriteLine('}');
        writer.WriteLine();
    }

    /// <summary>
    /// Disposes the fallback scope or the fallback provider that the container owns. <c>DisposeAsync</c> prefers
    /// <c>IAsyncDisposable</c>. An exception counts in the aggregation.
    /// </summary>
    private static void WriteFallbackDisposal(SourceWriter writer, string target, bool isAsync)
    {
        writer.WriteLine("try");
        writer.WriteLine('{');
        writer.Indentation++;
        if(isAsync)
        {
            writer.WriteLine($"if({target} is global::System.IAsyncDisposable asyncDisposable)");
            writer.WriteLine('{');
            writer.WriteLine("    await asyncDisposable.DisposeAsync().ConfigureAwait(false);");
            writer.WriteLine('}');
            writer.WriteLine($"else if({target} is global::System.IDisposable disposable)");
        }
        else
        {
            writer.WriteLine($"if({target} is global::System.IDisposable disposable)");
        }

        writer.WriteLine('{');
        writer.WriteLine("    disposable.Dispose();");
        writer.WriteLine('}');
        writer.Indentation--;
        writer.WriteLine('}');
        writer.WriteLine($"catch({Exception} e)");
        writer.WriteLine('{');
        writer.WriteLine($"    (errors ??= new {ExceptionList}()).Add(e);");
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

        if(container.IntegrateServiceProvider || stored.Any(static d => d.Definition.Disposal != Disposal.None))
        {
            WriteDisposeLost(writer);
        }

        if(container.IntegrateServiceProvider)
        {
            WriteFallbackHelpers(writer, container);
        }

        if(container.ThreadSafeStrategy is not (StrategyNone or StrategyLock) && stored.Any(static d => (d.Definition.Disposal & Disposal.Sync) != 0))
        {
            WriteDisposeLoser(writer);
        }
    }

    /// <summary>
    /// Disposes an instance that nobody else disposes, without a wait: an instance that is created while the container
    /// is disposed, or an instance whose creation fails after <c>new</c>. An exception of this disposal goes to the
    /// callback, because another exception (<c>ObjectDisposedException</c>, the original one) is thrown.
    /// </summary>
    internal static void WriteDisposeLost(SourceWriter writer)
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

    /// <summary>
    /// <c>__Fallback</c> (the provider for a resolution), the lazy fallback scope, the merge of collections, and
    /// <c>ServiceProviderFactory</c> (Container.Output.spec, "Fallback provider" and "Integration with IServiceCollection").
    /// </summary>
    private static void WriteFallbackHelpers(SourceWriter writer, ContainerModel container)
    {
        // The root uses the fallback provider itself, and a scope uses its fallback scope
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"private {ServiceProvider} __Fallback() => __isRoot ? __singletons.Fallback : __fallbackScope?.ServiceProvider ?? __CreateFallbackScope();");

        // Concurrent first uses leave one fallback scope, and each extra one is disposed at once. A disposal of the scope
        // that started meanwhile and this side compete for the new fallback scope, so that it is disposed exactly one time.
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"private {ServiceProvider} __CreateFallbackScope()");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine($"{ServiceProvider} fallback = __singletons.Fallback;");
        writer.WriteLine($"if(fallback.GetService(typeof({DependencyInjection}.IServiceScopeFactory)) is not {DependencyInjection}.IServiceScopeFactory factory)");
        writer.WriteLine('{');
        writer.WriteLine("    return fallback;");
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine($"{DependencyInjection}.IServiceScope scope = factory.CreateScope();");
        writer.WriteLine($"if({Interlocked}.CompareExchange(ref __fallbackScope, scope, null) is {{ }} winner)");
        writer.WriteLine('{');
        writer.WriteLine("    __DisposeLost(scope);");
        writer.WriteLine("    return winner.ServiceProvider;");
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine($"if({Volatile}.Read(ref __disposed) != 0)");
        writer.WriteLine('{');
        writer.WriteLine($"    if(global::System.Object.ReferenceEquals({Interlocked}.CompareExchange(ref __fallbackScope, null, scope), scope))");
        writer.WriteLine("    {");
        writer.WriteLine("        __DisposeLost(scope);");
        writer.WriteLine("    }");
        writer.WriteLine();
        writer.WriteLine($"    throw new global::System.ObjectDisposedException(\"{container.Name}\");");
        writer.WriteLine('}');
        writer.WriteLine();
        writer.WriteLine("return scope.ServiceProvider;");
        writer.Indentation--;
        writer.WriteLine('}');

        // The services of the fallback provider first, then those of the container
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"private static T[] __Merge<T>({ServiceProvider} fallback, T[] own)");
        writer.WriteLine('{');
        writer.WriteLine("    if(fallback.GetService(typeof(global::System.Collections.Generic.IEnumerable<T>)) is not global::System.Collections.Generic.IEnumerable<T> external)");
        writer.WriteLine("    {");
        writer.WriteLine("        return own;");
        writer.WriteLine("    }");
        writer.WriteLine();
        writer.WriteLine("    global::System.Collections.Generic.List<T> all = new global::System.Collections.Generic.List<T>(external);");
        writer.WriteLine("    all.AddRange(own);");
        writer.WriteLine("    return all.ToArray();");
        writer.WriteLine('}');

        // The keyed services of the fallback provider for the same key first, then those of the container
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"private static T[] __MergeKeyed<T>({ServiceProvider} fallback, object key, T[] own)");
        writer.WriteLine('{');
        writer.WriteLine($"    if(fallback is not {DependencyInjection}.IKeyedServiceProvider keyed || keyed.GetKeyedService(typeof(global::System.Collections.Generic.IEnumerable<T>), key) is not global::System.Collections.Generic.IEnumerable<T> external)");
        writer.WriteLine("    {");
        writer.WriteLine("        return own;");
        writer.WriteLine("    }");
        writer.WriteLine();
        writer.WriteLine("    global::System.Collections.Generic.List<T> all = new global::System.Collections.Generic.List<T>(external);");
        writer.WriteLine("    all.AddRange(own);");
        writer.WriteLine("    return all.ToArray();");
        writer.WriteLine('}');

        if(!container.HasServiceProviderBuilder)
        {
            return;
        }

        // An independent nested class: nobody creates a container only to get the factory. The root disposes the
        // provider that the factory builds. When the constructor of the root throws, the factory disposes it.
        string collection = $"{DependencyInjection}.IServiceCollection";
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine($"public sealed class ServiceProviderFactory : {DependencyInjection}.IServiceProviderFactory<{collection}>");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine($"public {collection} CreateBuilder({collection} services) => services;");
        writer.WriteLine();
        writer.WriteLine($"public {ServiceProvider} CreateServiceProvider({collection} containerBuilder)");
        writer.WriteLine('{');
        writer.Indentation++;
        writer.WriteLine($"{DependencyInjection}.ServiceProvider provider = {DependencyInjection}.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(containerBuilder);");
        writer.WriteLine("try");
        writer.WriteLine('{');
        writer.WriteLine($"    return new {TypeNames.CSharp(container.Type)}(provider, true);");
        writer.WriteLine('}');
        writer.WriteLine("catch");
        writer.WriteLine('{');
        writer.WriteLine("    __DisposeLost(provider);");
        writer.WriteLine("    throw;");
        writer.WriteLine('}');
        writer.Indentation--;
        writer.WriteLine('}');
        writer.Indentation--;
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
