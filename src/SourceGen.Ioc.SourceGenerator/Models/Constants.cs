namespace SourceGen.Ioc.SourceGenerator.Models;

internal static class Constants
{
    public const string IoCRegisterAttributeFullName = "SourceGen.Ioc.IoCRegisterAttribute";
    public const string IoCRegisterForAttributeFullName = "SourceGen.Ioc.IoCRegisterForAttribute";
    public const string IoCRegisterDefaultSettingsAttributeFullName = "SourceGen.Ioc.IoCRegisterDefaultSettingsAttribute";

    public const string Category_Usage = "Usage";
    public const string Category_Design = "Design";

    extension(ServiceLifetime lifetime)
    {
        public string Name =>
            lifetime switch
            {
                ServiceLifetime.Singleton => "Singleton",
                ServiceLifetime.Scoped => "Scoped",
                ServiceLifetime.Transient => "Transient",
                _ => lifetime.ToString()
            };
    }

    extension(AttributeData attribute)
    {
        public (bool HasArg, ServiceLifetime Lifetime) TryGetLifetime()
        {
            var (hasArg, val) = attribute.TryGetNamedArgument<int>("Lifetime", 0);// Default is ServiceLifetime.Singleton
            return (hasArg, (ServiceLifetime)val);
        }

        public (bool HasArg, bool Value) TryGetRegisterAllInterfaces() =>
            attribute.TryGetNamedArgument<bool>("RegisterAllInterfaces", false);

        public (bool HasArg, bool Value) TryGetRegisterAllBaseClasses() =>
            attribute.TryGetNamedArgument<bool>("RegisterAllBaseClasses", false);

        public ImmutableEquatableArray<TypeData> GetServiceTypes() =>
            attribute.GetTypeArrayArgument("ServiceTypes");

        public ImmutableEquatableArray<TypeData> GetDecorators() =>
            attribute.GetDecoratorTypeArrayArgument("Decorators");
    }
}
