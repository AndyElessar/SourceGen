using System;
using System.Collections.Generic;
using System.Linq;
using static SourceGen.Ioc.Constants;

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

        WriteProjectTaskHelper(writer, groups);
        WriteSyncFieldHelpers(writer, groups);

        foreach(var entry in groups.CollectionEntries)
        {
            entry.WriteResolver(writer);
            writer.WriteLine();
        }

        var kvpEntries = groups.KvpWrapperEntries;

        if(kvpEntries.Length > 0)
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

        var lazyEntries = groups.LazyWrapperEntries;

        if(lazyEntries.Length > 0)
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

        var funcEntries = groups.FuncWrapperEntries;

        if(funcEntries.Length > 0)
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
            if(entry is not ServiceContainerEntry { ResolverMethodName: var resolverMethodName })
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
        ImmutableEquatableArray<KvpWrapperContainerEntry> kvpEntries)
    {
        var grouped = kvpEntries
            .GroupBy(static e => (e.KeyTypeName, e.ValueTypeName))
            .ToList();

        foreach(var group in grouped)
        {
            var (keyTypeName, valueTypeName) = group.Key;
            var kvpTypeName = $"global::System.Collections.Generic.KeyValuePair<{keyTypeName}, {valueTypeName}>";
            var arrayMethodName = group.First().ArrayMethodName;

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
            var dictionaryMethodName = group.First().DictionaryMethodName;

            writer.WriteLine($"private global::System.Collections.Generic.Dictionary<{keyTypeName}, {valueTypeName}> {dictionaryMethodName}() =>");
            writer.Indentation++;
            writer.WriteLine($"new global::System.Collections.Generic.Dictionary<{keyTypeName}, {valueTypeName}>()");
            writer.WriteLine("{");
            writer.Indentation++;

            foreach(var entry in group)
            {
                writer.WriteLine($"[{entry.KeyExpr}] = {FormatWrappedResolveCall(entry.ValueInstance, entry.ResolverMethodName, "")},");
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

        for(var i = 0; i < container.PartialAccessors.Length; i++)
        {
            var accessor = container.PartialAccessors[i];
            var resolveExpression = groups.PartialAccessorDependencies[i].FormatExpression(accessor.IsNullable);

            switch(accessor.Kind)
            {
                case PartialAccessorKind.Method:
                    writer.WriteLine($"public partial {accessor.ReturnTypeName}{(accessor.IsNullable ? "?" : "")} {accessor.Name}() => {resolveExpression};");
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
}
