namespace SourceGen.Ioc;

/// <summary>
/// Generic constraint rules shared by the generator, which records them for type arguments, and the analyzer, which
/// checks them against symbols, so both close the same open generic registrations.
/// </summary>
internal static partial class RoslynExtensions
{
    extension(ITypeSymbol type)
    {
        /// <summary>
        /// Gets whether a type satisfies the <c>new()</c> constraint: a value type or a non-abstract class with a public
        /// parameterless constructor, which must set the required members of the type (<c>[SetsRequiredMembers]</c>)
        /// when it has any (CS9040).
        /// </summary>
        public bool SatisfiesConstructorConstraint()
        {
            IMethodSymbol? constructor = null;
            if(type is INamedTypeSymbol namedType)
            {
                foreach(var candidate in namedType.InstanceConstructors)
                {
                    if(candidate.Parameters.IsEmpty && candidate.DeclaredAccessibility is Accessibility.Public)
                    {
                        constructor = candidate;
                        break;
                    }
                }
            }

            if(!type.IsValueType
                && (type is not INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false } || constructor is null))
            {
                return false;
            }

            if(constructor is not null && constructor.GetAttributes().Any(static a =>
                a.AttributeClass is { Name: "SetsRequiredMembersAttribute" } attributeClass
                && attributeClass.ContainingNamespace.ToDisplayString() == "System.Diagnostics.CodeAnalysis"))
            {
                return true;
            }

            return !type.HasRequiredMembers();
        }

        /// <summary>Gets whether a type or one of its base classes declares a required field or property.</summary>
        private bool HasRequiredMembers()
        {
            for(var current = type; current is not null; current = current.BaseType)
            {
                foreach(var member in current.GetMembers())
                {
                    if(member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true })
                        return true;
                }
            }

            return false;
        }
    }

    extension(INamedTypeSymbol type)
    {
        /// <summary>Gets whether a generic type has a covariant (<c>out</c>) type parameter.</summary>
        public bool IsCovariantGeneric
            => type.IsGenericType && type.OriginalDefinition.TypeParameters.Any(static p => p.Variance == VarianceKind.Out);

        /// <summary>
        /// Gets whether a variant type converts to a type through covariance, one level deep: each covariant type argument
        /// that is a reference type may be replaced by <c>object</c>, one of its interfaces, or one of its base classes, such
        /// as <c>IEnumerable&lt;string&gt;</c> to <c>IEnumerable&lt;object&gt;</c>. Other type arguments must be the same,
        /// so contravariant and nested variant conversions are not followed. A type nested in a generic type is not
        /// converted, because the generator compares type names, which do not separate its containing type's arguments.
        /// </summary>
        public bool IsCovariantConversionTo(ITypeSymbol target)
        {
            if(!type.IsCovariantGeneric
                || type.ContainingType is { IsGenericType: true }
                || target is not INamedTypeSymbol namedTarget
                || !SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, namedTarget.OriginalDefinition))
            {
                return false;
            }

            var typeParameters = type.OriginalDefinition.TypeParameters;
            for(var i = 0; i < typeParameters.Length; i++)
            {
                var argument = type.TypeArguments[i];
                var targetArgument = namedTarget.TypeArguments[i];
                if(SymbolEqualityComparer.Default.Equals(argument, targetArgument))
                {
                    continue;
                }

                if(typeParameters[i].Variance != VarianceKind.Out
                    || !argument.IsReferenceType
                    || !(targetArgument.SpecialType is SpecialType.System_Object
                        || argument.AllInterfaces.Contains(targetArgument, SymbolEqualityComparer.Default)
                        || IsBaseClass(argument, targetArgument)))
                {
                    return false;
                }
            }

            return true;

            static bool IsBaseClass(ITypeSymbol type, ITypeSymbol target)
            {
                for(var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
                {
                    if(SymbolEqualityComparer.Default.Equals(baseType, target))
                        return true;
                }

                return false;
            }
        }
    }
}
