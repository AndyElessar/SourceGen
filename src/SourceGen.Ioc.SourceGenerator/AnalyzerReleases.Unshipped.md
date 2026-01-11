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
