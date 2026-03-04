# Basic Registration

## Overview

Basic registration covers fundamental service registration scenarios including simple type registration, keyed services with different key types, and automatic interface/base class registration.

## Feature: Basic Registration

- Always register Implementation type itself.
- When Service type is open generic type and Implementation type is closed type, make sure to register with closed generic type.

### Keyed Service Registration

```csharp
#region Define:
public interface IMyService;
[IocRegister<IMyService>(ServiceLifetime.Transient)]
internal class MyServiceImplementation : IMyService;

public interface IGenericService<T>;
[IocRegister(ServiceLifetime.Singleton, typeof(IGenericService<>))]
internal class GenericServiceImplementation<T> : IGenericService<T>;
[IocRegister(ServiceLifetime.Scoped, typeof(IGenericService<>))]
internal class ClosedGenericServiceStringImplementation : IGenericService<string>;

public interface IMyKeyedService;
[IocRegister<IMyKeyedService>(ServiceLifetime.Transient, Key = "MyKey")]
public class MyKeyedServiceImplementation1;
[IocRegister<IMyKeyedService>(ServiceLifetime.Transient, Key = MyEnum.EnumValue)]
public class MyKeyedServiceImplementation2;
[IocRegister<IMyKeyedService>(ServiceLifetime.Transient, Key = nameof(MyClass.StaticValue), KeyType = KeyType.Csharp)]
public class MyKeyedServiceImplementation3;

public static class MyClass
{
    public static Guid StaticValue => Guid.NewGuid();
}
#endregion

#region Generate:
namespace {ProjectRootNamespace};

public static class ServiceCollectionExtensions
{
    public static IServiceCollection Add{ProjectName}(this IServiceCollection services)
    {
        //always register Implementation type itself
        services.AddTransient<MyServiceImplementation, MyServiceImplementation>();
        services.AddTransient<IMyService>(sp=>sp.GetRequiredService<MyServiceImplementation>());
        services.AddSingleton(typeof(IGenericService<>), typeof(GenericServiceImplementation<>));
        services.AddScoped<ClosedGenericServiceStringImplementation, ClosedGenericServiceStringImplementation>();
        services.AddScoped<IGenericService<string>>(sp=>sp.GetRequiredService<ClosedGenericServiceStringImplementation>());
        services.AddKeyedTransient<MyKeyedServiceImplementation1, MyKeyedServiceImplementation1>("MyKey");
        services.AddKeyedTransient<IMyKeyedService>("MyKey", (sp, key)=>sp.GetKeyedService<MyKeyedServiceImplementation1>());
        services.AddKeyedTransient<MyKeyedServiceImplementation2, MyKeyedServiceImplementation2>(MyEnum.EnumValue);
        services.AddKeyedTransient<IMyKeyedService>(MyEnum.EnumValue, (sp, key)=>sp.GetKeyedService<MyKeyedServiceImplementation2>());
        services.AddKeyedTransient<MyKeyedServiceImplementation3, MyKeyedServiceImplementation3>(MyClass.StaticValue);
        services.AddKeyedTransient<IMyKeyedService>(MyClass.StaticValue, (sp, key)=>sp.GetKeyedService<MyKeyedServiceImplementation3>());
        
        return services;
    }
}
#endregion
```

## See Also

- [Keyed Services](Register.Basic.md#keyed-service-registration) (this feature)
- [Registration Attributes](SPEC.md)
- [Decorators](Register.Decorators.md)
- [Generics](Register.Generics.md)
