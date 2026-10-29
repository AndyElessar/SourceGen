namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Categorizes injection members into properties/fields and methods.
    /// </summary>
    private static (List<PropertyOrFieldInjectionData>? Properties, List<MethodInjectionData>? Methods) CategorizeInjectionMembers(
        ImmutableEquatableArray<InjectionMemberData> injectionMembers)
    {
        List<PropertyOrFieldInjectionData>? properties = null;
        List<MethodInjectionData>? methods = null;

        foreach(var member in injectionMembers)
        {
            switch(member)
            {
                case PropertyOrFieldInjectionData propertyOrField:
                    properties ??= [];
                    properties.Add(propertyOrField);
                    break;

                case MethodInjectionData { IsAsync: false } method:
                    methods ??= [];
                    methods.Add(method);
                    break;
            }
        }

        return (properties, methods);
    }
}
