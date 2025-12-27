var services = new ServiceCollection();

services.AddIocSampleServices();

var sp = services.BuildServiceProvider();

var h = sp.GetRequiredService<IRequestHandler<TestRequest, List<string>>>();
var r = h.Handle(new TestRequest(10));
Console.WriteLine(string.Join(", ", r));