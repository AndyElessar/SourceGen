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

        private const string GlobalPrefix = "global::";

        private readonly Dictionary<string, string> nameByTypeName;

        // Built on the first type not known up front, since most containers name only known types
        private HashSet<string>? usedNames;

        // Reused by GetShortName
        private char[] buffer = new char[128];

        /// <param name="typeNames">The type names known up front; the ones whose short names collide all get a hash suffix.</param>
        public ContainerNames(List<string> typeNames)
        {
            nameByTypeName = new(typeNames.Count, StringComparer.Ordinal);

            // Whether each short name belongs to more than one type
            var isSharedByShortName = new Dictionary<string, bool>(typeNames.Count, StringComparer.Ordinal);
            var hasSharedShortNames = false;
            foreach(var typeName in typeNames)
            {
                if(nameByTypeName.ContainsKey(typeName))
                    continue;

                var shortName = GetShortName(typeName);
                nameByTypeName[typeName] = shortName;

                if(isSharedByShortName.ContainsKey(shortName))
                {
                    isSharedByShortName[shortName] = true;
                    hasSharedShortNames = true;
                }
                else
                {
                    isSharedByShortName[shortName] = false;
                }
            }

            if(!hasSharedShortNames)
                return;

            foreach(var typeName in nameByTypeName.Keys.ToArray())
            {
                var shortName = nameByTypeName[typeName];
                if(isSharedByShortName[shortName])
                {
                    nameByTypeName[typeName] = AppendHash(shortName, typeName);
                }
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

            usedNames ??= new HashSet<string>(nameByTypeName.Values, StringComparer.Ordinal);

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
        private string GetShortName(string typeName)
        {
            if(buffer.Length <= typeName.Length)
            {
                buffer = new char[Math.Max(typeName.Length + 1, buffer.Length * 2)];
            }

            var length = 0;
            var segmentStart = 0;
            var lastWasSeparator = false;

            // "global::" is skipped wherever it occurs, also in generic arguments
            var start = 0;
            while(IsGlobalPrefixAt(typeName, start))
            {
                start += GlobalPrefix.Length;
            }

            if(start < typeName.Length && char.IsDigit(typeName[start]))
            {
                buffer[length++] = '_';
                segmentStart = length;
            }

            for(var i = start; i < typeName.Length; i++)
            {
                if(IsGlobalPrefixAt(typeName, i))
                {
                    i += GlobalPrefix.Length - 1;
                    continue;
                }

                var ch = typeName[i];
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

            return length > MaxLength
                ? AppendHash(new string(buffer, 0, TruncatedLength), typeName)
                : new string(buffer, 0, length);
        }

        private static bool IsGlobalPrefixAt(string typeName, int index) =>
            index + GlobalPrefix.Length <= typeName.Length
            && string.CompareOrdinal(typeName, index, GlobalPrefix, 0, GlobalPrefix.Length) == 0;

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
