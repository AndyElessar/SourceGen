namespace SourceGen.Ioc.SourceGenerator.Models;

internal static partial class TransformExtensions
{
    extension(AttributeData attribute)
    {
        /// <summary>
        /// Gets an array of type symbols from a named argument.
        /// </summary>
        /// <param name="name">The name of the named argument.</param>
        /// <param name="extractConstructorParams">Whether to extract constructor parameters.</param>
        /// <param name="extractInjectionMembers">Whether to extract injection members (for decorators).</param>
        public ImmutableEquatableArray<TypeData> GetTypeArrayArgument(
            string name,
            bool extractConstructorParams = false,
            bool extractInjectionMembers = false)
        {
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key.Equals(name, StringComparison.Ordinal) && !namedArg.Value.IsNull && namedArg.Value.Kind == TypedConstantKind.Array)
                {
                    List<TypeData> result = [];
                    foreach(var value in namedArg.Value.Values)
                    {
                        if(value.Value is INamedTypeSymbol namedTypeSymbol)
                        {
                            result.Add(namedTypeSymbol.GetTypeData(
                                extractConstructorParams,
                                extractHierarchy: false,
                                visited: null,
                                semanticModel: null,
                                extractInjectionMembers));
                        }
                        else if(value.Value is ITypeSymbol typeSymbol)
                        {
                            result.Add(typeSymbol.GetTypeData(extractConstructorParams));
                        }
                    }
                    return result.ToImmutableEquatableArray();
                }
            }

