using IocSample.Shared;

var services = new ServiceCollection();

services
    .AddShared("Mediator")
    .AddIocSample("Mediator");

var sp = services.BuildServiceProvider();
Console.WriteLine("Use MS.DI");
Test(sp);

Console.WriteLine();
Console.WriteLine("Use SourceGen.Ioc");
Test(new Module().CreateServiceProvider(services));

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