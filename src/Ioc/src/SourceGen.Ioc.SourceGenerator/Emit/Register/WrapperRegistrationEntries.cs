namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Represents a Lazy standalone service registration entry to be generated.
    /// </summary>
    /// <param name="InnerServiceTypeName">The fully-qualified service type name.</param>
    /// <param name="ImplementationTypeName">The fully-qualified implementation type name.</param>
    /// <param name="Lifetime">The service lifetime matching the inner service.</param>
    /// <param name="Tags">The tags inherited from the source registration.</param>
    private readonly partial record struct LazyRegistrationEntry(
        string InnerServiceTypeName,
        string ImplementationTypeName,
        ServiceLifetime Lifetime,
        ImmutableEquatableArray<string> Tags);

    /// <summary>
    /// Represents a Func standalone service registration entry to be generated.
    /// </summary>
    /// <param name="FuncServiceTypeName">The full Func service type name (e.g. Func&lt;string, IService&gt;).</param>
    /// <param name="InnerServiceTypeName">The fully-qualified return service type name.</param>
    /// <param name="ImplementationTypeName">The fully-qualified implementation type name.</param>
    /// <param name="Lifetime">The service lifetime matching the inner service.</param>
    /// <param name="ImplementationTypeConstructorParams">The implementation constructor parameters.</param>
    /// <param name="ImplementationTypeInjectionMembers">The implementation injection members.</param>
    /// <param name="InputTypes">The Func input type parameters.</param>
    /// <param name="Tags">The tags inherited from the source registration.</param>
    private readonly partial record struct FuncRegistrationEntry(
        string FuncServiceTypeName,
        string InnerServiceTypeName,
        string ImplementationTypeName,
        ServiceLifetime Lifetime,
        ImmutableEquatableArray<ParameterData>? ImplementationTypeConstructorParams,
        ImmutableEquatableArray<InjectionMemberData> ImplementationTypeInjectionMembers,
        ImmutableEquatableArray<TypeParameter> InputTypes,
        ImmutableEquatableArray<string> Tags);

    /// <summary>
    /// Represents a KeyValuePair service registration entry to be generated.
    /// Used when consumers depend on <c>KeyValuePair&lt;K, V&gt;</c>, <c>IDictionary&lt;K, V&gt;</c>,
    /// or <c>IEnumerable&lt;KeyValuePair&lt;K, V&gt;&gt;</c>.
    /// </summary>
    /// <param name="KeyTypeName">The fully-qualified key type name (e.g., <c>string</c>).</param>
    /// <param name="ValueTypeName">The fully-qualified value type name (e.g., <c>global::TestNamespace.IService</c>).</param>
    /// <param name="KeyExpr">The key literal expression (e.g., <c>"Key1"</c>).</param>
    /// <param name="Lifetime">The service lifetime matching the keyed value service.</param>
    /// <param name="Tags">The tags inherited from the source registration.</param>
    private readonly partial record struct KvpRegistrationEntry(
        string KeyTypeName,
        string ValueTypeName,
        string KeyExpr,
        ServiceLifetime Lifetime,
        ImmutableEquatableArray<string> Tags);
}