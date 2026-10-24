namespace SourceGen.Ioc.SourceGenerator.Models;

internal static partial class TransformExtensions
{
    extension(AttributeData attribute)
    {
        /// <summary>
        /// Extracts default settings from an IoCRegisterDefaultSettingsAttribute.
        /// </summary>
        /// <param name="semanticModel">Optional semantic model for resolving Factory method data.</param>
        /// <returns>The default settings model, or null if the attribute data is invalid.</returns>
        public DefaultSettingsModel? ExtractDefaultSettings(SemanticModel? semanticModel = null)
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

            // Get factory method data if semantic model is provided
            FactoryMethodData? factory = null;
            if(semanticModel is not null)
            {
                factory = attribute.GetFactoryMethodData(semanticModel);
            }

            // Get implementation types with constructor params and hierarchy (same as IocRegisterAttribute)
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
        /// <param name="semanticModel">Optional semantic model for resolving Factory method data.</param>
        /// <returns>The default settings model, or null if the attribute data is invalid.</returns>
        public DefaultSettingsModel? ExtractDefaultSettingsFromGenericAttribute(SemanticModel? semanticModel = null)
        {
            var attrClass = attribute.AttributeClass;
            if(attrClass?.IsGenericType != true || attrClass.TypeArguments.Length == 0)
                return null;

            if(attrClass.TypeArguments[0] is not INamedTypeSymbol targetServiceType)
                return null;

            // Lifetime is the first constructor argument for the generic version
            if(attribute.ConstructorArguments.Length < 1)
                return null;
            if(attribute.ConstructorArguments[0].Value is not int lifetime)
                return null;

            var (_, registerAllInterfaces) = attribute.TryGetRegisterAllInterfaces();
            var (_, registerAllBaseClasses) = attribute.TryGetRegisterAllBaseClasses();
            var serviceTypes = attribute.GetServiceTypes();
            var typeData = targetServiceType.GetTypeData();
            var decorators = attribute.GetDecorators();
            var tags = attribute.GetTags();

            // Get factory method data if semantic model is provided
            FactoryMethodData? factory = null;
            if(semanticModel is not null)
            {
                factory = attribute.GetFactoryMethodData(semanticModel);
            }

            // Get implementation types with constructor params and hierarchy (same as IocRegisterAttribute)
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
    }
}