namespace SourceGen.Ioc.SourceGenerator.Models;

internal static partial class TransformExtensions
{
    extension(IParameterSymbol param)
    {
        /// <summary>
        /// Gets the service key, injection attribute info, and [ServiceKey]/[FromKeyedServices] attribute from a parameter.
        /// [FromKeyedServices] takes precedence over [Inject] for service key resolution.
        /// HasInjectAttribute is only true for [Inject] attribute (not [FromKeyedServices], which MS.DI handles automatically).
        /// HasServiceKeyAttribute indicates the parameter is marked with [ServiceKey] from Microsoft.Extensions.DependencyInjection.
        /// HasFromKeyedServicesAttribute indicates the parameter is marked with [FromKeyedServices] from Microsoft.Extensions.DependencyInjection.
        /// </summary>
        /// <returns>A tuple containing the service key (if any), whether the parameter has [Inject] attribute, [ServiceKey] attribute, and [FromKeyedServices] attribute.</returns>
        public (string? ServiceKey, bool HasInjectAttribute, bool HasServiceKeyAttribute, bool HasFromKeyedServicesAttribute) GetServiceKeyAndAttributeInfo(SemanticModel? semanticModel = null)
        {
            string? serviceKey = null;
            bool hasInjectAttribute = false;
            bool hasServiceKeyAttribute = false;
            bool hasFromKeyedServicesAttribute = false;

            foreach(var attribute in param.GetAttributes())
            {
                var attrClass = attribute.AttributeClass;
                if(attrClass is null)
                    continue;

                // Check for Microsoft.Extensions.DependencyInjection.ServiceKeyAttribute
                if(attrClass.Name == "ServiceKeyAttribute"
                    && attrClass.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection")
                {
                    hasServiceKeyAttribute = true;
                    continue;
                }

                // Check for Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute (higher priority for key)
                // Note: [FromKeyedServices] is handled by MS.DI automatically, so we don't set hasInjectAttribute
                if(attrClass.Name == "FromKeyedServicesAttribute"
                    && attrClass.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection")
                {
                    hasFromKeyedServicesAttribute = true;
                    // The key is the first constructor argument
                    if(attribute.ConstructorArguments.Length > 0)
                    {
                        var keyArg = attribute.ConstructorArguments[0];
                        if(!keyArg.IsNull && keyArg.Value is not null)
                        {
                            serviceKey = keyArg.GetPrimitiveConstantString();
                        }
                    }

                    // [FromKeyedServices] found, but continue to check for [Inject] as well
                    continue;
                }

                // Check for IocInjectAttribute/InjectAttribute (by name only, to support third-party attributes)
                if(attrClass.IsInject)
                {
                    hasInjectAttribute = true;
                    // Only use [Inject] key if no [FromKeyedServices] key was found
                    if(serviceKey is null)
                    {
                        var (key, _, _) = attribute.GetKeyInfo(semanticModel);
                        serviceKey = key;
                    }
                }
            }

            return (serviceKey, hasInjectAttribute, hasServiceKeyAttribute, hasFromKeyedServicesAttribute);
        }
    }
}