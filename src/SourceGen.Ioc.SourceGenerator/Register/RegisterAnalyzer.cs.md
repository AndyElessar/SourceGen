# Ioc Register Analyzer

## Diagnostics
Format: ID - Level - Category - Description

1. SGIOC001 - Error - Usage - Invalid Attribute Usage
    - Report when IoCRegisterAttribute or IoCRegisterForAttribute is mark on private or abstract class.

2. SGIOC002 - Error - Design - Circular Dependency Detected
    - Report when circular dependencies are detected among registered services.

3. SGIOC003 - Error - Design - Service Lifetime Conflict Detected
    - Report when there are singleton service depending on scoped service.

4. SGIOC004 - Error - Design - Dangerous Service Lifetime Dependency Detected
    - Report when there are singleton service depending on transient service.

5. SGIOC005 - Error - Design - Dangerous Service Lifetime Dependency Detected
    - Report when there are scoped service depending on transient service.

6. SGIOC006 - Error - Design - Nested OpenGeneric Detected
    - Report when a service implements an interface or inherits from a class where a type argument is itself a generic type containing unbound type parameters.
    - Example (Error): `class Handler<T> : IHandler<Wrapper<T>>` - `Wrapper<T>` is a generic type with unbound `T` used as type argument.
    - Example (OK): `class Handler<T> : IHandler<T>` - `T` is directly used as type argument, not wrapped in another generic.
    - Example (OK): `class Handler : IHandler<Wrapper<string>>` - `Wrapper<string>` is a closed generic type.

7. SGIOC007 - Error - Usage - Invalid Attribute Usage
    - Report when InjectAttribute is mark on static member, or member can not assign/invoke (private setter, setter not exists, private field, readonly field, private method), or mark on method and it is not return void.