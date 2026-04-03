using IocSample.Shared;

namespace IocSample;

[IocImportModule<SharedModule>]
[IocContainer]
public sealed partial class Module
{
    public partial IBasic GetBasic();

    [IocInject(Key = KeyEnum.Key0)]
    public partial IKeyed GetKeyEnum();

    public partial Task<IAsyncDependency> GetAsyncDependencyTask();
}
