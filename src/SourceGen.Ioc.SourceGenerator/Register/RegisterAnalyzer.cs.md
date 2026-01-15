# Ioc Register Analyzer

## Diagnostics

Format: ID - Level - Category - Description

- SGIOC001 - Error - Usage - Invalid Attribute Usage
  - Report when IoCRegisterAttribute or IoCRegisterForAttribute is mark on private or abstract class.

- SGIOC002 - Error - Design - Circular Dependency Detected
  - Report when circular dependencies are detected among registered services.

- SGIOC003 - Error - Design - Service Lifetime Conflict Detected
  - Report when there are singleton service depending on scoped service.

- SGIOC004 - Error - Design - Dangerous Service Lifetime Dependency Detected
  - Report when there are singleton service depending on transient service.

- SGIOC005 - Error - Design - Dangerous Service Lifetime Dependency Detected
  - Report when there are scoped service depending on transient service.

- SGIOC006 - Warning - Usage - Duplicated Attribute Usage
  - Report when `FromKeyedServicesAttribute` and `InjectAttribute` mark on one parameter. `FromKeyedServicesAttribute` takes precedence.

- SGIOC007 - Error - Usage - Invalid Attribute Usage
  - Report when `InjectAttribute` is mark on static member, or member can not assign/invoke (private setter, setter not exists, private field, readonly field, private method), or mark on method and it is not return void.

- SGIOC008 - Error - Usage - Invalid Attribute Usage
  - Report when `IoCRegisterAttribute` or `IoCRegisterForAttribute` has specify `Factory` or `Instance` and use nameof(), but field/property/method in nameof() is not static or is inaccessible.

- SGIOC009 - Error - Usage - Invalid Attribute Usage
  - Report when `IoCRegisterAttribute` or `IoCRegisterForAttribute` has specify `Instance` and `Lifetime` is not Singleton.

- SGIOC010 - Error - Usage - Invalid Attribute Usage
  - Report when `IoCRegisterAttribute` or `IoCRegisterForAttribute` has specify `Factory` and `Instance` at same attribute. `Factory` takes precedence.

- SGIOC011 - Warning - Design - Duplicated Registration Detected
  - Report when there are duplicated registrations for same implement type, same key, and at least one matching tag.
  - When TagOnly=false, the registration is considered to have an empty tag for comparison.

- SGIOC012 - Warning - Design - Duplicated Registration Detected
  - Report when there are duplicated `IoCRegisterDefaults` for same target type and at least one matching tag.
  - When TagOnly=false, the setting is considered to have an empty tag for comparison.

- SGIOC013 - Error - Usage - Key type is unmatched
  - Report when `ServiceKeyAttribute` is mark on parameter, but the key type is not matched with the registered key type from `IoCRegisterAttribute` or `IoCRegisterForAttribute`.

- SGIOC014 - Error - Usage - Key does not exists
  - Report when `ServiceKeyAttribute` is mark on parameter, but there is no specified `Key` from `IoCRegisterAttribute` or `IoCRegisterForAttribute`.
