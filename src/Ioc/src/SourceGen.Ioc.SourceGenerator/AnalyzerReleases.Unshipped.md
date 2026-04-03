### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SGIOC001 | Usage | Error | Invalid Attribute Usage - IoCRegisterAttribute or IoCRegisterForAttribute is marked on private or abstract class.
SGIOC002 | Design | Error | Circular Dependency Detected - Circular dependencies are detected among registered services.
SGIOC003 | Design | Error | Service Lifetime Conflict Detected - Singleton service depending on Scoped service.
SGIOC004 | Design | Error | Dangerous Service Lifetime Dependency - Singleton service depending on Transient service.
SGIOC005 | Design | Error | Dangerous Service Lifetime Dependency - Scoped service depending on Transient service.
SGIOC006 | Usage | Warning | Duplicated Attribute Usage - Both FromKeyedServicesAttribute and InjectAttribute are marked on the same parameter.
SGIOC007 | Usage | Error | Invalid Attribute Usage - InjectAttribute is marked on static member, inaccessible member, or method that does not return void.
SGIOC008 | Usage | Error | Invalid Attribute Usage - Factory or Instance uses nameof() but the referenced member is not static or is inaccessible.
SGIOC009 | Usage | Error | Invalid Attribute Usage - Instance is specified but Lifetime is not Singleton.
SGIOC010 | Usage | Error | Invalid Attribute Usage - Both Factory and Instance are specified on the same attribute.
SGIOC011 | Design | Warning | Duplicated Registration Detected - Same implementation type and key are registered multiple times.
SGIOC012 | Design | Warning | Duplicated IoCRegisterDefaults Detected - Same target type has multiple default settings.
SGIOC013 | Usage | Error | Key type is unmatched - ServiceKeyAttribute parameter type does not match the registered key type.
SGIOC014 | Usage | Warning | Key does not exist - ServiceKeyAttribute is marked on parameter but no Key is registered.
SGIOC015 | Usage | Warning | KeyValuePair's Key type is unmatched - Injected KeyValuePair/Dictionary key type does not match any registered keyed service's key type.
SGIOC016 | Design | Error | Factory Method is unmatched - Generic factory method does not have IocGenericFactoryAttribute.
SGIOC017 | Design | Error | Generic Factory Method's type parameters are duplicated - Placeholder types in IocGenericFactoryAttribute must be unique.
SGIOC018 | Design | Error | Unable to resolve service - A dependency cannot be resolved when IntegrateServiceProvider is false.
SGIOC019 | Usage | Error | Container class must be partial - The class marked with IocContainerAttribute must be declared as partial.
SGIOC020 | Usage | Warning | UseSwitchStatement ignored with imported modules - UseSwitchStatement = true is ignored when container has IocImportModule attributes.
SGIOC021 | Design | Error | Unable to resolve partial accessor service - A partial method/property return type cannot be resolved when IntegrateServiceProvider is false.
SGIOC022 | Usage | Warning | Inject attribute ignored due to disabled feature - [IocInject]/[Inject] on property/field/method is ignored when its SourceGenIocFeatures flag is not enabled.
SGIOC023 | Usage | Error | Invalid InjectMembers element format - Each element in InjectMembers must be nameof(member) or new object[] { nameof(member), key [, KeyType] }.
SGIOC024 | Usage | Error | InjectMembers specifies non-injectable member - Members in InjectMembers must be injectable (instance properties with accessible setters, non-readonly fields, and ordinary non-generic void-returning methods, all of which must be public, internal, or protected internal).
SGIOC025 | Design | Error | Circular module import detected - A container has a circular [IocImportModule] dependency that would cause a static initializer deadlock.
SGIOC026 | Usage | Error | Invalid feature combination - AsyncMethodInject feature requires MethodInject to be enabled.
SGIOC027 | Design | Error | Partial accessor must return Task of T for async-init service - The matched implementation has async inject methods but the accessor returns plain TService.
SGIOC028 | Usage | Warning | async void injection method cannot be awaited - [IocInject] method is declared as async void.
SGIOC029 | Design | Error | Unsupported async partial accessor type - Partial accessor targets an async-init service but returns an async type other than Task of T.
SGIOC030 | Usage | Error | Synchronous dependency requested for async-init service - Consumer requests T but the service has async inject methods and no synchronous registration exists.
