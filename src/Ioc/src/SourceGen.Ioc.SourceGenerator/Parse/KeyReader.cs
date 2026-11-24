using System.Globalization;
using SourceGen.Ioc.Diagnostics;
using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Parse;

/// <summary>The result of reading a key: the key (or no key), or the diagnostic that skips the declaration.</summary>
/// <param name="Key">The key, or <see langword="null"/> for no key.</param>
/// <param name="ErrorId">The diagnostic ID, or <see langword="null"/> when the key is valid.</param>
/// <param name="ErrorNode">Where the diagnostic is reported.</param>
/// <param name="ErrorArguments">The message arguments.</param>
internal readonly record struct KeyResult(KeyRef? Key, string? ErrorId = null, SyntaxNode? ErrorNode = null, string[]? ErrorArguments = null);

/// <summary>Reads <c>KeyType</c> and <c>Key</c> of an attribute (Annotations.spec, "Keys" and "How the generator reads <c>nameof()</c>").</summary>
internal static class KeyReader
{
    private const string KeyedService = "Microsoft.Extensions.DependencyInjection.KeyedService";

    /// <summary>The argument of SGIOC007 for a registration.</summary>
    public const string RegistrationKey = "as a registration key";

    /// <summary>The argument of SGIOC007 for a site that asks for one service.</summary>
    public const string SingleService = "to resolve a single service";

    /// <summary>Reads the key of <paramref name="attribute"/>.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="syntax">The attribute syntax, to read a <c>nameof()</c> expression.</param>
    /// <param name="compilation">The compilation.</param>
    /// <param name="anyKeyNotAllowed">
    /// The argument of SGIOC007 when <c>KeyedService.AnyKey</c> is not allowed, or <see langword="null"/> when it is
    /// allowed: a site that asks for a collection or a dictionary can query with it.
    /// </param>
    /// <returns>The key, or the diagnostic.</returns>
    public static KeyResult Read(AttributeData attribute, AttributeSyntax? syntax, Compilation compilation, string? anyKeyNotAllowed = RegistrationKey)
    {
        TypedConstant key = default;
        bool csharp = false;
        foreach(KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            switch(argument.Key)
            {
                case "Key":
                    key = argument.Value;
                    break;
                case "KeyType":
                    csharp = argument.Value.Value is 1;
                    break;
            }
        }

        // Not set, or null: no key
        if(key.Kind == TypedConstantKind.Error || key.IsNull)
        {
            return new KeyResult(null);
        }

        ExpressionSyntax? keySyntax = syntax?.ArgumentList?.Arguments.FirstOrDefault(static a => a.NameEquals?.Name.Identifier.ValueText == "Key")?.Expression;
        SyntaxNode? location = keySyntax?.Parent ?? syntax;
        return csharp ? ReadSymbol(keySyntax, location, compilation, anyKeyNotAllowed) : ReadValue(key, location, compilation);
    }

    /// <summary>A value key from a constant of an attribute argument, for example the argument of <c>[FromKeyedServices]</c>.</summary>
    /// <param name="key">The constant. A <see langword="null"/> constant is no key.</param>
    /// <param name="location">Where SGIOC022 is reported.</param>
    /// <param name="compilation">The compilation.</param>
    /// <returns>The key, or the diagnostic.</returns>
    public static KeyResult ReadValue(TypedConstant key, SyntaxNode? location, Compilation compilation)
    {
        if(key.IsNull)
        {
            return new KeyResult(null);
        }

        if(key.Kind is TypedConstantKind.Array or TypedConstantKind.Type || key.Type is null)
        {
            return Unsupported(key.Type, location);
        }

        return FromConstant(key.Type, key.Value, compilation) is { } result ? new KeyResult(result) : Unsupported(key.Type, location);
    }

    private static KeyResult Unsupported(ITypeSymbol? type, SyntaxNode? location)
        => new(null, Descriptors.UnsupportedKey.Id, location, [type?.ToDisplayString() ?? "unknown"]);

    /// <summary>
    /// A key from a <c>nameof()</c> expression: a <c>const</c> is a value key, and another static field or property is a
    /// symbol key.
    /// </summary>
    private static KeyResult ReadSymbol(ExpressionSyntax? keySyntax, SyntaxNode? location, Compilation compilation, string? anyKeyNotAllowed)
    {
        KeyResult notFound = new(null, Descriptors.NameofTargetNotFound.Id, location, ["Key", "a static field, property, or constant"]);
        if(keySyntax is not InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }, ArgumentList.Arguments: [ArgumentSyntax operand] } invocation)
        {
            return notFound;
        }

        SemanticModel model = compilation.GetSemanticModel(invocation.SyntaxTree);

        // A method named "nameof" binds to a symbol. The operator does not
        if(model.GetSymbolInfo(invocation).Symbol is not null)
        {
            return notFound;
        }

        ISymbol? symbol = model.GetSymbolInfo(operand.Expression).Symbol;
        if(anyKeyNotAllowed is not null && symbol is IPropertySymbol { Name: "AnyKey", ContainingType: { } containing } && containing.ToDisplayString() == KeyedService)
        {
            return new KeyResult(null, Descriptors.AnyKeyRegistration.Id, location, [anyKeyNotAllowed]);
        }

        switch(symbol)
        {
            case IFieldSymbol { IsConst: true } constant:
                if(constant.ConstantValue is null)
                {
                    return new KeyResult(null);
                }

                return FromConstant(constant.Type, constant.ConstantValue, compilation) is { } value ? new KeyResult(value) : Unsupported(constant.Type, location);
            case IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true, IsIndexer: false }:
                ITypeSymbol memberType = symbol is IFieldSymbol field ? field.Type : ((IPropertySymbol)symbol).Type;
                if(TypeRefFactory.Create(symbol.ContainingType, compilation) is { } declaring && TypeRefFactory.Create(memberType, compilation) is { } valueType)
                {
                    return new KeyResult(new KeyRef(KeyKind.Symbol, declaring, symbol.Name, valueType));
                }

                return notFound;
            default:
                return notFound;
        }
    }

    /// <summary>A value key of a supported type (Annotations.spec, "The three forms of a key"), or <see langword="null"/>.</summary>
    private static KeyRef? FromConstant(ITypeSymbol type, object? value, Compilation compilation)
    {
        if(value is null || TypeRefFactory.Create(type, compilation) is not { } typeRef)
        {
            return null;
        }

        (string? text, KeyLiteral literal) = (type.TypeKind, value) switch
        {
            (TypeKind.Enum, IFormattable underlying) => (underlying.ToString(null, CultureInfo.InvariantCulture), KeyLiteral.Enum),
            (_, bool b) => (b ? "true" : "false", KeyLiteral.Boolean),
            (_, char c) => (((int)c).ToString(CultureInfo.InvariantCulture), KeyLiteral.Char),
            (_, string s) => (s, KeyLiteral.String),
            (_, float f) => (f.ToString("R", CultureInfo.InvariantCulture), KeyLiteral.Single),
            (_, double d) => (d.ToString("R", CultureInfo.InvariantCulture), KeyLiteral.Double),
            (_, sbyte or byte or short or ushort or int or uint or long or ulong) => (((IFormattable)value).ToString(null, CultureInfo.InvariantCulture), KeyLiteral.Integer),
            _ => ((string?)null, KeyLiteral.None),
        };

        return text is null ? null : new KeyRef(KeyKind.Value, typeRef, text, typeRef, literal);
    }
}
