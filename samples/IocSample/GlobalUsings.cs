global using IocSample;
global using Microsoft.Extensions.DependencyInjection;
global using SourceGen.Ioc;

[assembly: IoCRegisterDefaultSettings(typeof(ITest2), ServiceLifetime.Singleton)]
[assembly: IoCRegisterDefaultSettings(typeof(IGenericTest<>), ServiceLifetime.Scoped, ServiceTypes = [typeof(ITest1)])]
[assembly: IoCRegisterFor(typeof(TestFor), Lifetime = ServiceLifetime.Transient, RegisterAllInterfaces = true)]

[assembly: IoCRegisterFor(typeof(TestInterfaces), Lifetime = ServiceLifetime.Transient)]
//[assembly: IoCRegisterFor(typeof(TestClosed2), Lifetime = ServiceLifetime.Singleton)]