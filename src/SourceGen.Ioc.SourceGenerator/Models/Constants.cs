namespace SourceGen.Ioc.SourceGenerator.Models;

internal static class Constants
{
    public const string IoCRegisterAttributeFullName = "SourceGen.Ioc.IoCRegisterAttribute";
    public const string IoCRegisterForAttributeFullName = "SourceGen.Ioc.IoCRegisterForAttribute";
    public const string IoCRegisterDefaultsAttributeFullName = "SourceGen.Ioc.IoCRegisterDefaultsAttribute";
    public const string ImportModuleAttributeFullName = "SourceGen.Ioc.ImportModuleAttribute";

    public const string Category_Usage = "Usage";
    public const string Category_Design = "Design";

    extension(ServiceLifetime lifetime)
    {
        public string Name =>
            lifetime switch
            {
                ServiceLifetime.Singleton => "Singleton",
                ServiceLifetime.Scoped => "Scoped",
                ServiceLifetime.Transient => "Transient",
                _ => lifetime.ToString()
            };
    }

    extension(AttributeData attribute)
    {
        public (bool HasArg, ServiceLifetime Lifetime) TryGetLifetime()
        {
            var (hasArg, val) = attribute.TryGetNamedArgument<int>("Lifetime", 0);// Default is ServiceLifetime.Singleton
            return (hasArg, (ServiceLifetime)val);
        }

        public (bool HasArg, bool Value) TryGetRegisterAllInterfaces() =>
            attribute.TryGetNamedArgument<bool>("RegisterAllInterfaces", false);

        public (bool HasArg, bool Value) TryGetRegisterAllBaseClasses() =>
            attribute.TryGetNamedArgument<bool>("RegisterAllBaseClasses", false);

        public ImmutableEquatableArray<TypeData> GetServiceTypes() =>
            attribute.GetTypeArrayArgument("ServiceTypes");

        public ImmutableEquatableArray<TypeData> GetDecorators() =>
            attribute.GetTypeArrayArgument("Decorators", extractConstructorParams: true);

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
        /// Gets the ExcludeFromDefault value from the attribute.
        /// </summary>
        public bool GetExcludeFromDefault() =>
            attribute.GetNamedArgument<bool>("ExcludeFromDefault", false);

        /// <summary>
        /// Gets the Key and KeyType from the attribute.
        /// </summary>
        /// <returns>A tuple containing the key string and key type.</returns>
        public (string? Key, int KeyType) GetKey()
        {
            var keyType = attribute.GetNamedArgument<int>("KeyType", 0);
            string? key = null;

            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key == "Key")
                {
                    if(namedArg.Value.IsNull)
                    {
                        key = null;
                    }
                    else
                    {
                        if(keyType == 1) // KeyType.Csharp
                        {
                            // Try to get original syntax for nameof() expressions
                            key = attribute.TryGetNameof("Key")
                                ?? namedArg.Value.Value?.ToString();
                        }
                        else
                        {
                            key = namedArg.Value.GetPrimitiveConstantString();
                            keyType = 1; // Treat as CSharp code
                        }
                    }
                    break;
                }
            }

            return (key, keyType);
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

        /// <summary>
        /// Extracts default settings from an IoCRegisterDefaultSettingsAttribute.
        /// </summary>
        /// <returns>The default settings model, or null if the attribute data is invalid.</returns>
        public DefaultSettingsModel? ExtractDefaultSettings()
        {
            if(attribute.ConstructorArguments.Length < 2)
                return null;
            if(attribute.ConstructorArguments[0].Value is not INamedTypeSymbol targetServiceType)
                return null;
            if(attribute.ConstructorArguments[1].Value is not int lifetime)
                return null;

            var (_, registerAllInterfaces) = attribute.TryGetRegisterAllInterfaces();
            var (_, registerAllBaseClasses) = attribute.TryGetRegisterAllBaseClasses();
            var serviceTypes = attribute.GetServiceTypes();
            var typeData = targetServiceType.GetTypeData();
            var decorators = attribute.GetDecorators();
            var tags = attribute.GetTags();
            var excludeFromDefault = attribute.GetExcludeFromDefault();

            return new DefaultSettingsModel(
                typeData,
                (ServiceLifetime)lifetime,
                registerAllInterfaces,
                registerAllBaseClasses,
                serviceTypes,
                decorators,
                tags,
                excludeFromDefault);
        }
    }
}
