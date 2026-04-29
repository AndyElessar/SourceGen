using static SourceGen.Ioc.SourceGenerator.Models.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Writes a factory registration line for keyed or non-keyed services.
    /// </summary>
    private static void WriteFactoryRegistrationLine(
        SourceWriter writer,
        string lifetime,
        string serviceTypeName,
        string? registrationKey,
        string factoryInvocation)
    {
        if(registrationKey is not null)
        {
            writer.WriteLine($"services.AddKeyed{lifetime}<{serviceTypeName}>({registrationKey}, ({IServiceProviderGlobalTypeName} sp, object? key) => {factoryInvocation});");
            return;
        }

        writer.WriteLine($"services.Add{lifetime}<{serviceTypeName}>(({IServiceProviderGlobalTypeName} sp) => {factoryInvocation});");
    }
}
