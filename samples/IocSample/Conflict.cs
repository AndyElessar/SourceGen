namespace IocSample;

// Error will show when `dotnet build`
/*
[IoCRegister(Lifetime = ServiceLifetime.Transient)]
internal class Transient
{
}
[IoCRegister(Lifetime = ServiceLifetime.Scoped)]
internal class Scoped(Transient transient)
{
    private readonly Transient Transient = transient;
}
[IoCRegister(Lifetime = ServiceLifetime.Singleton)]
internal class Singleton(Transient transient, Scoped scoped)
{
    private readonly Transient transient = transient;
    private readonly Scoped scoped = scoped;
}

[IoCRegister]
internal class Circular1(Circular2 circular2)
{
    private readonly Circular2 circular2 = circular2;
}
[IoCRegister]
internal class Circular2(Circular1 circular1)
{
    private readonly Circular1 circular1 = circular1;
}
*/

public interface IConflict
{
}

[IoCRegisterDefaults<IConflict>(ServiceLifetime.Transient)]
//[IoCRegisterDefaults<IConflict>(ServiceLifetime.Transient)] //SGIOC012
[IoCRegisterFor<Conflict>]
//[IoCRegister] //SGIOC011
public class Conflict
{
}