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
        /// Constructs the covariant type with each covariant reference type argument replaced by one of its supertypes
        /// (its interfaces and base classes). Value type arguments do not vary, nested variance is not followed, and the
        /// count is capped to bound the cost.
        /// </summary>
        public IEnumerable<INamedTypeSymbol> GetCovariantConversions()
        {
            const int MaxConversions = 64;
            if(!type.IsCovariantGeneric)
            {
                yield break;
            }

            var definition = type.OriginalDefinition;
            var candidates = new List<ITypeSymbol>[type.TypeArguments.Length];
            for(var i = 0; i < candidates.Length; i++)
            {
                var argument = type.TypeArguments[i];
                candidates[i] = [argument];
                if(definition.TypeParameters[i].Variance == VarianceKind.Out && argument.IsReferenceType)
                {
                    candidates[i].AddRange(argument.AllInterfaces);
                    for(var baseType = argument.BaseType; baseType is not null; baseType = baseType.BaseType)
                    {
                        candidates[i].Add(baseType);
                    }
                }
            }

            var count = 0;
            var arguments = new ITypeSymbol[candidates.Length];
            foreach(var combination in Combine(0))
            {
                if(++count > MaxConversions)
                {
                    yield break;
                }
                yield return definition.Construct(combination);
            }

            // Yields every combination except the type's own arguments
            IEnumerable<ITypeSymbol[]> Combine(int index)
            {
                if(index == candidates.Length)
                {
                    if(!arguments.SequenceEqual(type.TypeArguments, SymbolEqualityComparer.Default))
                    {
                        yield return [.. arguments];
                    }
                    yield break;
                }

                foreach(var candidate in candidates[index])
                {
                    arguments[index] = candidate;
                    foreach(var combination in Combine(index + 1))
                    {
                        yield return combination;
                    }
                }
            }
        }
    }
}
