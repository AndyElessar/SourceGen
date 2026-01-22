namespace IocSample;

// Error will show when `dotnet build`
/*
[IocRegister(Lifetime = ServiceLifetime.Transient)]
internal class Transient
{
}
[IocRegister(Lifetime = ServiceLifetime.Scoped)]
internal class Scoped(Transient transient)
{
    private readonly Transient Transient = transient;
}
[IocRegister(Lifetime = ServiceLifetime.Singleton)]
internal class Singleton(Transient transient, Scoped scoped)
{
    private readonly Transient transient = transient;
    private readonly Scoped scoped = scoped;
}

[IocRegister]
internal class Circular1(Circular2 circular2)
{
    private readonly Circular2 circular2 = circular2;
}
[IocRegister]
internal class Circular2(Circular1 circular1)
{
    private readonly Circular1 circular1 = circular1;
}
*/

public interface IConflict
{
}

[IocRegisterDefaults<IConflict>(ServiceLifetime.Transient)]
//[IocRegisterDefaults<IConflict>(ServiceLifetime.Transient)] //SGIOC012
[IocRegisterFor<Conflict>]
//[IocRegister] //SGIOC011
public class Conflict
{
}
