namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// The wrapper and collection types that registrations depend on, scanned once for both the Register and Container
    /// outputs from every dependency of a registration: its factory's parameters (or the implementation type's constructor
    /// parameters), its injection members, and its decorators' constructor parameters and injection members.
    /// </summary>
    /// <remarks>
    /// Nested wrappers are intentionally excluded from the wrapper demand. This is a deliberate pragmatic choice: direct
    /// container resolution of nested wrappers (for example, <c>container.GetService&lt;Lazy&lt;Func&lt;T&gt;&gt;&gt;()</c>)
    /// is extremely rare, and constructor injection is already fully covered by inline construction. Keeping nested
    /// wrappers inline avoids expanding field scanning, naming conventions, and scoped-container infrastructure for all
    /// nested shapes. Nested wrappers also usually do not require cross-consumer instance sharing, so per-consumer inline
    /// instances are semantically correct. Collections are tracked inside wrappers, because their resolvers are shared.
    /// </remarks>
    private sealed class WrapperDemand
    {
        /// <summary>Gets the service types of direct <c>Lazy&lt;T&gt;</c> dependencies, alone or as collection elements.</summary>
        public HashSet<string> LazyServiceTypes { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the direct <c>Func&lt;…, T&gt;</c> dependencies by Func type name, alone or as collection elements.</summary>
        public Dictionary<string, FuncTypeData> FuncTypes { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the (key type, value type) pairs of <c>KeyValuePair</c> and dictionary dependencies.</summary>
        public HashSet<(string KeyTypeName, string ValueTypeName)> KvpPairs { get; } = [];

        /// <summary>Gets the element types requested as collections, including inside <c>Lazy</c>/<c>Func</c>/<c>Task</c> wrappers.</summary>
        public HashSet<string> CollectionServiceTypes { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets the return types of direct parameterless <c>Func&lt;T&gt;</c> dependencies, which the Container output backs with fields.
        /// </summary>
        public HashSet<string> GetParameterlessFuncServiceTypes()
        {
            var serviceTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach(var func in FuncTypes.Values)
            {
                if(!func.HasInputParameters)
                    serviceTypes.Add(func.ReturnType.Name);
            }

            return serviceTypes;
        }

        public static WrapperDemand Scan(IEnumerable<ServiceRegistrationModel> registrations)
        {
            var demand = new WrapperDemand();

            foreach(var registration in registrations)
            {
                demand.ScanParameters(registration.Factory?.AdditionalParameters ?? registration.ImplementationType.ConstructorParameters);
                demand.ScanInjectionMembers(registration.InjectionMembers);

                foreach(var decorator in registration.Decorators)
                {
                    demand.ScanParameters(decorator.ConstructorParameters);
                    demand.ScanInjectionMembers(decorator.InjectionMembers);
                }
            }

            return demand;
        }

        private void ScanParameters(ImmutableEquatableArray<ParameterData>? parameters)
        {
            if(parameters is null)
                return;

            foreach(var parameter in parameters)
                ScanType(parameter.Type);
        }

        private void ScanInjectionMembers(ImmutableEquatableArray<InjectionMemberData>? members)
        {
            if(members is null)
                return;

            foreach(var member in members)
            {
                switch(member)
                {
                    case PropertyOrFieldInjectionData propertyOrField:
                        ScanType(propertyOrField.Type);
                        break;

                    case MethodInjectionData method:
                        ScanParameters(method.Parameters);
                        break;
                }
            }
        }

        private void ScanType(TypeData type)
        {
            switch(type)
            {
                case LazyTypeData lazy when lazy.InstanceType is not WrapperTypeData:
                    LazyServiceTypes.Add(lazy.InstanceType.Name);
                    break;

                case CollectionWrapperTypeData { ElementType: LazyTypeData lazy } when lazy.InstanceType is not WrapperTypeData:
                    LazyServiceTypes.Add(lazy.InstanceType.Name);
                    break;
            }

            switch(type)
            {
                case FuncTypeData func when func.ReturnType is not WrapperTypeData:
                    AddFunc(func);
                    break;

                case CollectionWrapperTypeData { ElementType: FuncTypeData func } when func.ReturnType is not WrapperTypeData:
                    AddFunc(func);
                    break;
            }

            switch(type)
            {
                case KeyValuePairTypeData kvp:
                    KvpPairs.Add((kvp.KeyType.Name, kvp.ValueType.Name));
                    break;

                case DictionaryTypeData dictionary:
                    KvpPairs.Add((dictionary.KeyType.Name, dictionary.ValueType.Name));
                    break;

                case CollectionWrapperTypeData { ElementType: KeyValuePairTypeData kvp }:
                    KvpPairs.Add((kvp.KeyType.Name, kvp.ValueType.Name));
                    break;
            }

            ScanCollectionType(type);
        }

        private void AddFunc(FuncTypeData func)
        {
            if(!FuncTypes.ContainsKey(func.Name))
                FuncTypes[func.Name] = func;
        }

        private void ScanCollectionType(TypeData type)
        {
            switch(type)
            {
                case CollectionWrapperTypeData collection:
                    CollectionServiceTypes.Add(collection.ElementType.Name);
                    ScanCollectionType(collection.ElementType);
                    break;

                case LazyTypeData lazy:
                    ScanCollectionType(lazy.InstanceType);
                    break;

                case FuncTypeData func:
                    ScanCollectionType(func.ReturnType);
                    break;

                case TaskTypeData task:
                    ScanCollectionType(task.InnerType);
                    break;
            }
        }
    }
}
