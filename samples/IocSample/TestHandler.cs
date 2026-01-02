using IocSample.Shared;

namespace IocSample;

public sealed record TestQuery(string Name) : IQuery<TestQuery, string>;

[IoCRegister]
internal sealed class TestHandler : IRequestHandler<TestQuery, string>
{
    public string Handle(TestQuery request)
    {
        return $"Hello, {request.Name}!";
    }
}
