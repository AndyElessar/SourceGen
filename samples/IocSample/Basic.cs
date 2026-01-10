namespace IocSample;

public interface IBasic;
[IoCRegister<IBasic>(ServiceLifetime.Scoped)]
internal class Basic : IBasic;
[IoCRegister(ServiceLifetime.Transient, typeof(IBasic))]
internal class Basic2 : IBasic;

public interface IExternal;
public class External : IExternal;
public class External2 : IExternal;

//[assembly: IoCRegisterFor<External>]
[IoCRegisterFor<External>(ServiceLifetime.Singleton)]
[IoCRegisterFor<External2>(ServiceLifetime.Transient, ServiceTypes = [typeof(IExternal)])]
public class Marker;

[IoCRegisterDefaults<IDenpendency2>(ServiceLifetime.Transient)]
public interface IDenpendency2;
[IoCRegister]
internal class Default1 : IDenpendency2;
[IoCRegister(ServiceLifetime.Scoped)]
internal class Default2 : IDenpendency2;

//[IoCRegister]
public abstract class AbstractClass
{
    //[IoCRegister]
    private class PrivateClass;
}
