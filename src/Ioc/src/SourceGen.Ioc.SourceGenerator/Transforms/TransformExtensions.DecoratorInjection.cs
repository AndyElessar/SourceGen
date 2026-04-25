namespace SourceGen.Ioc.SourceGenerator.Models;

internal static partial class TransformExtensions
{
    extension(INamedTypeSymbol typeSymbol)
    {
        /// <summary>
        /// Extracts injection members (properties, fields, methods with [IocInject]/[Inject] attributes) from the type.
        /// This is used for both regular registrations and decorators.
        /// </summary>
        /// <param name="semanticModel">Optional semantic model to resolve full access paths for nameof() expressions.</param>
        /// <returns>An array of injection member data.</returns>
        public ImmutableEquatableArray<InjectionMemberData> ExtractInjectionMembersForDecorator(SemanticModel? semanticModel = null)
        {
            List<InjectionMemberData>? injectionMembers = null;

            foreach(var (member, injectAttribute) in typeSymbol.GetInjectedMembers())
            {
                // Extract key information from IocInjectAttribute/InjectAttribute
                var (key, _, _) = injectAttribute.GetKeyInfo(semanticModel);

                InjectionMemberData? memberData = member switch
                {
                    IPropertySymbol property => CreateDecoratorPropertyInjection(property, key),
                    IFieldSymbol field => CreateDecoratorFieldInjection(field, key),
                    IMethodSymbol method => CreateDecoratorMethodInjection(method, key, semanticModel),
                    _ => null
                };

                if(memberData is not null)
                {
                    injectionMembers ??= [];
                    injectionMembers.Add(memberData);
                }
            }

            return injectionMembers?.ToImmutableEquatableArray() ?? [];
        }

        private static InjectionMemberData CreateDecoratorPropertyInjection(IPropertySymbol property, string? key)
        {
            var propertyType = property.Type.GetTypeData();
            var isNullable = property.NullableAnnotation == NullableAnnotation.Annotated;

            // Try to get the default value from property initializer
            var (hasDefaultValue, defaultValue) = GetDecoratorPropertyDefaultValue(property);

            return new InjectionMemberData(
                InjectionMemberType.Property,
                property.Name,
                propertyType,
                null,
                key,
                isNullable,
                hasDefaultValue,
                defaultValue);
        }

        private static InjectionMemberData CreateDecoratorFieldInjection(IFieldSymbol field, string? key)
        {
            var fieldType = field.Type.GetTypeData();
            var isNullable = field.NullableAnnotation == NullableAnnotation.Annotated;

            // Try to get the default value from field initializer
            var (hasDefaultValue, defaultValue) = GetDecoratorFieldDefaultValue(field);

            return new InjectionMemberData(
                InjectionMemberType.Field,
                field.Name,
                fieldType,
                null,
                key,
                isNullable,
                hasDefaultValue,
                defaultValue);
        }

        private static (bool HasDefaultValue, string? DefaultValue) GetDecoratorPropertyDefaultValue(IPropertySymbol property)
        {
            var syntaxRef = property.DeclaringSyntaxReferences.FirstOrDefault();
            if(syntaxRef?.GetSyntax() is not PropertyDeclarationSyntax propertySyntax)
                return (false, null);

            var initializer = propertySyntax.Initializer;
            if(initializer is null)
                return (false, null);

            // Check if it's a null literal or null-forgiving expression (null!)
            if(IsDecoratorNullExpression(initializer.Value))
            {
                return (true, null);
            }

            return (true, initializer.Value.ToString());
        }

        private static (bool HasDefaultValue, string? DefaultValue) GetDecoratorFieldDefaultValue(IFieldSymbol field)
        {
            var syntaxRef = field.DeclaringSyntaxReferences.FirstOrDefault();
            var syntax = syntaxRef?.GetSyntax();

            // Field can be declared in VariableDeclaratorSyntax
            EqualsValueClauseSyntax? initializer = syntax switch
            {
                VariableDeclaratorSyntax variableDeclarator => variableDeclarator.Initializer,
                _ => null
            };

            if(initializer is null)
                return (false, null);

            // Check if it's a null literal or null-forgiving expression (null!)
            if(IsDecoratorNullExpression(initializer.Value))
            {
                return (true, null);
            }

            return (true, initializer.Value.ToString());
        }

        private static bool IsDecoratorNullExpression(ExpressionSyntax expression)
        {
            // Direct null literal
            if(expression is LiteralExpressionSyntax literal &&
               literal.Kind() == SyntaxKind.NullLiteralExpression)
            {
                return true;
            }

            // Null-forgiving expression: null!
            if(expression is PostfixUnaryExpressionSyntax postfix &&
               postfix.Kind() == SyntaxKind.SuppressNullableWarningExpression &&
               postfix.Operand is LiteralExpressionSyntax innerLiteral &&
               innerLiteral.Kind() == SyntaxKind.NullLiteralExpression)
            {
                return true;
            }

            return false;
        }

        private static InjectionMemberData CreateDecoratorMethodInjection(IMethodSymbol method, string? key, SemanticModel? semanticModel)
        {
            var parameters = method.Parameters
                .Select(p =>
                {
                    var (serviceKey, hasInjectAttribute, hasServiceKeyAttribute, hasFromKeyedServicesAttribute) = p.GetServiceKeyAndAttributeInfo(semanticModel);
                    return new ParameterData(
                        p.Name,
                        p.Type.GetTypeData(),
                        IsNullable: p.NullableAnnotation == NullableAnnotation.Annotated,
                        HasDefaultValue: p.HasExplicitDefaultValue,
                        DefaultValue: p.HasExplicitDefaultValue ? DecoratorToDefaultValueCodeString(p.ExplicitDefaultValue) : null,
                        ServiceKey: serviceKey,
                        HasInjectAttribute: hasInjectAttribute,
                        HasServiceKeyAttribute: hasServiceKeyAttribute,
                        HasFromKeyedServicesAttribute: hasFromKeyedServicesAttribute);
                })
                .ToImmutableEquatableArray();

            return new InjectionMemberData(
                InjectionMemberType.Method,
                method.Name,
                null,
                parameters,
                key);
        }

        private static string? DecoratorToDefaultValueCodeString(object? value)
        {
            return value switch
            {
                null => null,
                string s => $"\"{s}\"",
                char c => $"'{c}'",
                bool b => b ? "true" : "false",
                _ => value.ToString()
            };
        }
    }
}