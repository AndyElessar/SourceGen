
namespace IocSample;

public interface IWrapperService<T>;

[IocRegister(typeof(IWrapperService<>))]
internal sealed class WrapperService<T> : IWrapperService<T>
{
}

[IocRegister]
internal sealed class Consumer(
    Lazy<IWrapperService<int>> wrapperService, 
    Func<IWrapperService<string>> factory,
    [IocInject(Key = "Key")] KeyValuePair<string, IKeyed> keyed,
    IEnumerable<KeyValuePair<object, IKeyed>> keyValues,
    IReadOnlyDictionary<string, IKeyed> dictionary)
{
    private readonly Lazy<IWrapperService<int>> _wrapperService = wrapperService;
    private readonly Func<IWrapperService<string>> _factory = factory;
    private readonly KeyValuePair<string, IKeyed> _keyed = keyed;
    private readonly IEnumerable<KeyValuePair<object, IKeyed>> _keyValues = keyValues;
    private readonly IReadOnlyDictionary<string, IKeyed> _dictionary = dictionary;
}
