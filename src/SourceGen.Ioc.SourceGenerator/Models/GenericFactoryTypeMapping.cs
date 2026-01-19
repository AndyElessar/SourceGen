namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents the type mapping information for a generic factory method.
/// Used to map service type parameters to factory method type parameters.
/// </summary>
/// <remarks>
/// <para>
/// The first type in GenericTypeMap is the service type template with placeholders,
/// e.g., <c>IRequestHandler&lt;Task&lt;int&gt;&gt;</c> where <c>int</c> is a placeholder.
/// </para>
/// <para>
/// Following types map to the factory method's type parameters in order.
/// These types should match the placeholders in the service type template.
/// </para>
/// <example>
/// <code>
/// // Service type is IRequestHandler&lt;&gt;, has 1 type parameter
/// [IocGenericFactory(typeof(IRequestHandler&lt;Task&lt;int&gt;&gt;), typeof(int))]
/// public static Create&lt;T&gt;() => new Handler&lt;T&gt;();
///
/// // When generating for IRequestHandler&lt;Task&lt;Entity&gt;&gt;:
/// // - int maps to T (from GenericTypeMap[1])
/// // - Entity replaces int in the service type
/// // - Generate: FactoryContainer.Create&lt;Entity&gt;()
/// </code>
/// </example>
/// </remarks>
/// <param name="ServiceTypeTemplate">The service type template with placeholder types (e.g., IRequestHandler&lt;Task&lt;int&gt;&gt;).</param>
/// <param name="PlaceholderToTypeParamMap">Maps placeholder type names to factory method type parameter indices.</param>
internal sealed record class GenericFactoryTypeMapping(
    TypeData ServiceTypeTemplate,
    ImmutableEquatableDictionary<string, int> PlaceholderToTypeParamMap);
