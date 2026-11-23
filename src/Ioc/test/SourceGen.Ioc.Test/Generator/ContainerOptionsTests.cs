namespace SourceGen.Ioc.Test.Generator;

/// <summary>
/// The options of the container (Container.Output.spec, "Eager resolution", "Thread safety", "After disposal, and
/// concurrency"). The run-time behavior is tested by the AOT tests; these tests check the generated structure.
/// </summary>
public class ContainerOptionsTests
{
    private const string Source = """
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace App;

        // A sorts before Z, but depends on it: the eager creation must create Z first
        [IocRegister(ServiceLifetime.Singleton)]
        public sealed class A(Z z) : IDisposable { public void Dispose() { } }

        [IocRegister(ServiceLifetime.Singleton)]
        public sealed class Z : IDisposable { public void Dispose() { } }

        [IocRegister(ServiceLifetime.Scoped)]
        public sealed class S(A a) : IAsyncDisposable { public System.Threading.Tasks.ValueTask DisposeAsync() => default; }

        [IocContainer(IntegrateServiceProvider = false{0})]
        public partial class AppContainer;
        """;

    private static async Task<string> GenerateAsync(string options)
    {
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(Source.Replace("{0}", options));
        await result.VerifyCompilableAsync();
        await Assert.That(result.ReportedDiagnostics).IsEmpty();
        return result.GetGeneratedSource("AppContainer.g.cs")!.Replace("\r\n", "\n");
    }

    /// <summary>The body of the member whose declaration line contains <paramref name="declaration"/>.</summary>
    private static string Member(string source, string declaration)
    {
        int start = source.IndexOf(declaration, StringComparison.Ordinal);
        int end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        return source[start..end];
    }

    [Test]
    public async Task Eager_Default_RootConstructorCreatesSingletonsInTopologicalOrder()
    {
        string source = await GenerateAsync(string.Empty);

        string constructor = Member(source, "public AppContainer()");
        await Assert.That(constructor).Contains("__Get_Z_");
        await Assert.That(constructor.IndexOf("__Get_Z_", StringComparison.Ordinal)).IsLessThan(constructor.IndexOf("__Get_A_", StringComparison.Ordinal));
        await Assert.That(constructor).DoesNotContain("__Get_S_");
        await Assert.That(Member(source, "private AppContainer(")).DoesNotContain("__Get_");
    }

    [Test]
    public async Task Eager_Failure_DisposesCreatedInstancesAndRethrows()
    {
        string constructor = Member(await GenerateAsync(string.Empty), "public AppContainer()");

        await Assert.That(constructor).Contains("catch");
        await Assert.That(constructor).Contains("global::System.Threading.Interlocked.Exchange(ref __disposed, 1);");
        await Assert.That(constructor).Contains("throw;");

        // In the reverse topological order: A depends on Z, so A is disposed first
        await Assert.That(constructor.IndexOf("ref __singletons.__f_A_", StringComparison.Ordinal))
            .IsLessThan(constructor.IndexOf("ref __singletons.__f_Z_", StringComparison.Ordinal));
        await Assert.That(constructor).Contains("__DisposeLost(i0);");
    }

    [Test]
    public async Task Eager_FailureOfScope_StartsAsyncDisposalWithoutWait()
    {
        string source = await GenerateAsync(", EagerResolveOptions = EagerResolveOptions.Scoped");
        string constructor = Member(source, "private AppContainer(");

        // S implements only IAsyncDisposable: __DisposeLost starts DisposeAsync, and the synchronous rule does not apply
        await Assert.That(constructor).Contains("ref __f_S_");
        await Assert.That(constructor).Contains("__DisposeLost(i0);");
        await Assert.That(constructor).DoesNotContain("Use DisposeAsync");
    }

