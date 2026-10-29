namespace SourceGen.Ioc;

internal static class TypeDataExtensions
{
    /// <summary>
    /// Factory methods and common type checks for TypeData and its derived types.
    /// </summary>
    /// <param name="typeData">The type data to check.</param>
    extension(TypeData typeData)
    {
        /// <summary>
        /// Creates a non-generic non-collection type data.
        /// </summary>
        public static TypeData CreateSimple(
            string Name,
            ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
            bool HasInjectConstructor = false,
            ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
            ImmutableEquatableArray<TypeData>? AllInterfaces = null,
            ImmutableEquatableArray<TypeData>? AllBaseClasses = null,
            TypeTraits Traits = default) =>
            new SimpleTypeData(
                Name,
                Traits,
                ConstructorParameters,
                HasInjectConstructor,
                InjectionMembers,
                AllInterfaces,
                AllBaseClasses);

        /// <summary>
        /// Creates a generic type data.
        /// </summary>
        public static GenericTypeData CreateGeneric(
            string Name,
            string NameWithoutGeneric,
            bool IsOpenGeneric,
            int GenericArity,
            bool IsNestedOpenGeneric = false,
            ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
            ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
            bool HasInjectConstructor = false,
            ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
            ImmutableEquatableArray<TypeData>? AllInterfaces = null,
            ImmutableEquatableArray<TypeData>? AllBaseClasses = null,
            TypeTraits Traits = default) =>
            new NamedGenericTypeData(
                Name,
                NameWithoutGeneric,
                IsOpenGeneric,
                GenericArity,
                Traits,
                IsNestedOpenGeneric,
                TypeParameters,
                ConstructorParameters,
                HasInjectConstructor,
                InjectionMembers,
                AllInterfaces,
                AllBaseClasses);

        /// <summary>
        /// Creates a type parameter placeholder (e.g., T, TRequest).
        /// </summary>
        public static TypeParameterTypeData CreateTypeParameter(string Name) =>
            new(Name, Name);

        /// <summary>
        /// Creates a wrapper type data for the specified <see cref="WrapperKind"/>.
        /// Returns the appropriate sealed derived type.
        /// </summary>
        public static WrapperTypeData CreateWrapper(
            string Name,
            string NameWithoutGeneric,
            bool IsOpenGeneric,
            int GenericArity,
            WrapperKind WrapperKind,
            bool IsNestedOpenGeneric = false,
            ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
            ImmutableEquatableArray<ParameterData>? ConstructorParameters = null,
            bool HasInjectConstructor = false,
            ImmutableEquatableArray<InjectionMemberData>? InjectionMembers = null,
            ImmutableEquatableArray<TypeData>? AllInterfaces = null,
            ImmutableEquatableArray<TypeData>? AllBaseClasses = null,
            TypeTraits Traits = default) => WrapperKind switch
            {
                WrapperKind.Enumerable => new EnumerableTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity, Traits,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.ReadOnlyCollection => new ReadOnlyCollectionTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity, Traits,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.Collection => new CollectionTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity, Traits,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.ReadOnlyList => new ReadOnlyListTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity, Traits,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.List => new ListTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity, Traits,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.Array => new ArrayTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity, Traits,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.Lazy => new LazyTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity, Traits,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.Func => new FuncTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity, Traits,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.Dictionary => new DictionaryTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity, Traits,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.KeyValuePair => new KeyValuePairTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity, Traits,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                WrapperKind.Task => new TaskTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity, Traits,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses),
                _ => new OtherWrapperTypeData(
                    Name, NameWithoutGeneric, IsOpenGeneric, GenericArity,
                    WrapperKind, Traits,
                    IsNestedOpenGeneric, TypeParameters,
                    ConstructorParameters, HasInjectConstructor, InjectionMembers,
                    AllInterfaces, AllBaseClasses)
            };

        /// <summary>
        /// Checks if the type is an array type (e.g., T[]).
        /// </summary>
        public bool IsArrayType =>
            typeData.Name.EndsWith("[]", StringComparison.Ordinal);

