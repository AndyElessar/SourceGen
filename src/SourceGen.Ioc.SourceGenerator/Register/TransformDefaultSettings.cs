namespace SourceGen.Ioc.SourceGenerator.Register;

partial class RegisterSourceGenerator
{
    private static IEnumerable<DefaultSettingsModel> TransformDefaultSettings(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        foreach(var attr in ctx.Attributes)
        {
            var data = ExtractDefaultSettingsFromAttributeData(attr);

            if(data is not null)
                yield return data;
        }
    }

    private static DefaultSettingsModel? ExtractDefaultSettingsFromAttributeData(AttributeData attributeData)
    {
        if(attributeData.ConstructorArguments.Length < 2)
            return null;
        if(attributeData.ConstructorArguments[0].Value is not INamedTypeSymbol targetServiceType)
            return null;
        if(attributeData.ConstructorArguments[1].Value is not int lifetime)
            return null;

        var registerAllInterfaces = attributeData.GetNamedArgument<bool>("RegisterAllInterfaces", false);
        var registerAllBaseClasses = attributeData.GetNamedArgument<bool>("RegisterAllBaseClasses", false);
        var serviceTypes = attributeData.GetTypeArrayArgument("ServiceTypes");
        var typeData = targetServiceType.GetTypeData();

        return new DefaultSettingsModel(
            typeData,
            lifetime,
            registerAllInterfaces,
            registerAllBaseClasses,
            serviceTypes);
    }
}
