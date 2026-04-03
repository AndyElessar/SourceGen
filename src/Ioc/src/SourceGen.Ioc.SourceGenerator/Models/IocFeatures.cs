#nullable enable

namespace SourceGen.Ioc.SourceGenerator.Models;

[Flags]
internal enum IocFeatures
{
    None = 0,
    Register = 1 << 0,
    Container = 1 << 1,
    PropertyInject = 1 << 2,
    FieldInject = 1 << 3,
    MethodInject = 1 << 4,
    AsyncMethodInject = 1 << 5,
    Default = Register | Container | PropertyInject | MethodInject
}

internal static class IocFeaturesHelper
{
    private const IocFeatures AllInjectionFeatures = IocFeatures.PropertyInject | IocFeatures.FieldInject | IocFeatures.MethodInject | IocFeatures.AsyncMethodInject;

    public static bool HasAllInjectionFeatures(IocFeatures features) => (features & AllInjectionFeatures) == AllInjectionFeatures;

    public static bool IsInjectionFeatureEnabled(InjectionMemberType memberType, IocFeatures features) =>
        memberType switch
        {
            InjectionMemberType.Property => (features & IocFeatures.PropertyInject) != 0,
            InjectionMemberType.Field => (features & IocFeatures.FieldInject) != 0,
            InjectionMemberType.Method => (features & IocFeatures.MethodInject) != 0,
            InjectionMemberType.AsyncMethod => (features & IocFeatures.AsyncMethodInject) != 0,
            _ => false
        };

    public static IocFeatures Parse(string? rawFeatures)
    {
        if(string.IsNullOrWhiteSpace(rawFeatures))
            return IocFeatures.Default;

        var featuresValue = rawFeatures!;
        var features = IocFeatures.None;
        foreach(var rawToken in featuresValue.Split([','], StringSplitOptions.RemoveEmptyEntries))
        {
            var token = rawToken.Trim();
            if(token.Equals("register", StringComparison.OrdinalIgnoreCase))
                features |= IocFeatures.Register;
            else if(token.Equals("container", StringComparison.OrdinalIgnoreCase))
                features |= IocFeatures.Container;
            else if(token.Equals("propertyinject", StringComparison.OrdinalIgnoreCase))
                features |= IocFeatures.PropertyInject;
            else if(token.Equals("fieldinject", StringComparison.OrdinalIgnoreCase))
                features |= IocFeatures.FieldInject;
            else if(token.Equals("methodinject", StringComparison.OrdinalIgnoreCase))
                features |= IocFeatures.MethodInject;
            else if(token.Equals("asyncmethodinject", StringComparison.OrdinalIgnoreCase))
                features |= IocFeatures.AsyncMethodInject;
        }

        return features;
    }
}
