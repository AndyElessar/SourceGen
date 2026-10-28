namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Short identifiers for type names used in generated container member names.
    /// A type is named by its simple name, with namespaces and nesting removed (also in generic arguments).
    /// When different types would get the same identifier, each gets a stable hash of its full name appended.
    /// Used only while grouping one container; the resulting member names are stored in the container entries.
    /// </summary>
    private sealed class ContainerNames
    {
        private const int MaxLength = 80;
        private const int TruncatedLength = 60;

        private readonly Dictionary<string, string> nameByTypeName = new(StringComparer.Ordinal);
        private readonly HashSet<string> usedNames = new(StringComparer.Ordinal);

        /// <param name="typeNames">The type names known up front; the ones whose short names collide all get a hash suffix.</param>
        public ContainerNames(IEnumerable<string> typeNames)
        {
            var typeNameByShortName = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach(var typeName in typeNames)
            {
                if(nameByTypeName.ContainsKey(typeName))
                    continue;

                var shortName = GetShortName(typeName);
                nameByTypeName[typeName] = shortName;

                if(!typeNameByShortName.TryGetValue(shortName, out var existing))
                {
                    typeNameByShortName[shortName] = typeName;
                }
                else if(existing is not null)
                {
                    // Collision: mark the short name as shared (null)
                    typeNameByShortName[shortName] = null;
                }
            }

            foreach(var typeName in nameByTypeName.Keys.ToArray())
            {
                var shortName = nameByTypeName[typeName];
                if(typeNameByShortName[shortName] is null)
                {
                    nameByTypeName[typeName] = AppendHash(shortName, typeName);
                }
            }

            foreach(var name in nameByTypeName.Values)
            {
                usedNames.Add(name);
            }
        }

        /// <summary>
        /// Gets the identifier for <paramref name="typeName"/>. A type not known up front gets a hash suffix
        /// only when its short name is already taken.
        /// </summary>
        public string Get(string typeName)
        {
            if(nameByTypeName.TryGetValue(typeName, out var name))
                return name;

            name = GetShortName(typeName);
            if(!usedNames.Add(name))
            {
                name = AppendHash(name, typeName);
                usedNames.Add(name);
            }

            nameByTypeName[typeName] = name;
            return name;
        }

        /// <summary>Gets the <c>T[]</c> resolver method name for collections of <paramref name="serviceType"/>.</summary>
        public string ArrayResolver(string serviceType) => $"GetAll{Get(serviceType)}Array";

        /// <summary>Gets the <c>Lazy&lt;T&gt;[]</c> resolver method name.</summary>
        public string LazyArrayResolver(string innerServiceType) => $"GetAllLazy_{Get(innerServiceType)}_Array";

        /// <summary>Gets the <c>Func&lt;T&gt;[]</c> resolver method name.</summary>
        public string FuncArrayResolver(string innerServiceType) => $"GetAllFunc_{Get(innerServiceType)}_Array";

        /// <summary>Gets the <c>KeyValuePair&lt;TKey, TValue&gt;[]</c> resolver method name.</summary>
        public string KvpArrayResolver(string keyType, string valueType) => $"GetAllKvp_{Get(keyType)}_{Get(valueType)}_Array";

        /// <summary>Gets the <c>Dictionary&lt;TKey, TValue&gt;</c> resolver method name.</summary>
        public string KvpDictionaryResolver(string keyType, string valueType) => $"GetAllKvp_{Get(keyType)}_{Get(valueType)}_Dictionary";

        /// <summary>
        /// Converts a type name to an identifier from its simple names: <c>global::A.B.Foo&lt;global::A.Bar, int&gt;</c>
        /// becomes <c>Foo_Bar_int</c>. Names longer than <see cref="MaxLength"/> are truncated and get a hash suffix.
        /// </summary>
        private static string GetShortName(string typeName)
        {
            var source = typeName.Replace("global::", "");
            var buffer = new char[source.Length + 1];
            var length = 0;
            var segmentStart = 0;
            var lastWasSeparator = false;

            if(source.Length > 0 && char.IsDigit(source[0]))
            {
                buffer[length++] = '_';
                segmentStart = length;
            }

            foreach(var ch in source)
            {
                if(char.IsLetterOrDigit(ch) || ch == '_')
                {
                    buffer[length++] = ch;
                    lastWasSeparator = false;
                }
                else if(ch == '.')
                {
                    // Drop the namespace or containing type written so far in this segment
                    length = segmentStart;
                }
                else
                {
                    if(!lastWasSeparator && length > 0)
                    {
                        buffer[length++] = '_';
                        lastWasSeparator = true;
                    }

                    segmentStart = length;
                }
            }

            if(lastWasSeparator)
            {
                length--;
            }

            if(length == 0)
                return "Generated";

            var shortName = new string(buffer, 0, length);
            return shortName.Length > MaxLength
                ? AppendHash(shortName.Substring(0, TruncatedLength), typeName)
                : shortName;
        }

        /// <summary>
        /// Appends the FNV-1a hash of <paramref name="typeName"/> (deterministic, unlike <see cref="string.GetHashCode()"/>).
        /// </summary>
        private static string AppendHash(string name, string typeName)
        {
            var hash = 2166136261u;
            foreach(var ch in typeName)
            {
                hash = unchecked((hash ^ ch) * 16777619u);
            }

            return $"{name}_{hash:x8}";
        }
    }
}
