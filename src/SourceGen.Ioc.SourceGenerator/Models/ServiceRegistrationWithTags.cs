namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents a service registration with its associated tags for method grouping.
/// This allows deferring method grouping to the code generation phase for better performance.
/// </summary>
/// <param name="Registration">The service registration model.</param>
/// <param name="Tags">The tags for generating tag-specific extension methods.</param>
internal readonly record struct ServiceRegistrationWithTags(
    ServiceRegistrationModel Registration,
    ImmutableEquatableArray<string> Tags);
