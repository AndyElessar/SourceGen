using Microsoft.CodeAnalysis;
using SourceGen.Ioc.Emit;
using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Test.Emit;

/// <summary>The C# name that the generated code uses for a type reference.</summary>
public class TypeNamesTests
{
    private const string Source = """
        namespace Ns
        {
            public class Outer<T>
            {
                public class Inner<U>;
                public class Plain;
            }

            public class Probe
            {
                public Outer<int>.Inner<string> NestedGeneric;
                public Outer<int>.Plain NestedPlain;
                public System.Collections.Generic.Dictionary<string, int[]>[,] ArrayOfGeneric;
                public @class.@event.Keyword Keyword;
                public GlobalType Global;
                public (int a, string b) Tuple;
                public int[][,] JaggedOfRank2;
                public int[,][] Rank2OfJagged;
            }
        }

        namespace @class.@event
        {
            public class Keyword;
        }

        public class GlobalType;
        """;

    private static readonly Compilation Compilation = SourceGeneratorTestHelper.CreateCompilation([Source]);

    [Test]
    [Arguments("NestedGeneric", "global::Ns.Outer<global::System.Int32>.Inner<global::System.String>")]
    [Arguments("NestedPlain", "global::Ns.Outer<global::System.Int32>.Plain")]
    [Arguments("ArrayOfGeneric", "global::System.Collections.Generic.Dictionary<global::System.String, global::System.Int32[]>[,]")]
    [Arguments("Keyword", "global::@class.@event.Keyword")]
    [Arguments("Global", "global::GlobalType")]
    [Arguments("Tuple", "global::System.ValueTuple<global::System.Int32, global::System.String>")]
    [Arguments("JaggedOfRank2", "global::System.Int32[][,]")]
    [Arguments("Rank2OfJagged", "global::System.Int32[,][]")]
    public async Task CSharp_Field_GivesQualifiedName(string field, string expected)
    {
        INamedTypeSymbol probe = Compilation.GetTypeByMetadataName("Ns.Probe")!;
        TypeRef type = TypeRefFactory.Create(((IFieldSymbol)probe.GetMembers(field).Single()).Type, Compilation)!;

        string name = TypeNames.CSharp(type);

        await Assert.That(name).IsEqualTo(expected);
        await Assert.That(Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error)).IsEmpty();
    }

    [Test]
    public async Task GeneratedNames_Fnv1a_IsStable()
    {
        await Assert.That(GeneratedNames.Fnv1a(string.Empty)).IsEqualTo(2166136261u);
        await Assert.That(GeneratedNames.Fnv1a("a")).IsEqualTo(GeneratedNames.Fnv1a("a"));
        await Assert.That(GeneratedNames.Fnv1a("a")).IsNotEqualTo(GeneratedNames.Fnv1a("b"));
    }
}
