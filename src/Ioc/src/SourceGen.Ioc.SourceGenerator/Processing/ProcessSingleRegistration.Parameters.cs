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

        var dependencies = new List<ClosedGenericDependency>();
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);

        // The implementation type is constructed without a factory, or with a generic factory its mapping does not match
        var implementationType = registration.ImplementationType;
        var isClosedImplementation = implementationType is not GenericTypeData { IsOpenGeneric: true };
        var isConstructed = factory is null
            || (factory.TypeParameterCount > 0 && !implementationType.IsNotConstructible
                && (!isClosedImplementation || MapGenericFactoryTypeArguments(factory, implementationType) is null));

        if(isConstructed)
        {
            foreach(var param in implementationType.ConstructorParameters ?? [])
            {
                CollectClosedGenericDependencyFromType(param.Type, dependencies, addedKeys);
            }

            // Collect from injection members (properties, fields, methods with [Inject] attribute)
            foreach(var member in registration.InjectionMembers)
            {
                switch(member)
                {
                    // For properties and fields, check the member type
                    case PropertyOrFieldInjectionData propertyOrField:
                        CollectClosedGenericDependencyFromType(propertyOrField.Type, dependencies, addedKeys);
                        break;

                    // For methods, check each parameter type
                    case MethodInjectionData method:
                        foreach(var param in method.Parameters)
                        {
                            CollectClosedGenericDependencyFromType(param.Type, dependencies, addedKeys);
                        }
                        break;
                }
            }
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
                        CollectClosedGenericDependencyFromType(param.Type, dependencies, addedKeys);
                    }
                }
            }
            else
            {
                foreach(var param in factory.AdditionalParameters)
                {
                    CollectClosedGenericDependencyFromType(param.Type, dependencies, addedKeys);
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
                    CollectClosedGenericDependencyFromType(param.Type, dependencies, addedKeys);
                }

                // Collect from injection members (properties, fields, methods with [Inject] attribute)
                if(decorator.InjectionMembers is { Length: > 0 })
                {
                    foreach(var member in decorator.InjectionMembers)
                    {
                        switch(member)
                        {
                            // For properties and fields, check the member type
                            case PropertyOrFieldInjectionData propertyOrField:
                                CollectClosedGenericDependencyFromType(propertyOrField.Type, dependencies, addedKeys);
                                break;

                            // For methods, check each parameter type
                            case MethodInjectionData method:
                                foreach(var param in method.Parameters)
                                {
                                    CollectClosedGenericDependencyFromType(param.Type, dependencies, addedKeys);
                                }
                                break;
                        }
                    }
                }
            }
        }

        return dependencies.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Collects closed generic dependency from a type and adds it to the dependencies list.
    /// </summary>
    /// <param name="paramType">The type to check for closed generic dependencies.</param>
    /// <param name="dependencies">The list to add dependencies to.</param>
    /// <param name="addedKeys">Set of already added dependency keys to avoid duplicates.</param>
    private static void CollectClosedGenericDependencyFromType(
        TypeData paramType,
        List<ClosedGenericDependency> dependencies,
        HashSet<string> addedKeys)
    {
        if(GetWrapperInnerType(paramType) is { } innerType)
        {
            // Recursively collect from the inner type (handles nested wrappers like Lazy<KVP<K,V>>)
            CollectClosedGenericDependencyFromType(innerType, dependencies, addedKeys);
        }

        if(GetEnumerableElementType(paramType) is { } elementType)
        {
            // Recursively collect from the element type (handles nested wrappers like IEnumerable<Lazy<IHandler<T>>>)
            CollectClosedGenericDependencyFromType(elementType, dependencies, addedKeys);
        }

        // Check if this is a closed generic type (has generic arguments but is not open generic)
        if(paramType is GenericTypeData { GenericArity: > 0, IsOpenGeneric: false, IsNestedOpenGeneric: false } genericParamType)
        {
            // Add the original type as a dependency (skip arrays as they don't need registration)
            if(!paramType.IsArrayType && addedKeys.Add(paramType.Name))
            {
                dependencies.Add(new ClosedGenericDependency(
                    paramType.Name,
                    paramType,
                    genericParamType.NameWithoutGeneric));
            }
        }
    }

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