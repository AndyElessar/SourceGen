using IocSample.Shared;

namespace IocSample;

[IocImportModule<SharedModule>]
[IocContainer]
public sealed partial class Module;
