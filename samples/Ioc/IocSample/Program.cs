using IocSample.Shared;

var services = new ServiceCollection();

// Tagged registrations only add services with a matching tag, so also register the untagged ones
services
    .AddShared()
    .AddShared("Mediator")
    .AddIocSample()
    .AddIocSample("Mediator");

var sp = services.BuildServiceProvider();
Console.WriteLine("Use MS.DI");
Test(sp);

Console.WriteLine();
Console.WriteLine("Use SourceGen.Ioc");
// Module() eagerly creates singletons whose ILogger<T> only the fallback provider can supply
using var module = new Module(sp);
Test(module);

static void Test(IServiceProvider sp)
{
    var h = sp.GetRequiredService<IRequestHandler<TestRequest, List<string>>>();
    var r = h.Handle(new TestRequest(10));
    Console.WriteLine(string.Join(", ", r));

    Console.WriteLine();

    var h2 = sp.GetRequiredService<IRequestHandler<TestRequest2, List<string>>>();
    var r2 = h2.Handle(new TestRequest2("Hello"));
    Console.WriteLine(string.Join(", ", r2));

    Console.WriteLine();

    var vm = sp.GetRequiredService<ViewModel>();
    var es = vm.LoadEntities(5);
    Console.WriteLine(string.Join(',', es.Select(e => e.Id.ToString())));

    Console.WriteLine();

    var msger = sp.GetRequiredService<CustomMessenger>();
    var result = msger.Send(new GenericRequest2<Entity>(2));
    Console.WriteLine(string.Join(',', result.Select(e => e.Id.ToString())));
}