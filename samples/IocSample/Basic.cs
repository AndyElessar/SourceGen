namespace IocSample;

public interface IBasic;
public interface IBasic2;

[IocRegister<IBasic>(ServiceLifetime.Scoped)]
internal class Basic : IBasic;
[IocRegister(ServiceLifetime.Transient, typeof(IBasic), typeof(IBasic2))]
internal class Basic2 : IBasic, IBasic2;

public interface IExternal;
public class External : IExternal;
public class External2 : IExternal;

//[assembly: IoCRegisterFor<External>]
[IocRegisterFor<External>(ServiceLifetime.Singleton)]
[IocRegisterFor<External2>(ServiceLifetime.Transient, ServiceTypes = [typeof(IExternal)])]
public class Marker;

[IocRegisterDefaults<IDenpendency2>(ServiceLifetime.Transient)]
public interface IDenpendency2;
[IocRegister]
internal class Default1 : IDenpendency2;
[IocRegister(ServiceLifetime.Scoped)]
internal class Default2 : IDenpendency2;

//[IocRegister]
public abstract class AbstractClass
{
    //[IocRegister]
    private class PrivateClass;
}
