namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents factory method information for dependency injection registration.
/// </summary>
/// <param name="Path">The factory method path (e.g., "MyServiceFactory.Get").</param>
/// <param name="HasServiceProvider">Whether the factory method has an IServiceProvider parameter.</param>
/// <param name="HasKey">Reserved for future use. Always false in current implementation.</param>
/// <param name="ReturnTypeName">The fully qualified return type name of the factory method, used for casting if different from service type.</param>
internal sealed record class FactoryMethodData(
    string Path,
    bool HasServiceProvider,
    bool HasKey,
    string? ReturnTypeName);
