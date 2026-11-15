namespace SourceGen.Ioc;

internal static partial class TransformExtensions
{
    extension(AttributeData attribute)
    {
        /// <summary>
        /// Extracts default settings from an IoCRegisterDefaultSettingsAttribute.
        /// </summary>
        /// <param name="constructedIn">The assembly the generated code is compiled into, which selects the decorator constructors it can call.</param>
        /// <param name="semanticModel">Optional semantic model for resolving Factory method data.</param>
        /// <returns>The default settings model, or null if the attribute data is invalid.</returns>
        public DefaultSettingsModel? ExtractDefaultSettings(IAssemblySymbol constructedIn, SemanticModel? semanticModel = null)
        {
            if(attribute.ConstructorArguments.Length < 2)
                return null;
            // Settings for an open type nested in a generic type are ignored as a whole, with their ImplementationTypes (SGIOC038)
            if(attribute.ConstructorArguments[0].Value is not INamedTypeSymbol { IsOpenNestedInGenericType: false } targetServiceType)
                return null;
            if(attribute.ConstructorArguments[1].Value is not int lifetime)
                return null;

            // Settings whose Factory is given with nameof() of a member of an unbound generic type are ignored as a whole (SGIOC041)
            if(semanticModel is not null && attribute.HasNameofOfUnboundGenericType(semanticModel))
                return null;

            var (_, registerAllInterfaces) = attribute.TryGetRegisterAllInterfaces();
            var (_, registerAllBaseClasses) = attribute.TryGetRegisterAllBaseClasses();
            var serviceTypes = attribute.GetServiceTypes();
            var typeData = targetServiceType.GetTypeData();
            var decorators = attribute.GetDecorators(constructedIn, semanticModel);
            var tags = attribute.GetTags();

            // Get factory method data if semantic model is provided
            FactoryMethodData? factory = null;
            if(semanticModel is not null)
            {
                factory = attribute.GetFactoryMethodData(semanticModel);
            }

            // The implementation types by name; their registrations are created from the symbols
            var implementationTypes = attribute.GetImplementationTypes();

            return new DefaultSettingsModel(
                typeData,
                (ServiceLifetime)lifetime,
                registerAllInterfaces,
                registerAllBaseClasses,
                serviceTypes,
                decorators,
                tags,
                factory,
                implementationTypes);
        }

        /// <summary>
        /// Extracts default settings from a generic IoCRegisterDefaultsAttribute (e.g., IoCRegisterDefaultsAttribute&lt;T&gt;).
        /// The target service type is specified via type parameter instead of constructor argument.
        /// </summary>
        /// <param name="constructedIn">The assembly the generated code is compiled into, which selects the decorator constructors it can call.</param>
        /// <param name="semanticModel">Optional semantic model for resolving Factory method data.</param>
        /// <returns>The default settings model, or null if the attribute data is invalid.</returns>
        public DefaultSettingsModel? ExtractDefaultSettingsFromGenericAttribute(IAssemblySymbol constructedIn, SemanticModel? semanticModel = null)
        {
            var attrClass = attribute.AttributeClass;
            if(attrClass?.IsGenericType != true || attrClass.TypeArguments.Length == 0)
                return null;

            // Settings for an open type nested in a generic type are ignored as a whole, with their ImplementationTypes (SGIOC038)
            if(attrClass.TypeArguments[0] is not INamedTypeSymbol { IsOpenNestedInGenericType: false } targetServiceType)
                return null;

            // Lifetime is the first constructor argument for the generic version
            if(attribute.ConstructorArguments.Length < 1)
                return null;
            if(attribute.ConstructorArguments[0].Value is not int lifetime)
                return null;

            // Settings whose Factory is given with nameof() of a member of an unbound generic type are ignored as a whole (SGIOC041)
            if(semanticModel is not null && attribute.HasNameofOfUnboundGenericType(semanticModel))
                return null;

            var (_, registerAllInterfaces) = attribute.TryGetRegisterAllInterfaces();
            var (_, registerAllBaseClasses) = attribute.TryGetRegisterAllBaseClasses();
            var serviceTypes = attribute.GetServiceTypes();
            var typeData = targetServiceType.GetTypeData();
            var decorators = attribute.GetDecorators(constructedIn, semanticModel);
            var tags = attribute.GetTags();

            // Get factory method data if semantic model is provided
            FactoryMethodData? factory = null;
            if(semanticModel is not null)
            {
                factory = attribute.GetFactoryMethodData(semanticModel);
            }

            // The implementation types by name; their registrations are created from the symbols
            var implementationTypes = attribute.GetImplementationTypes();

            return new DefaultSettingsModel(
                typeData,
                (ServiceLifetime)lifetime,
                registerAllInterfaces,
                registerAllBaseClasses,
                serviceTypes,
                decorators,
                tags,
                factory,
                implementationTypes);
        }

        /// <summary>
        /// Gets whether the <c>Factory</c> of default settings is given with <c>nameof()</c> of a member of an unbound generic
        /// type, such as <c>nameof(Factories&lt;&gt;.Create)</c>. Default settings apply to other types, so its type arguments
        /// cannot be determined (SGIOC041).
        /// </summary>
        public bool HasNameofOfUnboundGenericType(SemanticModel semanticModel)
            => attribute.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax attributeSyntax
                && FindNameofOfUnboundGenericType(attributeSyntax, implementation: null, isCsharpKey: false, semanticModel) is not null;
    }
}