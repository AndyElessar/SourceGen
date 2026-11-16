namespace SourceGen.Ioc;

/// <summary>
/// Represents factory method information for dependency injection registration.
/// </summary>
/// <param name="Path">The factory method path (e.g., "MyServiceFactory.Get").</param>
/// <param name="ServiceProviderPosition">The position of the IServiceProvider parameter, or -1 if the factory method has none.</param>
/// <param name="KeyPosition">The position of the parameter marked with [ServiceKey] attribute, or -1 if the factory method has none.</param>
/// <param name="ReturnTypeName">The fully qualified return type name of the factory method, used for casting if different from service type.</param>
/// <param name="AdditionalParameters">Parameters that need to be resolved from the service provider (excluding IServiceProvider and [ServiceKey] parameters), in declaration order.</param>
/// <param name="GenericTypeMapping">The generic type mapping for generic factory methods marked with [IocGenericFactory], or null if not a generic factory.</param>
/// <param name="TypeParameterNames">The names of the type parameters of the factory method, which its parameter types are written with.</param>
/// <param name="CastArguments">
/// Whether each argument is cast to its parameter type, so that the call binds the resolved overload of an overloaded factory
/// method: the generated arguments, such as <c>this</c> or a concrete service type, could otherwise make C# overload
/// resolution prefer another overload, such as <c>Create&lt;T&gt;(T value)</c> or one that is not usable as a factory.
/// </param>
/// <param name="KeyParameterTypeName">The fully qualified type of the [ServiceKey] parameter, when <paramref name="CastArguments"/> is set.</param>
internal sealed record class FactoryMethodData(
    string Path,
    int ServiceProviderPosition,
    int KeyPosition,
    string? ReturnTypeName,
    ImmutableEquatableArray<ParameterData> AdditionalParameters,
    GenericFactoryTypeMapping? GenericTypeMapping = null,
    ImmutableEquatableArray<string>? TypeParameterNames = null,
    bool CastArguments = false,
    string? KeyParameterTypeName = null)
{
    /// <summary>The number of type parameters on the generic factory method.</summary>
    public int TypeParameterCount => TypeParameterNames?.Length ?? 0;

    /// <summary>Whether the factory method has an IServiceProvider parameter.</summary>
    public bool HasServiceProvider => ServiceProviderPosition >= 0;

    /// <summary>Whether the factory method has a parameter marked with [ServiceKey] attribute.</summary>
    public bool HasKey => KeyPosition >= 0;

    /// <summary>
    /// Orders factory invocation arguments by the factory method's parameter positions.
    /// </summary>
    /// <param name="serviceProvider">The expression passed to the IServiceProvider parameter.</param>
    /// <param name="key">The expression passed to the [ServiceKey] parameter, or null when the registration has no key; the parameter then gets <c>default!</c>.</param>
    /// <param name="additionalArguments">The resolved arguments for <see cref="AdditionalParameters"/>, in the same order.</param>
    public List<string> OrderArguments(string serviceProvider, string? key, IReadOnlyList<string> additionalArguments)
    {
        var count = additionalArguments.Count + (HasServiceProvider ? 1 : 0) + (HasKey ? 1 : 0);
        var arguments = new List<string>(count);
        var next = 0;
        for(var position = 0; position < count; position++)
        {
            if(position == ServiceProviderPosition)
            {
                arguments.Add(Cast("global::System.IServiceProvider", serviceProvider));
            }
            else if(position == KeyPosition)
            {
                // A non-keyed registration has no key; default compiles for any parameter type
                arguments.Add(Cast(KeyParameterTypeName, key ?? "default!"));
            }
            else
            {
                // A reference type is cast without its nullable annotation, which does not affect overload resolution
                arguments.Add(Cast(AdditionalParameters[next].Type.Name, additionalArguments[next]));
                next++;
            }
        }

        return arguments;

        string Cast(string? typeName, string argument) => CastArguments && typeName is not null ? $"({typeName})({argument})" : argument;
    }
}
