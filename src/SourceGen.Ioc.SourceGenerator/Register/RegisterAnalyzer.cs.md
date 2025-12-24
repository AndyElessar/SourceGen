# Ioc Register Analyzer

## Diagnostics
Format: ID - Level - Category - Description

1. SGIOC001 - Error - Usage - Invalid Attribute Usage
    - Report when IoCRegisterAttribute or IoCRegisterForAttribute is mark on private or abstract class.

2. SGIOC002 - Error - Design - Circular Dependency Detected
    - Report when circular dependencies are detected among registered services.

3. SGIOC003 - Error - Design - Service Lifetime Conflict Detected
    - Report when there are conflicting service lifetimes among registered services, like singleton depending on scoped.

4. SGIOC004 - Error - Design - Nested OpenGeneric Detected
    - Report when there are service is implementing nested open generic interfaces/class, which is not allow to register.

5. SGIOC101 - Warnings - Design - Service Lifetime Conflict Detected
    - Report when there are conflicting service lifetimes among registered services, like scoped depending on transient.