        /// <summary>
        /// Tries to extract the element type from an enumerable-compatible type that is NOT a <see cref="WrapperTypeData"/>.
        /// Checks direct IEnumerable&lt;T&gt;-compatible generic types, then AllInterfaces for IEnumerable&lt;T&gt; implementation.
        /// For <see cref="WrapperTypeData"/>, use <see cref="TypeDataExtensions.ElementType"/> directly instead.
        /// </summary>
        /// <returns>The element type if this is an enumerable-compatible type; otherwise, null.</returns>
        public TypeData? TryGetEnumerableElementType()
        {
            // For closed generic dependency extraction, allow direct IEnumerable<T>-compatible generic types.
            if(typeData is GenericTypeData
                {
                    GenericArity: 1,
                    TypeParameters: { Length: 1 } directTypeParameters
                } directGenericTypeData
                && IsEnumerableType(directGenericTypeData.NameWithoutGeneric))
            {
                return directTypeParameters[0].Type;
            }

            // Check AllInterfaces for IEnumerable<T> implementation
            if(typeData.AllInterfaces is { Length: > 0 })
            {
                foreach(var iface in typeData.AllInterfaces)
                {
                    // Look for IEnumerable<T> (with exactly one type argument)
                    if(iface is GenericTypeData { GenericArity: 1 } genericInterface
                        && IsEnumerableType(genericInterface.NameWithoutGeneric))
                    {
                        if(genericInterface.TypeParameters is { Length: 1 } ifaceTypeParams)
                        {
                            return ifaceTypeParams[0].Type;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Checks if the type is a non-enumerable wrapper type that requires factory construction.
        /// Direct <c>Lazy&lt;T&gt;</c> and <c>Func&lt;T&gt;</c> (where T is not a wrapper) are excluded
        /// because they now have standalone service registrations and can be resolved via DI directly.
        /// Nested wrappers like <c>Lazy&lt;Func&lt;T&gt;&gt;</c> still require inline construction.
        /// </summary>
        public bool NeedsWrapperResolution =>
            typeData switch
            {
                LazyTypeData lazy => lazy.InstanceType is WrapperTypeData,
                FuncTypeData func => func.ReturnType is WrapperTypeData,
                EnumerableTypeData => false,
                WrapperTypeData => true,
                SimpleTypeData or NamedGenericTypeData or TypeParameterTypeData => false
            };
    }

    extension(CollectionWrapperTypeData typeData)
    {
        /// <summary>
        /// Gets the element type of the collection wrapper (IEnumerable&lt;T&gt;, IReadOnlyCollection&lt;T&gt;,
        /// ICollection&lt;T&gt;, IReadOnlyList&lt;T&gt;, IList&lt;T&gt;, T[]).
        /// </summary>
        public TypeData ElementType => typeData.TypeParameters![0].Type;
    }

    extension(LazyTypeData typeData)
    {
        /// <summary>
        /// Gets the instance type of the Lazy&lt;T&gt; wrapper.
        /// </summary>
        public TypeData InstanceType => typeData.TypeParameters![0].Type;
    }

    extension(FuncTypeData typeData)
    {
        /// <summary>
        /// Gets the return type of the Func&lt;T&gt; wrapper.
        /// </summary>
        public TypeData ReturnType => typeData.TypeParameters![^1].Type;

        /// <summary>
        /// Gets the input parameter types of the Func wrapper (all type parameters except the last one).
        /// Empty for Func&lt;T&gt; (single type parameter).
        /// </summary>
        public ImmutableEquatableArray<TypeParameter> InputTypes =>
            typeData.TypeParameters is { Length: > 1 } tp
                ? tp.Take(tp.Length - 1).ToImmutableEquatableArray()
                : [];

        /// <summary>
        /// Whether this Func has input parameters (more than just the return type).
        /// </summary>
        public bool HasInputParameters => typeData.TypeParameters is { Length: > 1 };
    }

    extension(DictionaryTypeData typeData)
    {
        /// <summary>
        /// Gets the key type of the IDictionary&lt;TKey, TValue&gt; wrapper.
        /// </summary>
        public TypeData KeyType => typeData.TypeParameters![0].Type;

        /// <summary>
        /// Gets the value type of the IDictionary&lt;TKey, TValue&gt; wrapper.
        /// </summary>
        public TypeData ValueType => typeData.TypeParameters![1].Type;
    }

    extension(KeyValuePairTypeData typeData)
    {
        /// <summary>
        /// Gets the key type of the KeyValuePair&lt;TKey, TValue&gt; wrapper.
        /// </summary>
        public TypeData KeyType => typeData.TypeParameters![0].Type;

        /// <summary>
        /// Gets the value type of the KeyValuePair&lt;TKey, TValue&gt; wrapper.
        /// </summary>
        public TypeData ValueType => typeData.TypeParameters![1].Type;
    }

    extension(TaskTypeData typeData)
    {
        /// <summary>
        /// Gets the inner service type of the Task&lt;T&gt; wrapper.
        /// </summary>
        public TypeData InnerType => typeData.TypeParameters![0].Type;
    }
}
