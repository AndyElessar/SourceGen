using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC024: InjectMembers specifies non-injectable member.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC024)]
public class SGIOC024Tests
{
    [Test]
    [Arguments("ImplicitImplementation", "typeof(Node<>)", "nameof(INode<int>.Repo)", "public IRepo<T>? Repo { get; set; }", null)]
    [Arguments("ImplementationOfGetterOnlyMember", "typeof(Node<>)", "nameof(INode<int>.Getter)", "public IRepo<T>? Repo { get; set; } public IRepo<T>? Getter { get; set; }", null)]
    [Arguments("MissingTargetType", "typeof(Missing)", "nameof(INode<int>.Repo)", "public IRepo<T>? Repo { get; set; }", null)]
    [Arguments("ExplicitImplementation", "typeof(Node<>)", "nameof(INode<int>.Repo)", "IRepo<T>? INode<T>.Repo { get; set; }", "it is an explicit interface implementation")]
    [Arguments("ExplicitImplementationOfClosedType", "typeof(Node<string>)", "nameof(INode<string>.Repo)", "IRepo<T>? INode<T>.Repo { get; set; }", "it is an explicit interface implementation")]
    [Arguments("MemberOfOtherType", "typeof(Node<>)", "nameof(Other.Repo)", "", "it is not a member of 'Node<T>'")]
    [Arguments("MemberOfOtherTypeOnTypeLevelAttribute", null, "nameof(Other.Repo)", "", "it is not a member of 'Node<T>'")]
    public async Task SGIOC024_MemberOfInterfaceOrOtherType_ChecksMemberOfRegisteredType(string scenario, string? registeredType, string injectMember, string nodeMember, string? expectedReason)
    {
        // The generator injects the member of the registered type: an interface member through its implementation
        var attribute = $"IocRegisterFor({registeredType}, ServiceTypes = [typeof(INode<>)], InjectMembers = [{injectMember}])";
        var source = $$"""
            using SourceGen.Ioc;
            using TestNamespace;

            {{(registeredType is null ? "" : $"[assembly: {attribute}]")}}

            namespace TestNamespace;

            public interface IRepo<T>;

            public interface INode<T>
            {
                IRepo<T>? Repo { get; set; }
                IRepo<T>? Getter => null;
            }

            public sealed class Other
            {
                public IRepo<int>? Repo { get; set; }
            }

            {{(registeredType is null ? $"[IocRegister(ServiceTypes = [typeof(INode<>)], InjectMembers = [{injectMember}])]" : "")}}
            public sealed class Node<T> : INode<T>
            {
                {{(nodeMember.Length == 0 ? "IRepo<T>? INode<T>.Repo { get; set; }" : nodeMember)}}
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        if(expectedReason is null)
        {
            await Assert.That(sgioc024).IsEmpty().Because(scenario);
            return;
        }

        await Assert.That(sgioc024).Count().IsEqualTo(1).Because(scenario);
        await Assert.That(sgioc024[0].GetMessage()).Contains("'Repo'").And.Contains(expectedReason).Because(scenario);
    }

    [Test]
    [Arguments("HiddenProperty", "nameof(Base.X)", """
        public class Base { public IFoo? X { get; set; } }
        public sealed class Derived : Base { public new IBar? X { get; set; } }
        """, "it is hidden by 'Derived.X'")]
    [Arguments("HiddenField", "nameof(Base.X)", """
        public class Base { public IFoo? X; }
        public sealed class Derived : Base { public new IBar? X; }
        """, "it is hidden by 'Derived.X'")]
    [Arguments("HiddenByMemberOfOtherKind", "nameof(Base.X)", """
        public class Base { public IFoo? X { get; set; } }
        public sealed class Derived : Base { public new void X(IBar bar) { } }
        """, "it is hidden by 'Derived.X'")]
    [Arguments("HiddenMethod", "nameof(Base.X)", """
        public class Base { public void X(IFoo foo) { } }
        public sealed class Derived : Base { public new void X(IFoo foo) { } }
        """, "it is hidden by 'Derived.X'")]
    [Arguments("HiddenOnIntermediateBase", "nameof(Base.X)", """
        public class Base { public IFoo? X { get; set; } }
        public class Middle : Base { public new IBar? X { get; set; } }
        public sealed class Derived : Middle;
        """, "it is hidden by 'Middle.X'")]
    [Arguments("InterfaceMemberImplementedByBaseAndHidden", "nameof(IHasX.X)", """
        public interface IHasX { IFoo? X { get; set; } }
        public class Base : IHasX { public IFoo? X { get; set; } }
        public sealed class Derived : Base { public new IBar? X { get; set; } }
        """, "it is hidden by 'Derived.X'")]
    [Arguments("OverriddenProperty", "nameof(Base.X)", """
        public class Base { public virtual IFoo? X { get; set; } }
        public sealed class Derived : Base { public override IFoo? X { get; set; } }
        """, null)]
    [Arguments("InaccessibleHidingMember", "nameof(Base.X)", """
        public class Base { public IFoo? X { get; set; } }
        public sealed class Derived : Base { private new IBar? X { get; set; } }
        """, null)]
    [Arguments("MethodOverload", "nameof(Base.X)", """
        public class Base { public void X(IFoo foo) { } }
        public sealed class Derived : Base { public void X(IBar bar) { } }
        """, null)]
    [Arguments("InterfaceMemberReimplementedOnRegisteredType", "nameof(IHasX.X)", """
        public interface IHasX { IFoo? X { get; set; } }
        public class Base : IHasX { public IFoo? X { get; set; } }
        public sealed class Derived : Base, IHasX { public new IFoo? X { get; set; } }
        """, null)]
    public async Task SGIOC024_MemberHiddenOnRegisteredType_ReportsDiagnostic(string scenario, string injectMember, string declarations, string? expectedReason)
    {
        // The generator binds the member by name on the registered type, so it does not inject a member hidden there
        var source = $$"""
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor(typeof(Derived), InjectMembers = [{{injectMember}}])]

