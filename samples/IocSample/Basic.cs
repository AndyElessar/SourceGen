namespace IocSample;

public interface IBasic;
[IoCRegister(ServiceTypes = [typeof(IBasic)], Lifetime = ServiceLifetime.Scoped)]
internal class Basic : IBasic;

public interface IExternal;
internal class External : IExternal;
//[assembly: IoCRegisterFor(typeof(External), RegisterAllInterfaces = true)]
[IoCRegisterFor(typeof(External), RegisterAllInterfaces = true)]
public class Marker;

[IoCRegisterDefaults(typeof(IDenpendency2), ServiceLifetime.Transient)]
public interface IDenpendency2;
[IoCRegister]
internal class Default1 : IDenpendency2;
[IoCRegister]
internal class Default2 : IDenpendency2;

//[IoCRegister]
public abstract class AbstractClass
{
    //[IoCRegister]
    private class PrivateClass;
}
