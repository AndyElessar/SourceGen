using SourceGen.Ioc.Diagnostics;
using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Parse;

/// <summary>The key specification of a dependency site, or the diagnostics that skip the declaration.</summary>
/// <param name="Mode">The key specification.</param>
/// <param name="Key">The key, for <see cref="SiteKeyMode.Specified"/>.</param>
/// <param name="Errors">The diagnostics. Not empty means that the site is invalid.</param>
internal readonly record struct SiteKey(SiteKeyMode Mode, KeyRef? Key, List<DiagnosticInfo> Errors);

/// <summary>
/// Reads the key of a dependency site from <c>[IocInject]</c>, <c>[FromKeyedServices]</c>, and <c>[ServiceKey]</c>
/// (Annotations.spec, "Key specification").
/// </summary>
internal static class SiteKeyReader
{
    private const string FromKeyedServices = "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute";
    private const string ServiceKey = "Microsoft.Extensions.DependencyInjection.ServiceKeyAttribute";

    /// <summary>The values of <c>ServiceKeyLookupMode</c>.</summary>
    private const int InheritKey = 0;
    private const int NullKey = 1;

    /// <summary>Reads the key of a parameter, a field, a property, or a partial accessor.</summary>
    /// <param name="attributes">The attributes of the site.</param>
    /// <param name="type">The requested type: only a collection or a dictionary can be asked for with <c>AnyKey</c>.</param>
    /// <param name="name">The name of the site, for SGIOC018.</param>
    /// <param name="fallbackLocation">
    /// Where a diagnostic goes when the attribute has no syntax: a site of a type from metadata is reported at the
    /// registration attribute (Diagnostics.spec, "Location").
    /// </param>
    /// <param name="compilation">The compilation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The key specification.</returns>
    public static SiteKey Read(ImmutableArray<AttributeData> attributes, ITypeSymbol type, string name, SyntaxNode? fallbackLocation, Compilation compilation, CancellationToken cancellationToken)
    {
        AttributeData? inject = null;
        AttributeData? fromKeyed = null;
        AttributeData? serviceKey = null;
        foreach(AttributeData attribute in attributes)
        {
            switch(attribute.AttributeClass?.ToDisplayString())
            {
                case DeclarationReader.InjectAttribute:
                    inject = attribute;
                    break;
                case FromKeyedServices:
                    fromKeyed = attribute;
                    break;
                case ServiceKey:
                    serviceKey = attribute;
                    break;
            }
        }

        // Two key annotations on one position (SGIOC018). It is reported on [IocInject], or on [ServiceKey] without it
        List<DiagnosticInfo> errors = [];
        if((inject, fromKeyed, serviceKey) switch
        {
            ({ } i, { }, _) => (i, "[IocInject]", "[FromKeyedServices]"),
            ({ } i, null, { }) => (i, "[IocInject]", "[ServiceKey]"),
            (null, { }, { } s) => (s, "[FromKeyedServices]", "[ServiceKey]"),
            _ => ((AttributeData, string, string)?)null,
        } is var (at, first, second))
        {
            errors.Add(Create(Descriptors.InjectAndFromKeyedServices.Id, at, fallbackLocation, cancellationToken, name, first, second));
            return new SiteKey(SiteKeyMode.None, null, errors);
        }

        if(serviceKey is not null)
        {
            return new SiteKey(SiteKeyMode.ServiceKey, null, errors);
        }

        // [FromKeyedServices] without arguments is InheritKey, and with null it is no key. Its argument is always a value key
        if(fromKeyed is not null)
        {
            if(fromKeyed.ConstructorArguments is not [TypedConstant argument])
            {
                return new SiteKey(SiteKeyMode.Inherit, null, errors);
            }

            // SGIOC022 goes on the argument, as on the Key argument of [IocInject]
            SyntaxNode? syntax = fromKeyed.ApplicationSyntaxReference?.GetSyntax(cancellationToken);
            SyntaxNode? location = (SyntaxNode?)(syntax as AttributeSyntax)?.ArgumentList?.Arguments.FirstOrDefault() ?? syntax;
            return FromResult(KeyReader.ReadValue(argument, location ?? fallbackLocation, compilation), fallbackLocation, errors);
        }

        if(inject is null)
        {
            return new SiteKey(SiteKeyMode.None, null, errors);
        }

        int? lookupMode = null;
        bool hasKey = false;
        foreach(KeyValuePair<string, TypedConstant> argument in inject.NamedArguments)
        {
            switch(argument.Key)
            {
                case "LookupMode":
                    lookupMode = argument.Value.Value as int?;
                    break;
                case "Key":
                    hasKey = true;
                    break;
            }
        }

        if(lookupMode is InheritKey or NullKey)
        {
            if(hasKey)
            {
                errors.Add(Create(Descriptors.LookupModeWithKey.Id, inject, fallbackLocation, cancellationToken, lookupMode == InheritKey ? "InheritKey" : "NullKey"));
                return new SiteKey(SiteKeyMode.None, null, errors);
            }

            return new SiteKey(lookupMode == InheritKey ? SiteKeyMode.Inherit : SiteKeyMode.None, null, errors);
        }

        AttributeSyntax? injectSyntax = inject.ApplicationSyntaxReference?.GetSyntax(cancellationToken) as AttributeSyntax;
        string? anyKeyNotAllowed = WrapperTypes.IsCollectionOrDictionary(type) ? null : KeyReader.SingleService;
        return FromResult(KeyReader.Read(inject, injectSyntax, compilation, anyKeyNotAllowed), fallbackLocation, errors);
    }

    /// <summary>Whether <c>[IocInject]</c> on a constructor or a method sets <c>Key</c> or <c>LookupMode</c> (SGIOC019).</summary>
    /// <param name="attributes">The attributes of the constructor or method.</param>
    /// <param name="kind">"constructor" or "method".</param>
    /// <param name="fallbackLocation">Where the diagnostic goes when the attribute has no syntax.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The diagnostic, or <see langword="null"/>.</returns>
    public static DiagnosticInfo? CheckMember(ImmutableArray<AttributeData> attributes, string kind, SyntaxNode? fallbackLocation, CancellationToken cancellationToken)
    {
        foreach(AttributeData attribute in attributes)
        {
            if(attribute.AttributeClass?.ToDisplayString() == DeclarationReader.InjectAttribute
                && attribute.NamedArguments.Any(static a => a.Key is "Key" or "LookupMode"))
            {
                return Create(Descriptors.KeyOnConstructorOrMethod.Id, attribute, fallbackLocation, cancellationToken, kind);
            }
        }

        return null;
    }

    private static SiteKey FromResult(KeyResult result, SyntaxNode? fallbackLocation, List<DiagnosticInfo> errors)
    {
        if(result.ErrorId is { } id)
        {
            SyntaxNode? location = result.ErrorNode ?? fallbackLocation;
            errors.Add(location is null ? DiagnosticInfo.Create(id, (Location?)null, result.ErrorArguments ?? []) : DiagnosticInfo.Create(id, location, result.ErrorArguments ?? []));
            return new SiteKey(SiteKeyMode.None, null, errors);
        }

        return new SiteKey(result.Key is null ? SiteKeyMode.None : SiteKeyMode.Specified, result.Key, errors);
    }

    private static DiagnosticInfo Create(string id, AttributeData attribute, SyntaxNode? fallbackLocation, CancellationToken cancellationToken, params string[] arguments)
        => (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) ?? fallbackLocation) is { } node
            ? DiagnosticInfo.Create(id, node, arguments)
            : DiagnosticInfo.Create(id, (Location?)null, arguments);
}
