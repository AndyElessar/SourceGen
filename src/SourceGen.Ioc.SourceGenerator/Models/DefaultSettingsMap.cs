using System.Collections;

namespace SourceGen.Ioc.SourceGenerator.Models;

internal sealed class DefaultSettingsMap : IReadOnlyList<DefaultSettingsModel>, IEquatable<DefaultSettingsMap>
{
    public ImmutableArray<DefaultSettingsModel> Settings { get; }
    public ServiceLifetime FallbackLifetime { get; }

    private readonly Dictionary<string, int> _exactMatches;
    private readonly Dictionary<(string NameWithoutGeneric, int GenericArity), int> _genericMatches;

    public DefaultSettingsMap(ImmutableArray<DefaultSettingsModel> settings, ServiceLifetime fallbackLifetime = ServiceLifetime.Transient)
    {
        Settings = settings;
        FallbackLifetime = fallbackLifetime;
        _exactMatches = [];
        _genericMatches = [];

        for(int i = 0; i < settings.Length; i++)
        {
            var targetTypeData = settings[i].TargetServiceType;
            var targetType = targetTypeData.Name;

            if(!_exactMatches.ContainsKey(targetType))
            {
                _exactMatches[targetType] = i;
            }

            if(targetTypeData is GenericTypeData genericTargetTypeData && targetType != genericTargetTypeData.NameWithoutGeneric)
            {
                var genericKey = (genericTargetTypeData.NameWithoutGeneric, genericTargetTypeData.GenericArity);
                if(!_genericMatches.ContainsKey(genericKey))
                {
                    _genericMatches[genericKey] = i;
                }
            }
        }
    }

    public bool IsEmpty => Settings.IsDefaultOrEmpty;
    public bool TryGetExactMatches(string key, out int index) => _exactMatches.TryGetValue(key, out index);
    public bool TryGetGenericMatches(string nameWithoutGeneric, int genericArity, out int index) =>
        _genericMatches.TryGetValue((nameWithoutGeneric, genericArity), out index);

    public bool Equals(DefaultSettingsMap other)
    {
        if(other is null)
            return false;
        if(ReferenceEquals(this, other))
            return true;
        // Only need to compare the source array, as dictionaries are derived from it deterministically
        return FallbackLifetime == other.FallbackLifetime
            && Settings.SequenceEqual(other.Settings);
    }

    public override bool Equals(object obj) => obj is DefaultSettingsMap map && Equals(map);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (Settings.Length * 397) ^ (int)FallbackLifetime;
            var count = Math.Min(Settings.Length, 3);
            for(int i = 0; i < count; i++)
            {
                hash = hash * 31 + Settings[i].GetHashCode();
            }

            return hash;
        }
    }

    public int Count => Settings.Length;

    public DefaultSettingsModel this[int index] => Settings[index];

    IEnumerator<DefaultSettingsModel> IEnumerable<DefaultSettingsModel>.GetEnumerator() => Settings.AsEnumerable().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Settings.AsEnumerable().GetEnumerator();
}
