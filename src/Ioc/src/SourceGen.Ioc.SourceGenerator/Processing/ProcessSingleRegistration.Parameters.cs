namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Collects closed generic dependencies from a registration's constructor parameters, injection members,
    /// factory method parameters, and closed decorators' constructor parameters and injection members.
    /// </summary>
    private static ImmutableEquatableArray<ClosedGenericDependency> CollectClosedGenericDependenciesFromRegistration(
        RegistrationData registration,
        ImmutableEquatableArray<ServiceRegistrationModel> serviceRegistrations)
    {
        var constructorParams = registration.ImplementationType.ConstructorParameters;
        var injectionMembers = registration.InjectionMembers;
        var factoryParams = registration.Factory?.AdditionalParameters;

        // Check if we have any decorators with constructor parameters or injection members in the service registrations
        var hasDecoratorDependencies = false;
        foreach(var reg in serviceRegistrations)
        {
            foreach(var decorator in reg.Decorators)
            {
                // Check constructor parameters (> 1 because first param is the decorated service)
                if(decorator.ConstructorParameters is { Length: > 1 })
                {
                    hasDecoratorDependencies = true;
                    break;
                }
                // Check injection members
                if(decorator.InjectionMembers is { Length: > 0 })
                {
                    hasDecoratorDependencies = true;
                    break;
                }
            }
            if(hasDecoratorDependencies) break;
        }

        // Early exit if no constructor params, no injection members, no factory params, and no decorator dependencies
        if((constructorParams is null || constructorParams.Length == 0)
            && injectionMembers.Length == 0
            && (factoryParams is null || factoryParams.Length == 0)
            && !hasDecoratorDependencies)
        {
            return [];
        }

        var dependencies = new List<ClosedGenericDependency>();
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);

        // Collect from constructor parameters
        if(constructorParams is not null)
        {
            foreach(var param in constructorParams)
            {
                CollectClosedGenericDependencyFromType(param.Type, dependencies, addedKeys);
            }
        }

        // Collect from injection members (properties, fields, methods with [Inject] attribute)
        foreach(var member in injectionMembers)
        {
            // For properties and fields, check the member type
            if(member.Type is not null)
            {
                CollectClosedGenericDependencyFromType(member.Type, dependencies, addedKeys);
            }

            // For methods, check each parameter type
            if(member.Parameters is not null)
            {
                foreach(var param in member.Parameters)
                {
                    CollectClosedGenericDependencyFromType(param.Type, dependencies, addedKeys);
                }
            }
        }

        // Collect from factory method's additional parameters
        if(factoryParams is not null)
        {
            foreach(var param in factoryParams)
            {
                CollectClosedGenericDependencyFromType(param.Type, dependencies, addedKeys);
            }
        }

        // Collect from closed decorators' constructor parameters and injection members
        // These are decorators that have been closed (type parameters substituted) for specific service types
        foreach(var reg in serviceRegistrations)
        {
            foreach(var decorator in reg.Decorators)
            {
                // Collect from constructor parameters (skip first parameter - it's the decorated service)
                if(decorator.ConstructorParameters is { Length: > 1 })
                {
                    for(int i = 1; i < decorator.ConstructorParameters.Length; i++)
                    {
                        var param = decorator.ConstructorParameters[i];
                        CollectClosedGenericDependencyFromType(param.Type, dependencies, addedKeys);
                    }
                }

                // Collect from injection members (properties, fields, methods with [Inject] attribute)
                if(decorator.InjectionMembers is { Length: > 0 })
                {
                    foreach(var member in decorator.InjectionMembers)
                    {
                        // For properties and fields, check the member type
                        if(member.Type is not null)
                        {
                            CollectClosedGenericDependencyFromType(member.Type, dependencies, addedKeys);
                        }

                        // For methods, check each parameter type
                        if(member.Parameters is not null)
                        {
                            foreach(var param in member.Parameters)
                            {
                                CollectClosedGenericDependencyFromType(param.Type, dependencies, addedKeys);
                            }
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
        // Extract inner types from wrapper types for closed generic dependency discovery.
        // For example, Lazy<IHandler<int>> -> extract IHandler<int> as a dependency.
        var innerType = paramType switch
        {
            LazyTypeData l => l.InstanceType,
            FuncTypeData f => f.ReturnType,
            DictionaryTypeData d => d.ValueType,
            KeyValuePairTypeData k => k.ValueType,
            _ => (TypeData?)null
        };
        if(innerType is not null)
        {
            // Recursively collect from the inner type (handles nested wrappers like Lazy<KVP<K,V>>)
            CollectClosedGenericDependencyFromType(innerType, dependencies, addedKeys);
        }

        // Check if this is any enumerable-compatible type and extract element type for closed generic dependency.
        // Collection wrappers use ElementType directly via CollectionWrapperTypeData;
        // other types check direct generic type name and AllInterfaces for IEnumerable<T> implementation.
        var elementType = paramType switch
        {
            CollectionWrapperTypeData c => c.ElementType,
            _ => paramType.TryGetEnumerableElementType()
        };
        if(elementType is not null)
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
}