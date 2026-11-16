namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Collects closed generic dependencies from how a registration creates its instances (see Register.Generics.spec.md):
    /// the constructor parameters and injection members of a constructed implementation type, the parameters of the
    /// factory (closed for each registered type a generic factory's mapping matches), and none for an instance, and from the
    /// closed decorators' constructor parameters and injection members.
    /// </summary>
    /// <param name="factory">The factory of the registration, its own or of its default settings; null for an instance.</param>
    private static ImmutableEquatableArray<ClosedGenericDependency> CollectClosedGenericDependenciesFromRegistration(
        RegistrationData registration,
        FactoryMethodData? factory,
        ImmutableEquatableArray<ServiceRegistrationModel> serviceRegistrations)
    {
        // An instance is neither constructed nor decorated
        if(registration.Instance is not null)
        {
            return [];
        }

        // Created with the first dependency: most registrations have none, and this runs for every registration
        List<ClosedGenericDependency>? dependencies = null;
        HashSet<(string TypeName, string? Key, DependencyKeyKind KeyKind)>? addedKeys = null;

        // The implementation type is constructed without a factory, or with a generic factory its mapping does not match
        var implementationType = registration.ImplementationType;
        var isClosedImplementation = implementationType is not GenericTypeData { IsOpenGeneric: true };
        var isConstructed = factory is null
            || (factory.TypeParameterCount > 0
                && implementationType.Constructibility is Constructibility.Activatable or Constructibility.GeneratedCodeOnly
                && (!isClosedImplementation || MapGenericFactoryTypeArguments(factory, implementationType) is null));

        if(isConstructed)
        {
            foreach(var param in implementationType.ConstructorParameters ?? [])
            {
                CollectClosedGenericDependencyFromParameter(param, method: null, ref dependencies, ref addedKeys);
            }

            CollectClosedGenericDependenciesFromInjectionMembers(registration.InjectionMembers, ref dependencies, ref addedKeys);
        }

        // The factory of each registered type; those of a generic factory are closed for the type they create. The factory of
        // an open generic registration keeps its type parameters, which are skipped like the constructor's.
        if(factory is not null)
        {
            if(isClosedImplementation)
            {
                foreach(var reg in serviceRegistrations)
                {
                    foreach(var param in reg.Factory?.AdditionalParameters ?? [])
                    {
                        CollectClosedGenericDependencyFromParameter(param, method: null, ref dependencies, ref addedKeys);
                    }
                }
            }
            else
            {
                foreach(var param in factory.AdditionalParameters)
                {
                    CollectClosedGenericDependencyFromParameter(param, method: null, ref dependencies, ref addedKeys);
                }
            }
        }

        // Collect from closed decorators' constructor parameters and injection members
        // These are decorators that have been closed (type parameters substituted) for specific service types.
        // Decorators of an open generic registration are closed with its own type parameters, so their dependencies are
        // still open; their closed forms are collected when closed generic registrations are generated.
        foreach(var reg in serviceRegistrations)
        {
            if(reg.IsOpenGeneric)
                continue;

            foreach(var decorator in reg.Decorators)
            {
                // Collect from constructor parameters, except the one that receives the decorated instance
                foreach(var param in EnumerateDecoratorDependencyParameters(decorator, reg.ServiceType))
                {
                    CollectClosedGenericDependencyFromParameter(param, method: null, ref dependencies, ref addedKeys);
                }

                // Collect from injection members (properties, fields, methods with [Inject] attribute)
                CollectClosedGenericDependenciesFromInjectionMembers(decorator.InjectionMembers, ref dependencies, ref addedKeys);
            }
        }

        return dependencies?.ToImmutableEquatableArray() ?? [];
    }

    /// <summary>
    /// Collects closed generic dependencies from injected property and field types and injected method parameters.
    /// </summary>
    private static void CollectClosedGenericDependenciesFromInjectionMembers(
        ImmutableEquatableArray<InjectionMemberData>? injectionMembers,
        ref List<ClosedGenericDependency>? dependencies,
        ref HashSet<(string TypeName, string? Key, DependencyKeyKind KeyKind)>? addedKeys)
    {
        foreach(var member in injectionMembers ?? [])
        {
            switch(member)
            {
                // For properties and fields, check the member type
                case PropertyOrFieldInjectionData propertyOrField:
                    CollectClosedGenericDependencyFromType(
                        propertyOrField.Type,
                        propertyOrField.Key,
                        ref dependencies,
                        ref addedKeys,
                        ClosedGenericDependency.GetKeyKind(propertyOrField.Key, propertyOrField.IsCsharpKey));
                    break;

                // For methods, check each parameter type
                case MethodInjectionData method:
                    foreach(var param in method.Parameters)
                    {
                        CollectClosedGenericDependencyFromParameter(param, method, ref dependencies, ref addedKeys);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Collects closed generic dependencies from a parameter, resolved with its own key or, without one, the key of its
    /// injected method.
    /// </summary>
    private static void CollectClosedGenericDependencyFromParameter(
        ParameterData param,
        MethodInjectionData? method,
        ref List<ClosedGenericDependency>? dependencies,
        ref HashSet<(string TypeName, string? Key, DependencyKeyKind KeyKind)>? addedKeys)
    {
        var (key, keyKind) = GetParameterKey(param, method);
        CollectClosedGenericDependencyFromType(param.Type, key, ref dependencies, ref addedKeys, keyKind);
    }

    /// <summary>
    /// Gets the key a parameter is resolved with: its own key or, without one, the key of its injected method, and how it
    /// is resolved with it.
    /// </summary>
    private static (string? Key, DependencyKeyKind KeyKind) GetParameterKey(ParameterData param, InjectionMemberData? method)
        => param.ServiceKey is not null || method is null
            ? (param.ServiceKey, ClosedGenericDependency.GetKeyKind(param.ServiceKey, param.IsCsharpKey))
            : (method.Key, ClosedGenericDependency.GetKeyKind(method.Key, method.IsCsharpKey));

    /// <summary>
    /// Collects closed generic dependency from a type and adds it to the dependencies list.
    /// </summary>
    /// <param name="paramType">The type to check for closed generic dependencies.</param>
    /// <param name="key">
    /// The key the type is resolved with, or <see langword="null"/>; wrappers resolve their inner service with the same key,
    /// except dictionaries and collections of key value pairs, which resolve every keyed registration of their value type.
    /// </param>
    /// <param name="dependencies">The list to add dependencies to, created with the first dependency.</param>
    /// <param name="addedKeys">Set of already added dependency identities to avoid duplicates, created with the first dependency.</param>
    /// <param name="keyKind">How the type is resolved with <paramref name="key"/> (see <see cref="DependencyKeyKind"/>).</param>
    /// <param name="isPairOfEveryKey">
    /// Whether a key value pair type is one of the pairs of every keyed registration, as the element of a collection, rather
    /// than a single pair resolved with <paramref name="key"/>.
    /// </param>
    private static void CollectClosedGenericDependencyFromType(
        TypeData paramType,
        string? key,
        ref List<ClosedGenericDependency>? dependencies,
        ref HashSet<(string TypeName, string? Key, DependencyKeyKind KeyKind)>? addedKeys,
        DependencyKeyKind keyKind = DependencyKeyKind.Value,
        bool isPairOfEveryKey = false)
    {
        if(GetWrapperInnerType(paramType) is { } innerType)
        {
            // Recursively collect from the inner type (handles nested wrappers like Lazy<KVP<K,V>>)
            CollectClosedGenericDependencyFromType(innerType, key, ref dependencies, ref addedKeys, GetInnerKeyKind(paramType, keyKind, isPairOfEveryKey));
        }

        if(GetEnumerableElementType(paramType) is { } elementType)
        {
            // Recursively collect from the element type (handles nested wrappers like IEnumerable<Lazy<IHandler<T>>>)
            CollectClosedGenericDependencyFromType(elementType, key, ref dependencies, ref addedKeys, keyKind, isPairOfEveryKey: true);
        }

        // Check if this is a closed generic type (has generic arguments but is not open generic)
        if(paramType is GenericTypeData { GenericArity: > 0, IsOpenGeneric: false, IsNestedOpenGeneric: false } genericParamType)
        {
            // Add the original type as a dependency (skip arrays as they don't need registration)
            var dependencyKey = keyKind >= DependencyKeyKind.Unknown ? null : key;
            if(!paramType.IsArrayType && (addedKeys ??= []).Add((paramType.Name, dependencyKey, keyKind)))
            {
                (dependencies ??= []).Add(new ClosedGenericDependency(
                    paramType.Name,
                    paramType,
                    genericParamType.NameWithoutGeneric,
                    dependencyKey,
                    keyKind));
            }
        }
    }

    /// <summary>
    /// Gets how the inner type of a wrapper is resolved: the value type of a dictionary, or of a key value pair that is the
    /// element of a collection, with every key, and the inner type of any other wrapper, such as a single key value pair
    /// (<c>new KeyValuePair&lt;K, V&gt;(key, Resolve&lt;V&gt;(key))</c>), like the wrapper.
    /// </summary>
    /// <param name="isPairOfEveryKey">Whether a key value pair is the element of a collection.</param>
    private static DependencyKeyKind GetInnerKeyKind(TypeData wrapperType, DependencyKeyKind keyKind, bool isPairOfEveryKey)
        => wrapperType is DictionaryTypeData || (isPairOfEveryKey && wrapperType is KeyValuePairTypeData)
            ? DependencyKeyKind.AllKeyed
            : keyKind;

    /// <summary>
    /// Gets the type a wrapper resolves for closed generic dependency discovery, such as <c>IHandler&lt;int&gt;</c> for
    /// <c>Lazy&lt;IHandler&lt;int&gt;&gt;</c>.
    /// </summary>
    private static TypeData? GetWrapperInnerType(TypeData type) => type switch
    {
        LazyTypeData l => l.InstanceType,
        FuncTypeData f => f.ReturnType,
        DictionaryTypeData d => d.ValueType,
        KeyValuePairTypeData k => k.ValueType,
        _ => null
    };

    /// <summary>
    /// Gets the element type of an enumerable-compatible type for closed generic dependency discovery.
    /// </summary>
    /// <remarks>
    /// Collection wrappers use their element type; other types check their own generic type name and their interfaces
    /// for an <c>IEnumerable&lt;T&gt;</c> implementation.
    /// </remarks>
    private static TypeData? GetEnumerableElementType(TypeData type) => type switch
    {
        CollectionWrapperTypeData c => c.ElementType,
        _ => type.TryGetEnumerableElementType()
    };
}