            namespace TestNamespace;

            public interface IFoo;
            public interface IBar;

            {{declarations}}
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        if(expectedReason is null)
        {
            await Assert.That(sgioc024).IsEmpty().Because(scenario);
            return;
        }

        await Assert.That(sgioc024).Count().IsEqualTo(1).Because(scenario);
        await Assert.That(sgioc024[0].GetMessage()).Contains("'X'").And.Contains(expectedReason).Because(scenario);
    }

    [Test]
    [Arguments("nameof(Base<int>.X)", "public new IBar<T>? X { get; set; }", "it is hidden by 'Derived<T>.X'")]
    [Arguments("nameof(IHasX<int>.X)", "public new IBar<T>? X { get; set; }", "it is hidden by 'Derived<T>.X'")]
    [Arguments("nameof(Base<int>.X)", "", null)]
    public async Task SGIOC024_MemberHiddenOnOpenGenericRegisteredType_ReportsDiagnostic(string injectMember, string derivedMember, string? expectedReason)
    {
        var source = $$"""
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor(typeof(Derived<>), InjectMembers = [{{injectMember}}])]

            namespace TestNamespace;

            public interface IFoo<T>;
            public interface IBar<T>;

            public interface IHasX<T> { IFoo<T>? X { get; set; } }
            public class Base<T> : IHasX<T> { public IFoo<T>? X { get; set; } }
            public sealed class Derived<T> : Base<T> { {{derivedMember}} }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        if(expectedReason is null)
        {
            await Assert.That(sgioc024).IsEmpty();
            return;
        }

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains(expectedReason);
    }

    [Test]
    [Arguments("Property", "nameof(Base.X)", """
        public class Base { public IFoo? X { get; set; } }
        public sealed class Derived : Base { [IocInject] public new IBar? X { get; set; } }
        """)]
    [Arguments("Field", "nameof(Base.X)", """
        public class Base { public IFoo? X; }
        public sealed class Derived : Base { [IocInject] public new IBar? X; }
        """)]
    [Arguments("Method", "nameof(Base.X)", """
        public class Base { public void X(IFoo foo) { } }
        public sealed class Derived : Base { [IocInject] public new void X(IFoo foo) { } }
        """)]
    [Arguments("InterfaceMember", "nameof(IHasX.X)", """
        public interface IHasX { IFoo? X { get; set; } }
        public class Base : IHasX { public IFoo? X { get; set; } }
        public sealed class Derived : Base { [IocInject] public new IBar? X { get; set; } }
        """)]
    public async Task SGIOC024_MemberHiddenByIocInjectMember_NoDiagnostic(string scenario, string injectMember, string declarations)
    {
        // [IocInject] on the hiding member takes precedence over the InjectMembers element of the same name
        var source = $$"""
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor(typeof(Derived), InjectMembers = [{{injectMember}}])]

            namespace TestNamespace;

            public interface IFoo;
            public interface IBar;

            {{declarations}}
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).IsEmpty().Because(scenario);
    }

    [Test]
    [Arguments("StaticMethod", "nameof(Base.Init)", """
        public class Base { public static void Init(int value) { } }
        public sealed class Derived : Base { [IocInject] public void Init(IFoo foo) { } }
        """)]
    [Arguments("TaskMethodWithoutAsyncMethodInject", "nameof(Base.Init)", """
        public class Base { public Task Init(int value) => Task.CompletedTask; }
        public sealed class Derived : Base { [IocInject] public void Init(IFoo foo) { } }
        """)]
    [Arguments("MemberOfOtherType", "nameof(Other.Init)", """
        public sealed class Other { public void Init(int value) { } }
        public sealed class Derived { [IocInject] public void Init(IFoo foo) { } }
        """)]
    [Arguments("OverloadsWithoutInjectableOverload", "nameof(Base.Init)", """
        public class Base { public static void Init(int value) { } public static void Init(string value) { } }
        public sealed class Derived : Base { [IocInject] public void Init(IFoo foo) { } }
        """)]
    [Arguments("KeyedOverloadsWithoutInjectableOverload", "new object[] { nameof(Base.Init), \"key\" }", """
        public class Base { public static void Init(int value) { } public static void Init(string value) { } }
        public sealed class Derived : Base { [IocInject] public void Init(IFoo foo) { } }
        """)]
    public async Task SGIOC024_IocInjectMemberWithSameName_NoDiagnostic(string scenario, string injectMember, string declarations)
    {
        // The generator drops an InjectMembers element named like an [IocInject] member of the registered type
        // (MergeInjectionMembers), whatever member the element names, so it is never reported
        var source = $$"""
            using System.Threading.Tasks;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor(typeof(Derived), InjectMembers = [{{injectMember}}])]

            namespace TestNamespace;

            public interface IFoo;

            {{declarations}}
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024)).IsEmpty().Because(scenario);
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC043)).IsEmpty().Because(scenario);
    }

    [Test]
    [Arguments("InternalHidingMemberOfReferencedAssembly", "", 0)]
    [Arguments("InternalHidingMemberVisibleThroughInternalsVisibleTo", "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"TestAssembly\")]", 1)]
    public async Task SGIOC024_InternalHidingMemberOfReferencedAssembly_ReportsOnlyWhenAccessible(string scenario, string externalAttribute, int expectedCount)
    {
        // An internal member of another assembly is not visible to the generated code, so it does not hide Base.X there
        var externalSource = $$"""
            {{externalAttribute}}

            namespace External;

            public interface IFoo;
            public interface IBar;

            public class Base { public IFoo? X { get; set; } }
            public sealed class Derived : Base { internal new IBar? X { get; set; } }
            """;

        const string source = """
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(External.Derived), InjectMembers = [nameof(External.Base.X)])]
            """;

        var externalCompilation = SourceGeneratorTestHelper.CreateCompilation("ExternalAssembly", externalSource);
        var compilation = SourceGeneratorTestHelper.CreateCompilation("TestAssembly", source, [externalCompilation.ToMetadataReference()]);
        var diagnostics = await SourceGeneratorTestHelper.GetCheckedAnalyzerDiagnosticsAsync(compilation.WithAnalyzers([new RegisterAnalyzer()]));
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(expectedCount).Because(scenario);
        if(expectedCount > 0)
            await Assert.That(sgioc024[0].GetMessage()).Contains("it is hidden by 'Derived.X'").Because(scenario);
    }

    [Test]
    public async Task SGIOC024_NonStaticProperty_WithSetter_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.Dep)])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? Dep { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024);

        await Assert.That(sgioc024).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC024_NonStaticField_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.Dep)])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? Dep;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024);

        await Assert.That(sgioc024).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC024_VoidMethod_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.InjectDep)])]
            public static class MyModule { }

            public class MyService
            {
                public void InjectDep(IDependency dep) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024);

        await Assert.That(sgioc024).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC024_StaticProperty_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.Dep)])]
            public static class MyModule { }

            public class MyService
            {
                public static IDependency? Dep { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("Dep").And.Contains("static");
    }

    [Test]
    public async Task SGIOC024_PropertyWithoutSetter_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.Dep)])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? Dep { get; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("Dep").And.Contains("setter");
    }

    [Test]
    public async Task SGIOC024_PropertyWithPrivateSetter_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.Dep)])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? Dep { get; private set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("Dep").And.Contains("private");
    }

    [Test]
    public async Task SGIOC024_ReadonlyField_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.Dep)])]
            public static class MyModule { }

            public class MyService
            {
                public readonly IDependency? Dep;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("Dep").And.Contains("readonly");
    }

    [Test]
    public async Task SGIOC024_PrivateField_ReportsDiagnostic()
    {
        // When [IocRegisterFor] is on the class itself, nameof can resolve private members
        // but the generator cannot set them — SGIOC024 should fire
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(_dep)])]
            public class MyService
            {
                private IDependency? _dep;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("_dep").And.Contains("private");
    }

    [Test]
    public async Task SGIOC024_NonVoidMethod_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.GetDep)])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? GetDep(IDependency dep) => dep;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("GetDep").And.Contains("void");
    }

    [Test]
    public async Task SGIOC024_NonVoidMethod_WithAsyncMethodInjectEnabled_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.GetDepAsync)])]
            public static class MyModule { }

            public class MyService
            {
                public Task<int> GetDepAsync(IDependency dep) => Task.FromResult(0);
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage())
            .Contains("GetDepAsync")
            .And.Contains("void or non-generic Task");
    }

    [Test]
    public async Task SGIOC024_GenericMethod_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.InjectDep)])]
            public static class MyModule { }

            public class MyService
            {
                public void InjectDep<T>(T dep) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("InjectDep").And.Contains("generic");
    }

    [Test]
    public async Task SGIOC024_ProtectedProperty_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(Dep)])]
            public class MyService
            {
                protected IDependency? Dep { get; protected set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("Dep").And.Contains("not accessible");
    }

    [Test]
    public async Task SGIOC024_ProtectedField_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(Dep)])]
            public class MyService
            {
                protected IDependency? Dep;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("Dep").And.Contains("not accessible");
    }

    [Test]
    public async Task SGIOC024_PrivateProtectedField_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(_dep)])]
            public class MyService
            {
                private protected IDependency? _dep;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("_dep").And.Contains("not accessible");
    }

    [Test]
    public async Task SGIOC024_ProtectedMethod_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(InjectDep)])]
            public class MyService
            {
                protected void InjectDep(IDependency dep) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("InjectDep").And.Contains("not accessible");
    }

    [Test]
    public async Task SGIOC024_PrivateProtectedMethod_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(InjectDep)])]
            public class MyService
            {
                private protected void InjectDep(IDependency dep) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("InjectDep").And.Contains("not accessible");
    }

    [Test]
    public async Task SGIOC024_PrivateProtectedProperty_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(Dep)])]
            public class MyService
            {
                private protected IDependency? Dep { get; private protected set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("Dep").And.Contains("not accessible");
    }
}
