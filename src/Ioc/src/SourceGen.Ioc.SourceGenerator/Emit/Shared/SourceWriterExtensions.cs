using static SourceGen.Ioc.SourceGenerator.Models.Constants;

namespace SourceGen.Ioc;

internal static class SourceWriterExtensions
{
    extension(SourceWriter writer)
    {
        public void WriteServiceLambdaOpen(string lifetime, string serviceTypeName, string? registrationKey)
        {
            if(registrationKey is not null)
            {
                writer.WriteLine($"services.AddKeyed{lifetime}<{serviceTypeName}>({registrationKey}, ({IServiceProviderGlobalTypeName} sp, object? key) =>");
                return;
            }

            writer.WriteLine($"services.Add{lifetime}<{serviceTypeName}>(({IServiceProviderGlobalTypeName} sp) =>");
        }

        public void WriteEarlyReturnIfNotNull(string fieldName)
        {
            writer.WriteLine($"if({fieldName} is not null) return {fieldName};");
        }

        public void WriteFieldAssignAndReturn(string fieldName, string instanceVar)
        {
            writer.WriteLine($"{fieldName} = {instanceVar};");
            writer.WriteLine($"return {instanceVar};");
        }
    }
}