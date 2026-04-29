namespace SourceGen.Ioc.SourceGenerator;

internal static partial class RoslynExtensions
{
    /// <summary>
    /// Returns <see langword="true"/> when the method returns the non-generic
    /// <see cref="System.Threading.Tasks.Task"/> type (arity 0).
    /// </summary>
    internal static bool IsNonGenericTaskReturnType(IMethodSymbol method)
        => method.ReturnType is INamedTypeSymbol { Arity: 0, Name: "Task" } named
            && named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks";

    extension<T>(IEnumerable<T> source)
    {
        public IEnumerable<(int Index, T Item)> Index()
        {
            int index = 0;
            foreach(var item in source)
            {
                yield return (index, item);
                checked { index++; }
            }
        }
    }

    extension<T>(IReadOnlyList<T> source)
    {
        public IEnumerable<(int Index, T Item)> Index()
        {
            for(int i = 0; i < source.Count; i++)
            {
                yield return (i, source[i]);
            }
        }
    }
}