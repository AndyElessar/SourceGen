namespace SourceGen.Ioc;

partial class RegisterSourceGenerator
{
    private static IEnumerable<DefaultSettingsModel> TransformDefaultSettings(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        foreach(var attr in ctx.Attributes)
        {
            // Use shared method from Constants to extract default settings
            var data = attr.ExtractDefaultSettings();

            if(data is not null)
                yield return data;
        }
    }

    /// <summary>
    /// Transforms generic IoCRegisterDefaultsAttribute (IoCRegisterDefaultsAttribute&lt;T&gt;) to extract default settings.
    /// The target service type is specified via type parameter instead of constructor argument.
    /// </summary>
    private static IEnumerable<DefaultSettingsModel> TransformDefaultSettingsGeneric(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        foreach(var attr in ctx.Attributes)
        {
            // Use shared method from Constants to extract default settings from generic attribute
            var data = attr.ExtractDefaultSettingsFromGenericAttribute();

            if(data is not null)
                yield return data;
        }
    }
}