            return [];
        }

        /// <summary>
        /// Gets an array of type symbols from an attribute constructor argument of type <c>params Type[]</c>.
        /// This is the constructor-argument counterpart to <see cref="GetTypeArrayArgument"/>, and is used when
        /// service types are supplied positionally to the attribute constructor instead of via a named <c>Type[]</c> argument.
        /// </summary>
        /// <remarks>
        /// This method scans the attribute's constructor arguments for an array (or <c>params</c>) argument that contains
        /// type values (for example, <c>params Type[] serviceTypes</c>) and converts those <see cref="ITypeSymbol"/> instances
        /// to <see cref="TypeData"/>. It skips non-type arguments, such as <c>ServiceLifetime</c> enum values.
        /// </remarks>
        public ImmutableEquatableArray<TypeData> GetTypeArrayFromConstructorArgument(bool extractConstructorParams = false)
        {
            foreach(var ctorArg in attribute.ConstructorArguments)
            {
                // Look for an array argument containing type values
                if(ctorArg.Kind == TypedConstantKind.Array && !ctorArg.IsNull)
                {
                    List<TypeData> result = [];
                    foreach(var value in ctorArg.Values)
                    {
                        if(value.Value is ITypeSymbol typeSymbol)
                        {
                            result.Add(typeSymbol.GetTypeData(extractConstructorParams));
                        }
                    }

                    // Only return if we found type values
                    if(result.Count > 0)
                        return result.ToImmutableEquatableArray();
                }
            }

            return [];
        }

        public (bool HasArg, ServiceLifetime Lifetime) TryGetLifetime()
        {
            // First, check if lifetime is passed as a constructor argument (for generic attributes like IoCRegisterAttribute<T>(ServiceLifetime.Scoped))
            foreach(var ctorArg in attribute.ConstructorArguments)
            {
                if(ctorArg.Type?.Name == nameof(ServiceLifetime) && ctorArg.Value is int lifetimeValue)
                {
                    return (true, (ServiceLifetime)lifetimeValue);
                }
            }

            // Fall back to named argument
            var (hasArg, val) = attribute.TryGetNamedArgument<int>("Lifetime", 2); // Default is ServiceLifetime.Transient
            return (hasArg, (ServiceLifetime)val);
        }

        public (bool HasArg, bool Value) TryGetRegisterAllInterfaces() =>
            attribute.TryGetNamedArgument<bool>("RegisterAllInterfaces", false);

        public (bool HasArg, bool Value) TryGetRegisterAllBaseClasses() =>
            attribute.TryGetNamedArgument<bool>("RegisterAllBaseClasses", false);

        /// <summary>
        /// Gets the service types from the attribute.
        /// This method checks both named arguments and constructor arguments for service types.
        /// </summary>
        /// <remarks>
        /// The method first checks for a named argument "ServiceTypes" (e.g., ServiceTypes = [typeof(IService)]).
        /// If not found, it checks constructor arguments for an array of types (e.g., params Type[] serviceTypes).
        /// </remarks>
        public IEnumerable<INamedTypeSymbol> GetServiceTypeSymbols()
        {
            // First, try to get from named argument
            var namedResult = attribute.GetTypeSymbolsFromNamedArgument("ServiceTypes");
            if(namedResult.Length > 0)
                return namedResult;

            // Fall back to constructor argument (params Type[] serviceTypes)
            foreach(var ctorArg in attribute.ConstructorArguments)
            {
                if(ctorArg.Kind == TypedConstantKind.Array && !ctorArg.IsNull)
                {
                    List<INamedTypeSymbol> result = [];
                    foreach(var value in ctorArg.Values)
                    {
                        if(value.Value is INamedTypeSymbol namedTypeSymbol)
                        {
                            result.Add(namedTypeSymbol);
                        }
                    }

                    if(result.Count > 0)
                        return result;
                }
            }

            return [];
        }

        public ImmutableEquatableArray<TypeData> GetServiceTypes()
        {
            List<TypeData> result = [];
            foreach(var serviceTypeSymbol in attribute.GetServiceTypeSymbols())
            {
                result.Add(serviceTypeSymbol.GetTypeData());
            }

            return result.ToImmutableEquatableArray();
        }

        /// <summary>
        /// Gets the service types from generic attribute type parameters (e.g., IoCRegisterAttribute&lt;T1, T2&gt;).
        /// </summary>
        public IEnumerable<INamedTypeSymbol> GetServiceTypeSymbolsFromGenericAttribute()
        {
            var attrClass = attribute.AttributeClass;
            if(attrClass?.IsGenericType != true || attrClass.TypeArguments.Length == 0)
                return [];

            List<INamedTypeSymbol> result = [];
            foreach(var typeArg in attrClass.TypeArguments)
            {
                if(typeArg is INamedTypeSymbol namedType)
                {
                    result.Add(namedType);
                }
            }

            return result;
        }

        public ImmutableEquatableArray<TypeData> GetServiceTypesFromGenericAttribute()
        {
            List<TypeData> result = [];
            foreach(var serviceTypeSymbol in attribute.GetServiceTypeSymbolsFromGenericAttribute())
            {
                result.Add(serviceTypeSymbol.GetTypeData());
            }

            return result.ToImmutableEquatableArray();
        }

        public INamedTypeSymbol? GetImportedModuleType()
        {
            var attributeClass = attribute.AttributeClass;
            if(attributeClass is null)
                return null;

            if(attributeClass.IsGenericType)
            {
                if(attributeClass.TypeArguments.Length == 0)
                    return null;

                return attributeClass.TypeArguments[0] as INamedTypeSymbol;
            }

            if(attribute.ConstructorArguments.Length == 0)
                return null;

            return attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
        }

        public ImmutableEquatableArray<TypeData> GetDecorators() =>
            attribute.GetTypeArrayArgument("Decorators", extractConstructorParams: true, extractInjectionMembers: true);

        /// <summary>
        /// Gets the ImplementationTypes array from the attribute.
        /// Extracts implementation types with constructor parameters and hierarchy information,
        /// using the same parsing logic as IocRegisterAttribute.
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetImplementationTypes() =>
            attribute.GetTypeArrayArgumentWithHierarchy("ImplementationTypes");

        /// <summary>
        /// Gets the ImplementationTypes array as INamedTypeSymbol from the attribute.
        /// Used when full symbol access is needed for injection member extraction.
        /// </summary>
        public ImmutableEquatableArray<INamedTypeSymbol> GetImplementationTypeSymbols() =>
            attribute.GetTypeSymbolsFromNamedArgument("ImplementationTypes");

        /// <summary>
        /// Gets an array of type symbols from a named argument.
        /// Used when full symbol access is needed for further analysis.
        /// </summary>
        public ImmutableEquatableArray<INamedTypeSymbol> GetTypeSymbolsFromNamedArgument(string name)
        {
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key.Equals(name, StringComparison.Ordinal) && !namedArg.Value.IsNull && namedArg.Value.Kind == TypedConstantKind.Array)
                {
                    List<INamedTypeSymbol> result = [];
                    foreach(var value in namedArg.Value.Values)
                    {
                        if(value.Value is INamedTypeSymbol namedTypeSymbol)
                        {
                            result.Add(namedTypeSymbol);
                        }
                    }
                    return result.ToImmutableEquatableArray();
                }
            }

            return [];
        }

        /// <summary>
        /// Gets an array of type symbols from a named argument with full hierarchy extraction.
        /// Used for ImplementationTypes where we need constructor params and all interfaces/base classes.
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetTypeArrayArgumentWithHierarchy(string name)
        {
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key.Equals(name, StringComparison.Ordinal) && !namedArg.Value.IsNull && namedArg.Value.Kind == TypedConstantKind.Array)
                {
                    List<TypeData> result = [];
                    foreach(var value in namedArg.Value.Values)
                    {
                        if(value.Value is INamedTypeSymbol namedTypeSymbol)
                        {
                            // Extract with constructor params and hierarchy, same as IocRegisterAttribute
                            result.Add(namedTypeSymbol.GetTypeData(extractConstructorParams: true, extractHierarchy: true));
                        }
                    }
                    return result.ToImmutableEquatableArray();
                }
            }

            return [];
        }

        /// <summary>
        /// Gets the Tags array from the attribute.
        /// </summary>
        public ImmutableEquatableArray<string> GetTags()
        {
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key.Equals("Tags", StringComparison.Ordinal) && !namedArg.Value.IsNull && namedArg.Value.Kind == TypedConstantKind.Array)
                {
                    List<string> result = [];
                    foreach(var value in namedArg.Value.Values)
                    {
                        if(value.Value is string tag)
                        {
                            result.Add(tag);
                        }
                    }
                    return result.ToImmutableEquatableArray();
                }
            }

            return [];
        }

        /// <summary>
        /// Checks if the attribute has Factory or Instance specified.
        /// </summary>
        /// <returns>A tuple indicating whether Factory and/or Instance are specified.</returns>
        public (bool HasFactory, bool HasInstance) HasFactoryOrInstance()
        {
            bool hasFactory = false;
            bool hasInstance = false;

            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key == "Factory" && !namedArg.Value.IsNull)
                {
                    hasFactory = true;
                }
                else if(namedArg.Key == "Instance" && !namedArg.Value.IsNull)
                {
                    hasInstance = true;
                }

                // Early exit if both found
                if(hasFactory && hasInstance)
                    break;
            }

            return (hasFactory, hasInstance);
        }

        /// <summary>
        /// Gets the target type from an IoCRegisterForAttribute.
        /// For non-generic variant, extracts from constructor argument.
        /// For generic variant (IoCRegisterForAttribute&lt;T&gt;), extracts from type parameter.
        /// </summary>
        /// <returns>The target type symbol, or null if not found.</returns>
        public INamedTypeSymbol? GetTargetTypeFromRegisterForAttribute()
        {
            var attributeClass = attribute.AttributeClass;
            if(attributeClass is null)
                return null;

            // For generic IoCRegisterForAttribute<T>, get T from type arguments
            if(attributeClass.IsGenericType && attributeClass.TypeArguments.Length > 0)
            {
                return attributeClass.TypeArguments[0] as INamedTypeSymbol;
            }

            // For non-generic IoCRegisterForAttribute, get from constructor argument
            if(attribute.ConstructorArguments.Length > 0 &&
               attribute.ConstructorArguments[0].Value is INamedTypeSymbol targetType)
            {
                return targetType;
            }

            return null;
        }

        /// <summary>
        /// Gets the Instance path from the attribute.
        /// </summary>
        /// <param name="semanticModel">Optional semantic model to resolve full access paths for nameof() expressions.</param>
        /// <returns>The static instance path (e.g., "MyService.Default"), or null if not specified.</returns>
        public string? GetInstance(SemanticModel? semanticModel = null)
        {
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key == "Instance")
                {
                    if(namedArg.Value.IsNull)
                        return null;

                    // Try to get original syntax for nameof() expressions with full access path resolution
                    return attribute.TryGetNameof("Instance", semanticModel)
                        ?? namedArg.Value.Value?.ToString();
                }
            }

            return null;
        }

        /// <summary>
        /// Determines if the attribute will cause registration of interfaces or base classes.
        /// For open generic types, nested open generics are only a problem when registering interfaces/base classes.
        /// </summary>
        public bool WillRegisterInterfacesOrBaseClasses()
        {
            // Check if ServiceTypes is specified
            var serviceTypes = attribute.GetServiceTypes();
            if(serviceTypes.Length > 0)
                return true;

            // Check if RegisterAllInterfaces is true
            var (hasRegisterAllInterfaces, registerAllInterfaces) = attribute.TryGetRegisterAllInterfaces();
            if(hasRegisterAllInterfaces && registerAllInterfaces)
                return true;

            // Check if RegisterAllBaseClasses is true
            var (hasRegisterAllBaseClasses, registerAllBaseClasses) = attribute.TryGetRegisterAllBaseClasses();
            if(hasRegisterAllBaseClasses && registerAllBaseClasses)
                return true;

            // Only registering self, no interfaces/base classes
            return false;
        }
    }
}