    [Test]
    [Arguments("EagerResolveOptions.None", false, false)]
    [Arguments("EagerResolveOptions.Scoped", false, true)]
    [Arguments("EagerResolveOptions.SingletonAndScoped", true, true)]
    public async Task Eager_Options_ChooseTheLifetimes(string option, bool singletons, bool scoped)
    {
        string source = await GenerateAsync($", EagerResolveOptions = {option}");

        await Assert.That(Member(source, "public AppContainer()").Contains("__Get_A_", StringComparison.Ordinal)).IsEqualTo(singletons);
        await Assert.That(Member(source, "private AppContainer(").Contains("__Get_S_", StringComparison.Ordinal)).IsEqualTo(scoped);
    }

    [Test]
    public async Task ThreadSafe_Lock_UsesOneLockForEachSingletonAndOneForScoped()
    {
        string source = await GenerateAsync(string.Empty);

        await Assert.That(source).Contains("lock(s.__lock_A_");
        await Assert.That(source).Contains("lock(s.__lock_Z_");
        await Assert.That(source).Contains("lock(__scopedLock)");
        await Assert.That(source).DoesNotContain(", instance, null)");
    }

    [Test]
    public async Task Helpers_OnlyWhenUsed()
    {
        string withLock = await GenerateAsync(string.Empty);

        await Assert.That(withLock).DoesNotContain("__DisposeLoser");
        await Assert.That(withLock).Contains("private static void __DisposeLost(");
    }

    [Test]
    public async Task ThreadSafe_None_HasNoLock()
    {
        string source = await GenerateAsync(", ThreadSafeStrategy = ThreadSafeStrategy.None");

        await Assert.That(source).DoesNotContain("lock(");
        await Assert.That(source).DoesNotContain("global::System.Threading.Lock");
        await Assert.That(source).DoesNotContain(", instance, null)");
    }

    [Test]
    public async Task ThreadSafe_CompareExchange_KeepsTheFirstAndDisposesTheLoser()
    {
        string source = await GenerateAsync(", ThreadSafeStrategy = ThreadSafeStrategy.CompareExchange");

        await Assert.That(source).DoesNotContain("lock(");
        await Assert.That(source).Contains("global::System.Threading.Interlocked.CompareExchange(ref s.__f_A_");
        await Assert.That(source).Contains(", instance, null) is { } winner)");
        await Assert.That(source).Contains("__DisposeLoser(");
        await Assert.That(Member(source, "private global::App.S __Create_S_")).DoesNotContain("__DisposeLoser(");
    }

    [Test]
    [Arguments("")]
    [Arguments(", ThreadSafeStrategy = ThreadSafeStrategy.None")]
    [Arguments(", ThreadSafeStrategy = ThreadSafeStrategy.CompareExchange")]
    public async Task Creation_AfterStore_ChecksDisposalAgain(string options)
    {
        string source = await GenerateAsync(options);

        string singleton = Member(source, "private global::App.A __Create_A_");
        string scoped = Member(source, "private global::App.S __Create_S_");
        await Assert.That(singleton).Contains("__DisposeLost(");
        await Assert.That(singleton).Contains("s.Root.__disposed");

        // The store before the check has a full fence, so that a concurrent disposal sees the instance
        await Assert.That(singleton).Contains("global::System.Threading.Interlocked.");
        await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(singleton, @"s\.__f_A_\w+ = instance;")).IsFalse();
        await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(scoped, @"^\s*__f_S_\w+ = instance;", System.Text.RegularExpressions.RegexOptions.Multiline)).IsFalse();
        await Assert.That(scoped).Contains("__DisposeLost(");
        await Assert.That(scoped).Contains("ref __disposed");
    }

    [Test]
    public async Task ThrowIfDisposed_Scope_AlsoChecksTheRoot()
    {
        string check = Member(await GenerateAsync(string.Empty), "private void __ThrowIfDisposed()");

        await Assert.That(check).Contains("ref __disposed");
        await Assert.That(check).Contains("ref __singletons.Root.__disposed");
    }
}
