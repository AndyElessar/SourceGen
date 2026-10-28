# Basic Registration

## Overview

Basic registration covers fundamental service registration scenarios including simple type registration, keyed services with different key types, and automatic interface/base class registration.

## Feature: Basic Registration

- Always register Implementation type itself.
- When Service type is open generic type and Implementation type is closed type, register each closed form of the service type that the implementation implements or inherits (e.g. `typeof(IGenericService<>)` on `Impl : IGenericService<string>` registers `IGenericService<string>`). Without such a form, the service type is skipped (SGIOC032).
- When both are open generic, register `typeof(Service<>)` with `typeof(Implementation<>)` only when the service type's type arguments, as implemented, are the implementation's type parameters in declaration order. Open generic registrations close the implementation with the service type's type arguments by position, so `TextHandler<T> : IHandler<T, string>` and `SwappedHandler<TResponse, TRequest> : IHandler<TRequest, TResponse>` are not registered as `IHandler<,>`; their closed service types are registered when discovered (see [Generic Registration](Register.Generics.spec.md)).

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
public enum MyEnum { EnumValue }
[IocRegister<IMyKeyedService>(ServiceLifetime.Transient, Key = "MyKey")]
public class MyKeyedServiceImplementation1 : IMyKeyedService;
[IocRegister<IMyKeyedService>(ServiceLifetime.Transient, Key = MyEnum.EnumValue)]
public class MyKeyedServiceImplementation2 : IMyKeyedService;
[IocRegister<IMyKeyedService>(ServiceLifetime.Transient, Key = nameof(MyClass.StaticValue), KeyType = KeyType.Csharp)]
public class MyKeyedServiceImplementation3 : IMyKeyedService;

public static class MyClass
{
    public static Guid StaticValue => Guid.NewGuid();
}
#endregion

#region Generate:
namespace {RootNamespace}
{
    [GeneratedCode("SourceGen.Ioc", "{Version}")]
    public static class {AssemblyName}ServiceCollectionExtensions
    {
        public static IServiceCollection Add{AssemblyName}(this IServiceCollection services, params IEnumerable<string> tags)
        {
            if (!tags.Any())
            {
                // Always register the implementation type itself
                services.AddSingleton(typeof(GenericServiceImplementation<>), typeof(GenericServiceImplementation<>));
                services.AddSingleton(typeof(IGenericService<>), typeof(GenericServiceImplementation<>));
                services.AddScoped<ClosedGenericServiceStringImplementation, ClosedGenericServiceStringImplementation>();
                services.AddScoped<IGenericService<string>>((IServiceProvider sp) => sp.GetRequiredService<ClosedGenericServiceStringImplementation>());
                services.AddTransient<MyServiceImplementation, MyServiceImplementation>();
                services.AddTransient<IMyService>((IServiceProvider sp) => sp.GetRequiredService<MyServiceImplementation>());
                services.AddKeyedTransient<MyKeyedServiceImplementation1, MyKeyedServiceImplementation1>("MyKey");
                services.AddKeyedTransient<IMyKeyedService>("MyKey", (IServiceProvider sp, object? key) => sp.GetRequiredKeyedService<MyKeyedServiceImplementation1>(key));
                services.AddKeyedTransient<MyKeyedServiceImplementation2, MyKeyedServiceImplementation2>(MyEnum.EnumValue);
                services.AddKeyedTransient<IMyKeyedService>(MyEnum.EnumValue, (IServiceProvider sp, object? key) => sp.GetRequiredKeyedService<MyKeyedServiceImplementation2>(key));
                services.AddKeyedTransient<MyKeyedServiceImplementation3, MyKeyedServiceImplementation3>(MyClass.StaticValue);
                services.AddKeyedTransient<IMyKeyedService>(MyClass.StaticValue, (IServiceProvider sp, object? key) => sp.GetRequiredKeyedService<MyKeyedServiceImplementation3>(key));
            }

            return services;
        }
    }
}
#endregion
```

## See Also

- [Keyed Services](Register.Basic.spec.md#keyed-service-registration) (this feature)
- [Registration Attributes](SPEC.spec.md)
- [Decorators](Register.Decorators.spec.md)
- [Generics](Register.Generics.spec.md)
