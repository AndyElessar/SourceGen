using Microsoft.CodeAnalysis;
using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Test.Model;

/// <summary>The type model, normalization, accessibility, and canonical string (Analysis.Model.spec, "Type model").</summary>
public class TypeRefTests
{
    private const string Source = """
        using System.Collections.Generic;

        namespace Ns
        {
            public class Pub
            {
                protected class Prot;
                public class Nested;
            }

            internal class Internal;

            internal class InternalOuter
            {
                public class PublicInner;
            }

            public class Outer<T>
            {
                public class Inner<U>;
            }

            public class Handler<T>;

            public unsafe class Probe
            {
                public int[] Array;
                public int[,] Array2;
                public List<int>[] ListArray;
                public (int a, string b) Tuple;
                public System.ValueTuple<int, string> ValueTuple;
                public string? NullableString;
                public string String;
                public dynamic Dynamic;
                public object Object;
                public nint NativeInt;
                public System.IntPtr IntPtr;
                public int? NullableInt;
                public Outer<int>.Inner<string> NestedGeneric;
                public List<Internal> ListOfInternal;
                public int* Pointer;
                public List<(int a, string b)> ListOfTuple;
                public List<System.ValueTuple<int, string>> ListOfValueTuple;
                public List<dynamic> ListOfDynamic;
                public List<object> ListOfObject;
                public List<nint> ListOfNint;
                public List<System.IntPtr> ListOfIntPtr;
                public List<string?> ListOfNullableString;
                public List<string> ListOfString;
                public (int a, int b, int c, int d, int e, int f, int g, int h) LongTuple;
                public System.ValueTuple<int, int, int, int, int, int, int, System.ValueTuple<int>> LongValueTuple;
                public Internal[] ArrayOfInternal;
            }
        }

        namespace @class.Ns
        {
            public class Keyword;
        }
        """;

    private static readonly Compilation Compilation = SourceGeneratorTestHelper.CreateCompilation([Source]);

    private static TypeRef? Field(string name)
    {
        INamedTypeSymbol probe = Compilation.GetTypeByMetadataName("Ns.Probe")!;
        return TypeRefFactory.Create(((IFieldSymbol)probe.GetMembers(name).Single()).Type, Compilation);
    }

    private static TypeRef? Type(string metadataName) => TypeRefFactory.Create(Compilation.GetTypeByMetadataName(metadataName)!, Compilation);

    [Test]
    [Arguments("Array", "System.Int32[]")]
    [Arguments("Array2", "System.Int32[,]")]
    [Arguments("ListArray", "System.Collections.Generic.List`1<System.Int32>[]")]
    [Arguments("NullableInt", "System.Nullable`1<System.Int32>")]
    [Arguments("NestedGeneric", "Ns.Outer`1+Inner`1<System.Int32,System.String>")]
    public async Task Create_Field_GivesSortName(string field, string sortName)
        => await Assert.That(Field(field)!.SortName).IsEqualTo(sortName);

    [Test]
    [Arguments("Tuple", "ValueTuple")]
    [Arguments("NullableString", "String")]
    [Arguments("Dynamic", "Object")]
    [Arguments("NativeInt", "IntPtr")]
    [Arguments("ListOfTuple", "ListOfValueTuple")]
    [Arguments("ListOfDynamic", "ListOfObject")]
    [Arguments("ListOfNint", "ListOfIntPtr")]
    [Arguments("ListOfNullableString", "ListOfString")]
    [Arguments("LongTuple", "LongValueTuple")]
    public async Task Create_TypeThatRunTimeTypeDecides_IsNormalized(string field, string sameAs)
    {
        TypeRef? actual = Field(field);
        TypeRef? expected = Field(sameAs);

        await Assert.That(actual).IsEqualTo(expected);
        await Assert.That(actual!.GetHashCode()).IsEqualTo(expected!.GetHashCode());
    }

    [Test]
    public async Task Create_TypeOfThisAssembly_CanonicalStringHasAssembly()
        => await Assert.That(Type("Ns.Pub")!.CanonicalString).IsEqualTo("[TestAssembly]Ns.Pub");

    [Test]
    public async Task Create_GenericDefinitionAndUnboundGeneric_HaveTypeParameterOrNoArguments()
    {
        INamedTypeSymbol definition = Compilation.GetTypeByMetadataName("Ns.Handler`1")!;

        NamedTypeRef open = (NamedTypeRef)TypeRefFactory.Create(definition, Compilation)!;
        NamedTypeRef unbound = (NamedTypeRef)TypeRefFactory.Create(definition.ConstructUnboundGenericType(), Compilation)!;

        await Assert.That(open.SortName).IsEqualTo("Ns.Handler`1<!0>");
        await Assert.That(open.IsUnboundGeneric).IsFalse();
        await Assert.That(unbound.SortName).IsEqualTo("Ns.Handler`1");
        await Assert.That(unbound.IsUnboundGeneric).IsTrue();
        await Assert.That(open.Definition).IsEqualTo(unbound);
    }

