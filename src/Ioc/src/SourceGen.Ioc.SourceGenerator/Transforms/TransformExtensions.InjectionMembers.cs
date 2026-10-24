namespace SourceGen.Ioc.SourceGenerator.Models;

internal static partial class TransformExtensions
{
    extension(INamedTypeSymbol typeSymbol)
    {
        public bool IsInject =>
            typeSymbol.Name is "IocInjectAttribute" or "InjectAttribute";

        /// <summary>
        /// Enumerates members (properties, fields, methods) marked with IocInjectAttribute/InjectAttribute.
        /// This is a shared method used by both Analyzer (ServiceInfo) and Generator (RegistrationData).
        /// </summary>
        /// <remarks>
        /// The method filters members based on:
        /// - Non-static members only
        /// - Properties with a setter
        /// - Non-readonly fields
        /// - Ordinary methods that return <see langword="void"/> (sync) or non-generic
        ///   <see cref="System.Threading.Tasks.Task"/> (async, when <c>AsyncMethodInject</c>
        ///   feature is enabled), and are not generic
        /// </remarks>
        /// <returns>
        /// An enumerable of tuples containing the member symbol and its inject attribute.
        /// Analyzer can use ISymbol directly; Generator can convert to InjectionMemberData.
        /// </returns>
        public IEnumerable<(ISymbol Member, AttributeData InjectAttribute)> GetInjectedMembers()
        {
            // For unbound generic types (e.g., LoggingDecorator<,>), we need to use OriginalDefinition
            // to get the actual member declarations with their attributes
            var typeToInspect = typeSymbol.IsUnboundGenericType ? typeSymbol.OriginalDefinition : typeSymbol;

            foreach(var member in typeToInspect.GetMembers())
            {
                // Skip static members
                if(member.IsStatic)
                    continue;

                // Check if the member has IocInjectAttribute/InjectAttribute (by name only)
                var injectAttribute = member.GetAttributes()
                    .FirstOrDefault(static attr => attr.AttributeClass?.IsInject == true);

                if(injectAttribute is null)
                    continue;

                // Validate member is injectable based on type
                var isInjectable = member switch
                {
                    IPropertySymbol property => property.SetMethod is not null,
                    IFieldSymbol field => !field.IsReadOnly,
                    IMethodSymbol method => method.MethodKind == MethodKind.Ordinary
                        && (method.ReturnsVoid || RoslynExtensions.IsNonGenericTaskReturnType(method))
                        && !method.IsGenericMethod,
                    _ => false
                };

                if(isInjectable)
                {
                    yield return (member, injectAttribute);
                }
            }
        }
    }
}