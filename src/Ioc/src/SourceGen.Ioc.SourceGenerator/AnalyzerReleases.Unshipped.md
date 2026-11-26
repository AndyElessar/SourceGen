### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SGIOC001 | SourceGen.Ioc | Error | Inconsistent registration settings
SGIOC002 | SourceGen.Ioc | Error | Constructor cannot be determined
SGIOC003 | SourceGen.Ioc | Error | Invalid implementation type
SGIOC004 | SourceGen.Ioc | Error | Invalid service type
SGIOC006 | SourceGen.Ioc | Error | Wrapper type cannot be registered
SGIOC007 | SourceGen.Ioc | Error | AnyKey cannot be used here
SGIOC010 | SourceGen.Ioc | Error | nameof refers to overloaded members
SGIOC011 | SourceGen.Ioc | Error | nameof target not found
SGIOC016 | SourceGen.Ioc | Error | Invalid injection method
SGIOC017 | SourceGen.Ioc | Error | LookupMode conflicts with Key
SGIOC018 | SourceGen.Ioc | Error | Conflicting key annotations
SGIOC019 | SourceGen.Ioc | Error | Key specified on constructor or method
SGIOC020 | SourceGen.Ioc | Error | IocInjectMember key on method
SGIOC021 | SourceGen.Ioc | Error | IocInjectMember without registration in scope
SGIOC022 | SourceGen.Ioc | Error | Unsupported key value
SGIOC023 | SourceGen.Ioc | Error | ServiceKey on non-keyed registration
SGIOC024 | SourceGen.Ioc | Error | Member has two injection settings
SGIOC025 | SourceGen.Ioc | Error | Attribute on a class that is neither module nor container
SGIOC107 | SourceGen.Ioc | Error | Unsupported module or container class
SGIOC108 | SourceGen.Ioc | Error | Invalid Add method name
SGIOC301 | SourceGen.Ioc | Error | Circular dependency
SGIOC302 | SourceGen.Ioc | Error | Captive dependency
SGIOC304 | SourceGen.Ioc | Error | Unsupported wrapper combination
SGIOC305 | SourceGen.Ioc | Error | Invalid Func with arguments
SGIOC401 | SourceGen.Ioc | Error | Ambiguous dependency
SGIOC402 | SourceGen.Ioc | Error | Missing dependency
SGIOC403 | SourceGen.Ioc | Error | Disposable Transient
SGIOC404 | SourceGen.Ioc | Error | Duplicate dictionary key
SGIOC501 | SourceGen.Ioc | Info | Dependency resolved from the fallback provider
SGIOC502 | SourceGen.Ioc | Warning | Singleton holds a transient service
SGIOC503 | SourceGen.Ioc | Warning | Scoped service holds a transient service
SGIOC505 | SourceGen.Ioc | Warning | Key can match only at run time
