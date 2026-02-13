using System.Collections;

namespace SourceGen.Ioc.SourceGenerator.Models;

internal sealed class DefaultSettingsMap : IReadOnlyList<DefaultSettingsModel>, IEquatable<DefaultSettingsMap>
{
    public ImmutableArray<DefaultSettingsModel> Settings { get; }

    private readonly Dictionary<string, int> _exactMatches;
    private readonly Dictionary<(string NameWithoutGeneric, int GenericArity), int> _genericMatches;

    public DefaultSettingsMap(ImmutableArray<DefaultSettingsModel> settings)
    {
        Settings = settings;
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
        return Settings.SequenceEqual(other.Settings);
    }

    public override bool Equals(object obj) => obj is DefaultSettingsMap map && Equals(map);

    public override int GetHashCode()
    {
        // Simple hash code based on length to avoid iterating array
        return Settings.Length;
    }

    public int Count => Settings.Length;

    public DefaultSettingsModel this[int index] => Settings[index];

    IEnumerator<DefaultSettingsModel> IEnumerable<DefaultSettingsModel>.GetEnumerator() => Settings.AsEnumerable().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Settings.AsEnumerable().GetEnumerator();
}
