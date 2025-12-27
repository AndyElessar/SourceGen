namespace SourceGen.Ioc.SourceGenerator.Register;

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
}
