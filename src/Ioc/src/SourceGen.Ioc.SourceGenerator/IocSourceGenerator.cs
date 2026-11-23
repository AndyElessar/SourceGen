namespace SourceGen.Ioc;

/// <summary>
/// Generates the IoC container, the module output, and the <c>Add{Name}</c> method.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class IocSourceGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
    }
}
