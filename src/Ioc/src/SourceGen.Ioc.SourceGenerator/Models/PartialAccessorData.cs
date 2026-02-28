namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Specifies the kind of partial accessor declared in the container class.
/// </summary>
internal enum PartialAccessorKind
{
    /// <summary>
    /// A partial method accessor (e.g., <c>public partial IMyService GetMyService();</c>).
    /// </summary>
    Method,

    /// <summary>
    /// A partial property accessor (e.g., <c>public partial IMyService MyService { get; }</c>).
    /// </summary>
    Property,
}

/// <summary>
/// Represents a user-declared partial method or property in a container class
/// that serves as a fast-path accessor for a registered service.
/// </summary>
/// <param name="Kind">Whether this is a partial method or partial property.</param>
/// <param name="Name">The name of the method or property.</param>
/// <param name="ReturnTypeName">Fully qualified return type name (e.g., "global::IMyService").</param>
/// <param name="IsNullable">Whether the return type is nullable (optional resolution).</param>
/// <param name="Key">The keyed service key from [IocInject], or null for non-keyed resolution.</param>
internal sealed record class PartialAccessorData(
    PartialAccessorKind Kind,
    string Name,
    string ReturnTypeName,
    bool IsNullable,
    string? Key);
