namespace SourceGen.Ioc;
/// <summary>
/// Identifies a service by its type and key.
/// </summary>
/// <param name="ServiceType">Service type.</param>
/// <param name="Key">Service key.</param>
public readonly record struct ServiceIdentifier(Type ServiceType, object Key);
