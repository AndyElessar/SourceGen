namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents factory method information for dependency injection registration.
/// </summary>
/// <param name="Path">The factory method path (e.g., "MyServiceFactory.Get").</param>
/// <param name="HasServiceProvider">Whether the factory method has an IServiceProvider parameter.</param>
/// <param name="HasKey">Whether the factory method has a parameter marked with [ServiceKey] attribute.</param>
/// <param name="ReturnTypeName">The fully qualified return type name of the factory method, used for casting if different from service type.</param>
/// <param name="AdditionalParameters">Parameters that need to be resolved from the service provider (excluding IServiceProvider and [ServiceKey] parameters).</param>
internal sealed record class FactoryMethodData(
    string Path,
    bool HasServiceProvider,
    bool HasKey,
    string? ReturnTypeName,
    ImmutableEquatableArray<ParameterData> AdditionalParameters);
