namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Combined container and grouped registrations for pipeline caching.
/// </summary>
/// <param name="Container">The container model.</param>
/// <param name="Groups">The pre-computed registration groups.</param>
internal sealed record ContainerWithGroups(
    ContainerModel Container,
    ContainerRegistrationGroups Groups);
