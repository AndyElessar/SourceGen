# Import Module

## Overview

Import module functionality allows you to share registrations from one module assembly to another. Use `[IocImportModule]` to import default settings and registrations from a module container into your application.

## Feature: Import Module

When a class marked with `IocImportModuleAttribute` (or `IocImportModuleAttribute<T>`), generator will get `IocImportModuleAttribute.ModuleType`'s (or `T` type's) assembly's `IocRegisterDefaultsAttribute` as default settings for current assembly.

See [Container Module Import](Container.ImportModule.md) for container-level module import details.

## See Also

- [Registration Attributes](SPEC.md)
- [Default Settings](SPEC.md)
- [Container Module Import](Container.ImportModule.md)
