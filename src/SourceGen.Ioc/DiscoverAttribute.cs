using System.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Indicates that <paramref name="closedGenericType"/> should be discovered by source generators.<br/>
/// Use for open generic registrations, only need when the closed generic type is not directly referenced in code.
/// </summary>
/// <param name="closedGenericType">The constructed generic type should be discovered by source generators.</param>
[AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class DiscoverAttribute(Type closedGenericType) : Attribute
{
    /// <summary>
    /// Gets the constructed generic type.
    /// </summary>
    public Type ClosedGenericType { get; init; } = closedGenericType;
}

#if NET7_0_OR_GREATER

/// <summary>
/// Indicates that <typeparamref name="T"/> should be discovered by source generators.<br/>
/// Use for open generic registrations, only need when the closed generic type is not directly referenced in code.
/// </summary>
/// <typeparam name="T">The constructed generic type should be discovered by source generators.</typeparam>
[AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class DiscoverAttribute<T> : Attribute
{
}

#endif