namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <param name="IsShared">
    /// Whether one <c>Lazy&lt;T&gt;</c> field is shared by the container (or scope). Transient services are not shared:
    /// each consumer gets its own <c>Lazy&lt;T&gt;</c> and therefore its own instance, like the Register output.
    /// </param>
    /// <param name="CollectionElements">The <c>Lazy&lt;T&gt;[]</c> elements: field names, or inline <c>Lazy&lt;T&gt;</c> creations.</param>
    private sealed record class LazyWrapperContainerEntry(
        string InnerServiceTypeName,
        string InnerImplTypeName,
        string FieldName,
        string InnerResolverMethodName,
        string? Key,
        bool IsShared,
        bool EmitCollectionResolver,
        string? ArrayMethodName,
        ImmutableEquatableArray<string> CollectionElements) : ContainerEntry
    {
        /// <summary>
        /// Formats a new <c>Lazy&lt;T&gt;</c> of <paramref name="innerServiceTypeName"/> over <paramref name="resolverCall"/>.
        /// </summary>
        public static string FormatNewLazy(string innerServiceTypeName, string resolverCall) =>
            $"new global::System.Lazy<{innerServiceTypeName}>(() => {resolverCall}, global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication)";

        public override void WriteField(SourceWriter writer)
        {
            if(IsShared)
            {
                writer.WriteLine($"private readonly global::System.Lazy<{InnerServiceTypeName}> {FieldName};");
            }
        }

        public override void WriteResolver(SourceWriter writer)
        {
        }

        public override void WriteInit(SourceWriter writer)
        {
            if(IsShared)
            {
                writer.WriteLine($"{FieldName} = new global::System.Lazy<{InnerServiceTypeName}>(() => {InnerResolverMethodName}(), global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);");
            }
        }

        public override void WriteCollectionResolver(SourceWriter writer)
        {
            if(!EmitCollectionResolver)
            {
                return;
            }

            writer.WriteLine($"private global::System.Lazy<{InnerServiceTypeName}>[] {ArrayMethodName}() =>");
            writer.Indentation++;
            writer.WriteLine("[");
            writer.Indentation++;

            foreach(var element in CollectionElements)
            {
                writer.WriteLine($"{element},");
            }

            writer.Indentation--;
            writer.WriteLine("];");
            writer.Indentation--;
        }

        public override void WriteLocalResolverEntries(SourceWriter writer)
        {
            if(!EmitCollectionResolver)
            {
                return;
            }

            var wrapperTypeName = $"global::System.Lazy<{InnerServiceTypeName}>";

            if(IsShared)
            {
                writer.WriteLine($"{ResolverEntryMethodName}(typeof({wrapperTypeName}), static c => c.{FieldName}),");
            }
            else
            {
                writer.WriteLine($"{ResolverEntryMethodName}(typeof({wrapperTypeName}), static c => new {wrapperTypeName}(() => c.{InnerResolverMethodName}(), global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication)),");
            }

            WriteCollectionResolverEntries(writer, wrapperTypeName, ArrayMethodName!);
        }
    }

    private sealed record class FuncWrapperContainerEntry(
        string InnerServiceTypeName,
        string InnerImplTypeName,
        string FieldName,
        string InnerResolverMethodName,
        string? Key,
        bool EmitCollectionResolver,
        string? ArrayMethodName,
        ImmutableEquatableArray<string> CollectionFieldNames) : ContainerEntry
    {
        public override void WriteField(SourceWriter writer)
        {
            writer.WriteLine($"private readonly global::System.Func<{InnerServiceTypeName}> {FieldName};");
        }

        public override void WriteResolver(SourceWriter writer)
        {
        }

        public override void WriteInit(SourceWriter writer)
        {
            writer.WriteLine($"{FieldName} = new global::System.Func<{InnerServiceTypeName}>(() => {InnerResolverMethodName}());");
        }

        public override void WriteCollectionResolver(SourceWriter writer)
        {
            if(!EmitCollectionResolver)
            {
                return;
            }

            writer.WriteLine($"private global::System.Func<{InnerServiceTypeName}>[] {ArrayMethodName}() =>");
            writer.Indentation++;
            writer.WriteLine("[");
            writer.Indentation++;

            foreach(var fieldName in CollectionFieldNames)
            {
                writer.WriteLine($"{fieldName},");
            }

            writer.Indentation--;
            writer.WriteLine("];");
            writer.Indentation--;
        }

        public override void WriteLocalResolverEntries(SourceWriter writer)
        {
            if(!EmitCollectionResolver)
            {
                return;
            }

            var wrapperTypeName = $"global::System.Func<{InnerServiceTypeName}>";

            writer.WriteLine($"{ResolverEntryMethodName}(typeof({wrapperTypeName}), static c => c.{FieldName}),");
            WriteCollectionResolverEntries(writer, wrapperTypeName, ArrayMethodName!);
        }
    }

    private sealed record class KvpWrapperContainerEntry(
        string KeyTypeName,
        string ValueTypeName,
        string KeyExpr,
        string ResolverMethodName,
        string KvpResolverMethodName,
        string ArrayMethodName,
        string DictionaryMethodName) : ContainerEntry
    {
        public override void WriteResolver(SourceWriter writer)
        {
            writer.WriteLine($"private global::System.Collections.Generic.KeyValuePair<{KeyTypeName}, {ValueTypeName}> {KvpResolverMethodName}() => new global::System.Collections.Generic.KeyValuePair<{KeyTypeName}, {ValueTypeName}>({KeyExpr}, {ResolverMethodName}());");
        }

        public override void WriteLocalResolverEntries(SourceWriter writer)
        {
            var kvpTypeName = $"global::System.Collections.Generic.KeyValuePair<{KeyTypeName}, {ValueTypeName}>";

            writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.Collections.Generic.IEnumerable<{kvpTypeName}>), static c => c.{DictionaryMethodName}()),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.Collections.Generic.IReadOnlyCollection<{kvpTypeName}>), static c => c.{DictionaryMethodName}()),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.Collections.Generic.ICollection<{kvpTypeName}>), static c => c.{DictionaryMethodName}()),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.Collections.Generic.IReadOnlyList<{kvpTypeName}>), static c => c.{ArrayMethodName}()),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.Collections.Generic.IList<{kvpTypeName}>), static c => c.{ArrayMethodName}()),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof({kvpTypeName}[]), static c => c.{ArrayMethodName}()),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.Collections.Generic.IReadOnlyDictionary<{KeyTypeName}, {ValueTypeName}>), static c => c.{DictionaryMethodName}()),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.Collections.Generic.IDictionary<{KeyTypeName}, {ValueTypeName}>), static c => c.{DictionaryMethodName}()),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.Collections.Generic.Dictionary<{KeyTypeName}, {ValueTypeName}>), static c => c.{DictionaryMethodName}()),");
        }
    }

    private sealed record class CollectionContainerEntry(
        string ElementServiceTypeName,
        string ArrayMethodName,
        ImmutableEquatableArray<ResolvedDependency> ElementResolvers) : ContainerEntry
    {
        public override void WriteResolver(SourceWriter writer)
        {
            writer.WriteLine($"private {ElementServiceTypeName}[] {ArrayMethodName}() =>");
            writer.Indentation++;
            writer.WriteLine("[");
            writer.Indentation++;

            foreach(var elementResolver in ElementResolvers)
            {
                writer.WriteLine($"{elementResolver.FormatExpression(false)},");
            }

            writer.Indentation--;
            writer.WriteLine("];");
            writer.Indentation--;
        }
    }
}
