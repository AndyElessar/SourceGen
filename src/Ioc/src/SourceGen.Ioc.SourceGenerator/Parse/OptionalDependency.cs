using System.Globalization;
using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Parse;

/// <summary>Decides the optional mode of a dependency site (Semantics.spec, "Dependency resolution").</summary>
internal static class OptionalDependency
{
    /// <summary>The optional mode of a parameter, and the expression of its default value.</summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns>The mode and the default value expression.</returns>
    public static (OptionalMode Mode, string? DefaultValue) Of(IParameterSymbol parameter)
    {
        // A default value wins, also when the type is nullable too
        if(parameter.HasExplicitDefaultValue)
        {
            return (OptionalMode.Default, Format(parameter.ExplicitDefaultValue));
        }

        return IsNullableReference(parameter.Type, parameter.NullableAnnotation) ? (OptionalMode.Nullable, null) : (OptionalMode.Required, null);
    }

    /// <summary>
    /// Whether a type is a reference type annotated as nullable. Code without nullable annotations
    /// (<c>#nullable disable</c>) is always required.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="annotation">The annotation of the site.</param>
    /// <returns>Whether the site is optional because of the annotation.</returns>
    public static bool IsNullableReference(ITypeSymbol type, NullableAnnotation annotation)
        => annotation == NullableAnnotation.Annotated && type.IsReferenceType;

    /// <summary>
    /// The C# expression of a constant. The generated code casts it to the parameter type, so an enum value or a small
    /// integer needs no suffix. <c>G9</c> and <c>G17</c> always round-trip; <c>R</c> does not on .NET Framework, where the
    /// generator can run.
    /// </summary>
    private static string Format(object? value) => value switch
    {
        null => "default",
        bool b => b ? "true" : "false",
        string s => SymbolDisplay.FormatLiteral(s, quote: true),
        char c => SymbolDisplay.FormatLiteral(c, quote: true),
        float f when float.IsNaN(f) => "global::System.Single.NaN",
        float f when float.IsPositiveInfinity(f) => "global::System.Single.PositiveInfinity",
        float f when float.IsNegativeInfinity(f) => "global::System.Single.NegativeInfinity",
        float f => f.ToString("G9", CultureInfo.InvariantCulture) + "F",
        double d when double.IsNaN(d) => "global::System.Double.NaN",
        double d when double.IsPositiveInfinity(d) => "global::System.Double.PositiveInfinity",
        double d when double.IsNegativeInfinity(d) => "global::System.Double.NegativeInfinity",
        double d => d.ToString("G17", CultureInfo.InvariantCulture) + "D",
        decimal m => m.ToString(CultureInfo.InvariantCulture) + "M",
        long l => l.ToString(CultureInfo.InvariantCulture) + "L",
        ulong u => u.ToString(CultureInfo.InvariantCulture) + "UL",
        uint u => u.ToString(CultureInfo.InvariantCulture) + "U",
        int or short or ushort or byte or sbyte => ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture),
        // [DateTimeConstant] on an optional parameter
        DateTime time => "new global::System.DateTime(" + time.Ticks.ToString(CultureInfo.InvariantCulture) + "L)",
        _ => "default",
    };
}
