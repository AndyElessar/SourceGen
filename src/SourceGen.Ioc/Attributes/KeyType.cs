namespace SourceGen.Ioc;

/// <summary>
/// Specifies <see cref="IoCRegisterAttribute.Key"/> type.
/// </summary>
public enum KeyType
{
    /// <summary>
    /// Treat Key as its value.
    /// </summary>
    Value = 0,
    /// <summary>
    /// Treat Key as C# code string.
    /// </summary>
    Csharp = 1
}