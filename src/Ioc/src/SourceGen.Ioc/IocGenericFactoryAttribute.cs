using System.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Hint generic factory method's type parameter mapping to SourceGen.Ioc generation.
/// </summary>
/// <remarks>
/// <code>
/// Define:
/// [IocRegisterDefaults(typeof(IRequestHandler❮❯), Factory = nameof(FactoryContainer.Create))]
/// public class FactoryContainer
/// {                                                   ┌--------------┐ "int" is a placeholder, make sure placeholders is unique
///                                                     │              │ in the context of the generic type mapping.
///                                                     ↓              ↓
///     [IocGenericFactory(typeof(IRequestHandler❮Task❮int❯❯), typeof(int))]
///     public static Create❮T❯() = new Handler❮T❯();
/// }
///
/// Generate:
/// services.AddSingleton❮IRequestHandler❮Task❮Entity❯❯❯(sp => FactoryContainer.Create❮Entity❯());
/// </code>
/// </remarks>
/// <param name="genericTypeMap">Type parameter mapping for genric factory method.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocGenericFactoryAttribute(params Type[] genericTypeMap) : Attribute
{
    /// <summary>
    /// Gets type parameter mapping for genric factory method.
    /// </summary>
    public Type[] GenericTypeMap { get; } = genericTypeMap;
}
