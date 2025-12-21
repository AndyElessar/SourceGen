# Service collection register source generator

This source generator automatically generates extension methods for registering services in Microsoft.Extensions.DependencyInjection.

## Collect information

1. Find classes marked with `SourceGen.Ioc.IoCRegisterAttribute` and `SourceGen.Ioc.IoCRegisterForAttribute`.

2. Find `SourceGen.Ioc.IoCRegisterDefaultSettingsAttribute`. If exists, apply its settings as defaults to classes that step 1 finds, unless they override them with their own attributes.

3. Service registration settings to collect:
   - Service type (from `TargetServiceType`, `ServiceTypes` or follow settings: `RegisterAllInterfaces`, `RegisterAllBaseClasses`; always register Implementation type itself)
   - Implementation type (from `IoCRegisterForAttribute.TargetType` or the class marked with `IoCRegisterAttribute`)
   - Lifetime (from `IoCRegisterAttribute` or default settings)
   - Key (from `IoCRegisterAttribute.Key`, `IoCRegisterForAttribute.Key` or default settings)
   - Project root namespace (from compilation options)
   - Project name (from compilation options)

4. When `KeyType` is `KeyType.Value`, take `Key` as value, when `KeyType` is `KeyType.Csharp`, take `Key` as C# code snippet.
   - primitive types (int, string, bool, etc.) should be represented as itself (e.g., `42`, `"myString"`, `true`)
   - enum types should be represented as `EnumType.EnumValue`

5. When Service type is generic type and Implementation type is closed type, make sure to register with closed generic type.

6. When multiple default settings are found on an implementation type, use settings:
    1. The one directly on the implementation type
    2. The one on the closest base class
    3. The one on the first interface in `RegistrationData.AllInterfaces`

Generated code example:
```csharp
namespace {ProjectRootNamespace};

public static class ServiceCollectionExtensions
{
	public static IServiceCollection Add{ProjectName}(this IServiceCollection services)
	{
		services.AddTransient<IMyService, MyServiceImplementation>();
		services.AddSingleton(typeof(IGenericService<>), typeof(GenericServiceImplementation<>));
		services.AddScoped(typeof(IGenericService<string>), typeof(GenericServiceStringImplementation));
		services.AddTransient(typeof(IMyKeyedService), provider => new MyKeyedServiceImplementation("MyKey"));
		services.AddTransient(typeof(IMyKeyedService), provider => new MyKeyedServiceImplementation(MyEnum.EnumValue));
		services.AddTransient(typeof(IMyKeyedService), provider => new MyKeyedServiceImplementation(MyClass.StaticValue));
		
		return services;
	}
}
```