using IocSample.Shared;

var services = new ServiceCollection();

services
    .AddIocSample_Shared()
    .AddIocSample_Shared_Mediator()
    .AddIocSample()
    .AddIocSample_Mediator();

var sp = services.BuildServiceProvider();

var h = sp.GetRequiredService<IRequestHandler<TestRequest, List<string>>>();
var r = h.Handle(new TestRequest(10));
Console.WriteLine(string.Join(", ", r));

Console.WriteLine();

var h2 = sp.GetRequiredService<IRequestHandler<TestRequest2, List<string>>>();
var r2 = h2.Handle(new TestRequest2("Hello"));
Console.WriteLine(string.Join(", ", r2));

var vm = sp.GetRequiredService<ViewModel>();
var es = vm.LoadEntities(5);
Console.WriteLine(string.Join(',', es.Select(e => e.Id.ToString())));