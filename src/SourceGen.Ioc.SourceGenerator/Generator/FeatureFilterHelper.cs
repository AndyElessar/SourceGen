namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private static ServiceRegistrationModel FilterRegistrationForFeatures(ServiceRegistrationModel registration, IocFeatures features)
    {
        if(IocFeaturesHelper.HasAllInjectionFeatures(features))
            return registration;

        var filteredInjectionMembers = FilterInjectionMembers(registration.InjectionMembers, features);

        List<TypeData>? filteredDecorators = null;
        for(var i = 0; i < registration.Decorators.Length; i++)
        {
            var decorator = registration.Decorators[i];
            if(decorator.InjectionMembers is not { } injectionMembers)
            {
                filteredDecorators?.Add(decorator);

                continue;
            }

            var filteredDecoratorMembers = FilterInjectionMembers(injectionMembers, features);
            if(filteredDecoratorMembers.Length == injectionMembers.Length)
            {
                filteredDecorators?.Add(decorator);

                continue;
            }

            filteredDecorators ??= new List<TypeData>(registration.Decorators.Length);
            if(filteredDecorators.Count == 0)
            {
                for(var j = 0; j < i; j++)
                    filteredDecorators.Add(registration.Decorators[j]);
            }

            filteredDecorators.Add(decorator with { InjectionMembers = filteredDecoratorMembers });
        }

        if(filteredDecorators is null && filteredInjectionMembers.Length == registration.InjectionMembers.Length)
            return registration;

        return registration with
        {
            InjectionMembers = filteredInjectionMembers,
            Decorators = filteredDecorators is null
                ? registration.Decorators
                : filteredDecorators.ToImmutableEquatableArray()
        };
    }

    private static ImmutableEquatableArray<InjectionMemberData> FilterInjectionMembers(
        ImmutableEquatableArray<InjectionMemberData> members,
        IocFeatures features)
    {
        if(members.Length == 0 || IocFeaturesHelper.HasAllInjectionFeatures(features))
            return members;

        List<InjectionMemberData>? filtered = null;
        for(var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if(IocFeaturesHelper.IsInjectionFeatureEnabled(member.MemberType, features))
            {
                filtered?.Add(member);
                continue;
            }

            filtered ??= new List<InjectionMemberData>(members.Length);
            if(filtered.Count == 0)
            {
                for(var j = 0; j < i; j++)
                    filtered.Add(members[j]);
            }
        }

        return filtered is null ? members : filtered.ToImmutableEquatableArray();
    }
}