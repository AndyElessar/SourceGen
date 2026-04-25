using System;
using System.Collections.Generic;
using System.Linq;
using static SourceGen.Ioc.SourceGenerator.Models.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Writes individual service resolver methods.
    /// </summary>
    private static void WriteServiceResolverSection(
        SourceWriter writer,
        ContainerRegistrationGroups groups)
    {
        writer.WriteLine("#region Service Resolution");
        writer.WriteLine();

        var writtenMethods = new HashSet<string>(StringComparer.Ordinal);

        WriteServiceResolverGroup(writer, groups.SingletonEntries, writtenMethods, writeField: true);
        WriteServiceResolverGroup(writer, groups.ScopedEntries, writtenMethods, writeField: true);
        WriteServiceResolverGroup(writer, groups.TransientEntries, writtenMethods, writeField: false);

        foreach(var entry in groups.CollectionEntries)
        {
            entry.WriteResolver(writer);
            writer.WriteLine();
        }

        var kvpEntries = groups.WrapperEntries
            .OfType<KvpWrapperContainerEntry>()
            .ToList();

        if(kvpEntries.Count > 0)
        {
            writer.WriteLine("// KeyValuePair resolver methods");
            writer.WriteLine();

            foreach(var entry in kvpEntries)
            {
                entry.WriteResolver(writer);
                writer.WriteLine();
            }

            WriteKvpCollectionResolvers(writer, kvpEntries);
        }

        var lazyEntries = groups.WrapperEntries
            .OfType<LazyWrapperContainerEntry>()
            .ToList();

        if(lazyEntries.Count > 0)
        {
            writer.WriteLine("// Lazy wrapper fields");
            writer.WriteLine();

            foreach(var entry in lazyEntries)
            {
                entry.WriteField(writer);
            }

            writer.WriteLine();

            foreach(var entry in lazyEntries)
            {
                if(!entry.EmitCollectionResolver)
                    continue;

                entry.WriteCollectionResolver(writer);
                writer.WriteLine();
            }
        }

        var funcEntries = groups.WrapperEntries
            .OfType<FuncWrapperContainerEntry>()
            .ToList();

        if(funcEntries.Count > 0)
        {
            writer.WriteLine("// Func wrapper fields");
            writer.WriteLine();

            foreach(var entry in funcEntries)
            {
                entry.WriteField(writer);
            }

            writer.WriteLine();

            foreach(var entry in funcEntries)
            {
                if(!entry.EmitCollectionResolver)
                    continue;

                entry.WriteCollectionResolver(writer);
                writer.WriteLine();
            }
        }

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    private static void WriteServiceResolverGroup(
        SourceWriter writer,
        ImmutableEquatableArray<ContainerEntry> entries,
        HashSet<string> writtenMethods,
        bool writeField)
    {
        foreach(var entry in entries)
        {
            if(!TryGetResolverMethodName(entry, out var resolverMethodName))
                continue;

            if(!writtenMethods.Add(resolverMethodName))
                continue;

            if(writeField)
            {
                entry.WriteField(writer);

                if(entry is AsyncContainerEntry)
                {
                    writer.WriteLine();
                }
            }

            entry.WriteResolver(writer);
            writer.WriteLine();
        }
    }

    private static void WriteKvpCollectionResolvers(
        SourceWriter writer,
        List<KvpWrapperContainerEntry> kvpEntries)
    {
        var grouped = kvpEntries
            .GroupBy(static e => (e.KeyTypeName, e.ValueTypeName))
            .ToList();

        foreach(var group in grouped)
        {
            var (keyTypeName, valueTypeName) = group.Key;
            var kvpTypeName = $"global::System.Collections.Generic.KeyValuePair<{keyTypeName}, {valueTypeName}>";
            var arrayMethodName = GetKvpArrayResolverMethodName(keyTypeName, valueTypeName);

            writer.WriteLine($"private {kvpTypeName}[] {arrayMethodName}() =>");
            writer.Indentation++;
            writer.WriteLine("[");
            writer.Indentation++;

            foreach(var entry in group)
            {
                writer.WriteLine($"{entry.KvpResolverMethodName}(),");
            }

            writer.Indentation--;
            writer.WriteLine("];");
            writer.Indentation--;
            writer.WriteLine();
        }

        foreach(var group in grouped)
        {
            var (keyTypeName, valueTypeName) = group.Key;
            var dictionaryMethodName = GetKvpDictionaryResolverMethodName(keyTypeName, valueTypeName);

            writer.WriteLine($"private global::System.Collections.Generic.Dictionary<{keyTypeName}, {valueTypeName}> {dictionaryMethodName}() =>");
            writer.Indentation++;
            writer.WriteLine($"new global::System.Collections.Generic.Dictionary<{keyTypeName}, {valueTypeName}>()");
            writer.WriteLine("{");
            writer.Indentation++;

            foreach(var entry in group)
            {
                writer.WriteLine($"[{entry.KeyExpr}] = {entry.ResolverMethodName}(),");
            }

            writer.Indentation--;
            writer.WriteLine("};");
            writer.Indentation--;
            writer.WriteLine();
        }
    }

    /// <summary>
    /// Writes individual service resolver methods.
    /// </summary>
    private static void WritePartialAccessorImplementations(
        SourceWriter writer,
        ContainerModel container,
        ContainerRegistrationGroups groups)
    {
        if(container.PartialAccessors.Length == 0)
            return;

        writer.WriteLine("#region Partial Accessor Implementations");
        writer.WriteLine();

        foreach(var accessor in container.PartialAccessors)
        {
            var isTaskReturn = accessor.Kind == PartialAccessorKind.Method
                && TryExtractTaskInnerType(accessor.ReturnTypeName, out _);

            var resolveExpression = ResolvePartialAccessorExpression(accessor, container, groups);

            switch(accessor.Kind)
            {
                case PartialAccessorKind.Method:
                    // Async partial methods (returning Task<T>) require the 'async' modifier
                    if(isTaskReturn)
                    {
                        writer.WriteLine($"public partial async {accessor.ReturnTypeName} {accessor.Name}() => {resolveExpression};");
                    }
                    else
                    {
                        writer.WriteLine($"public partial {accessor.ReturnTypeName}{(accessor.IsNullable ? "?" : "")} {accessor.Name}() => {resolveExpression};");
                    }
                    break;

                case PartialAccessorKind.Property:
                    writer.WriteLine($"public partial {accessor.ReturnTypeName}{(accessor.IsNullable ? "?" : "")} {accessor.Name} {{ get => {resolveExpression}; }}");
                    break;
            }

            writer.WriteLine();
        }

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    /// <summary>
    /// Resolves the expression to use for a partial accessor implementation.
    /// Looks up the registration by return type and optional key, with fallback to IServiceProvider.
    /// For <c>Task&lt;T&gt;</c> return types, routes through the async resolver.
    /// </summary>
    private static string ResolvePartialAccessorExpression(
        PartialAccessorData accessor,
        ContainerModel container,
        ContainerRegistrationGroups groups)
    {
        var serviceType = accessor.ReturnTypeName;
        var key = accessor.Key;

        // Handle Task<T> return types — route through the async resolver.
        if(TryExtractTaskInnerType(serviceType, out var innerTypeName))
        {
            if(groups.LastWinsByServiceType.TryGetValue((innerTypeName, key), out var entry))
            {
                switch(entry)
                {
                    case AsyncContainerEntry asyncEntry:
                        return $"await {GetAsyncResolverMethodName(asyncEntry.ResolverMethodName)}()";
                    case AsyncTransientContainerEntry asyncTransientEntry:
                        return $"await {GetAsyncCreateMethodName(asyncTransientEntry.ResolverMethodName)}()";
                    case InstanceContainerEntry instanceEntry:
                        return $"global::System.Threading.Tasks.Task.FromResult(({innerTypeName}){instanceEntry.Registration.Instance})";
                    case ServiceContainerEntry serviceEntry:
                        return $"global::System.Threading.Tasks.Task.FromResult(({innerTypeName}){serviceEntry.ResolverMethodName}())";
                }
            }

            // Fallback: delegate to IServiceProvider if available
            if(container.IntegrateServiceProvider)
            {
                if(key is not null)
                    return $"({serviceType})GetRequiredKeyedService(typeof({serviceType}), {key})";
                return $"({serviceType})GetRequiredService(typeof({serviceType}))";
            }

            return $"""throw new global::System.InvalidOperationException("Service '{innerTypeName}' is not registered.")""";
        }

        // Try to find direct resolver in this container
        if(groups.LastWinsByServiceType.TryGetValue((serviceType, key), out var directEntry))
        {
            switch(directEntry)
            {
                case InstanceContainerEntry instanceEntry:
                    return instanceEntry.Registration.Instance!;
                case AsyncContainerEntry asyncEntry:
                    return $"{GetAsyncResolverMethodName(asyncEntry.ResolverMethodName)}().ConfigureAwait(false).GetAwaiter().GetResult()";
                case AsyncTransientContainerEntry asyncTransientEntry:
                    return $"{GetAsyncCreateMethodName(asyncTransientEntry.ResolverMethodName)}().ConfigureAwait(false).GetAwaiter().GetResult()";
                case ServiceContainerEntry serviceEntry:
                    return $"{serviceEntry.ResolverMethodName}()";
            }
        }

        // Fallback to GetService/GetRequiredService (only when IntegrateServiceProvider is enabled)
        if(container.IntegrateServiceProvider)
        {
            if(key is not null)
            {
                return accessor.IsNullable
                    ? $"GetKeyedService(typeof({serviceType}), {key}) as {serviceType}"
                    : $"({serviceType})GetRequiredKeyedService(typeof({serviceType}), {key})";
            }

            return accessor.IsNullable
                ? $"GetService(typeof({serviceType})) as {serviceType}"
                : $"({serviceType})GetRequiredService(typeof({serviceType}))";
        }

        // No resolver found and no fallback: throw (analyzer should have caught this)
        return accessor.IsNullable
            ? "default"
            : $"""throw new global::System.InvalidOperationException("Service '{serviceType}' is not registered.")""";
    }

    /// <summary>
    /// Tries to extract the inner type name from a
    /// <c>global::System.Threading.Tasks.Task&lt;T&gt;</c> type name string.
    /// Returns <see langword="true"/> and sets <paramref name="innerTypeName"/> if matched.
    /// </summary>
    private static bool TryExtractTaskInnerType(string typeName, out string innerTypeName)
    {
        const string TaskPrefix = "global::System.Threading.Tasks.Task<";
        if(typeName.StartsWith(TaskPrefix, StringComparison.Ordinal)
            && typeName.EndsWith(">", StringComparison.Ordinal))
        {
            innerTypeName = typeName[TaskPrefix.Length..^1];
            return true;
        }
        innerTypeName = string.Empty;
        return false;
    }

    /// <summary>
    /// Gets the array resolver method name for IEnumerable&lt;T&gt;, IReadOnlyCollection&lt;T&gt;, IReadOnlyList&lt;T&gt;, T[] resolution.
    /// </summary>
    private static string GetArrayResolverMethodName(string serviceType)
    {
        var baseName = GetSafeIdentifier(serviceType);
        return $"GetAll{baseName}Array";
    }
}