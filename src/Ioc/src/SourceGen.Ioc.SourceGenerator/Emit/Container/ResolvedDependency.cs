namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private abstract record class ResolvedDependency
    {
        public abstract string FormatExpression(bool isOptional);
    }
}