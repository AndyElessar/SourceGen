namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Categorizes injection members into properties/fields and methods.
    /// </summary>
    private static (List<InjectionMemberData>? Properties, List<InjectionMemberData>? Methods) CategorizeInjectionMembers(
        ImmutableEquatableArray<InjectionMemberData> injectionMembers)
    {
        List<InjectionMemberData>? properties = null;
        List<InjectionMemberData>? methods = null;

        foreach(var member in injectionMembers)
        {
            if(member.MemberType is InjectionMemberType.Property or InjectionMemberType.Field)
            {
                properties ??= [];
                properties.Add(member);
            }
            else if(member.MemberType == InjectionMemberType.Method)
            {
                methods ??= [];
                methods.Add(member);
            }
        }

        return (properties, methods);
    }
}