    [Test]
    public async Task Create_NestedGenericDefinition_IndexesTypeParametersFlat()
    {
        INamedTypeSymbol inner = Compilation.GetTypeByMetadataName("Ns.Outer`1+Inner`1")!;

        await Assert.That(TypeRefFactory.Create(inner, Compilation)!.SortName).IsEqualTo("Ns.Outer`1+Inner`1<!0,!1>");
        await Assert.That(TypeRefFactory.Create(inner.ConstructUnboundGenericType(), Compilation)!.SortName).IsEqualTo("Ns.Outer`1+Inner`1");
    }

    [Test]
    public async Task Create_KeywordNamespace_UsesMetadataName()
        => await Assert.That(Type("class.Ns.Keyword")!.SortName).IsEqualTo("class.Ns.Keyword");

    [Test]
    public async Task Create_ErrorTypeInArgument_ReturnsNull()
    {
        INamedTypeSymbol list = Compilation.GetTypeByMetadataName("System.Collections.Generic.List`1")!;

        await Assert.That(TypeRefFactory.Create(list.Construct(Compilation.CreateErrorTypeSymbol(null, "Missing", 0)), Compilation)).IsNull();
    }

    [Test]
    public async Task Create_ArrayOfInternal_NotFullyAccessible()
        => await Assert.That(Field("ArrayOfInternal")!.IsFullyAccessible).IsFalse();

    [Test]
    [Arguments("Ns.Pub", true)]
    [Arguments("Ns.Pub+Nested", true)]
    [Arguments("Ns.Pub+Prot", false)]
    [Arguments("Ns.Internal", false)]
    [Arguments("Ns.InternalOuter+PublicInner", false)]
    public async Task Create_NamedType_AccessibleOnlyWhenEachLevelIsPublic(string metadataName, bool accessible)
    {
        NamedTypeRef type = (NamedTypeRef)Type(metadataName)!;

        await Assert.That(type.IsAccessible).IsEqualTo(accessible);
        await Assert.That(type.IsFullyAccessible).IsEqualTo(accessible);
    }

    [Test]
    public async Task Create_PublicTypeWithInternalArgument_NodeAccessibleButNotFully()
    {
        NamedTypeRef type = (NamedTypeRef)Field("ListOfInternal")!;

        await Assert.That(type.IsAccessible).IsTrue();
        await Assert.That(type.IsFullyAccessible).IsFalse();
    }

    [Test]
    public async Task Create_PointerOrErrorType_ReturnsNull()
    {
        await Assert.That(Field("Pointer")).IsNull();
        await Assert.That(TypeRefFactory.Create(Compilation.CreateErrorTypeSymbol(null, "Missing", 0), Compilation)).IsNull();
    }

    [Test]
    public async Task Equals_OneSideHasCachedStrings_StillEqual()
    {
        TypeRef read = Field("ListArray")!;
        TypeRef fresh = Field("ListArray")!;

        _ = read.CanonicalString;
        _ = read.SortName;

        await Assert.That(read).IsNotSameReferenceAs(fresh);
        await Assert.That(read).IsEqualTo(fresh);
        await Assert.That(read.GetHashCode()).IsEqualTo(fresh.GetHashCode());
    }

    [Test]
    public async Task With_SourceHasCachedStrings_CopyBuildsItsOwn()
    {
        NamedTypeRef list = (NamedTypeRef)Field("ListOfString")!;
        _ = list.CanonicalString;
        _ = list.SortName;

        NamedTypeRef definition = list.Definition;

        await Assert.That(definition.SortName).IsEqualTo("System.Collections.Generic.List`1");
        await Assert.That(definition.CanonicalString).EndsWith("]System.Collections.Generic.List`1");
    }

    [Test]
    public async Task With_ArrayWithOtherElement_BuildsItsOwnStrings()
    {
        ArrayTypeRef array = new(new TypeParameterRef(0), Rank: 1);
        _ = array.SortName;

        ArrayTypeRef copy = array with { Element = new TypeParameterRef(1), Rank = 2 };

        await Assert.That(copy.SortName).IsEqualTo("!1[,]");
        await Assert.That(array.SortName).IsEqualTo("!0[]");
    }

    [Test]
    public async Task Equals_DifferentKindsOrMembers_NotEqual()
    {
        TypeRef parameter = new TypeParameterRef(0);

        await Assert.That(parameter).IsNotEqualTo(new TypeParameterRef(1));
        await Assert.That(parameter).IsNotEqualTo(new ArrayTypeRef(parameter, 1));
        await Assert.That(new ArrayTypeRef(parameter, 1)).IsNotEqualTo(new ArrayTypeRef(parameter, 2));
        await Assert.That(Field("Array")).IsNotEqualTo(Field("ListArray"));
    }

    [Test]
    public async Task CanonicalString_TypeParameterAndArray_FollowSpec()
    {
        TypeRef type = new ArrayTypeRef(
            new NamedTypeRef("System.Runtime", "System.Collections.Generic.List`1", [new TypeParameterRef(0)], IsAccessible: true),
            Rank: 1);

        await Assert.That(type.CanonicalString).IsEqualTo("[System.Runtime]System.Collections.Generic.List`1<!0>[]");
        await Assert.That(type.SortName).IsEqualTo("System.Collections.Generic.List`1<!0>[]");
    }
}
