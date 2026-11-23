namespace SourceGen.Ioc;

/// <summary>How the generator reads a key.</summary>
public enum KeyType
{
    /// <summary>The key is the constant value of the attribute argument.</summary>
    Value = 0,

    /// <summary>The key is the static member that a <c>nameof()</c> expression names.</summary>
    Csharp = 1,
}
