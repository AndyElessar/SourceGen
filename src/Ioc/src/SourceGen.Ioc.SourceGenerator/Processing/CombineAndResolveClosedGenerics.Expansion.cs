namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// The deepest nesting of type arguments a dependency collected from a closed generic registration is closed with. It
    /// only stops dependencies that <see cref="FindExpandingDependencies"/> does not model, so that closing always ends.
    /// </summary>
    private const int MaxCollectedTypeArgumentDepth = 16;

    /// <summary>A closed generic registration or decorator that dependencies were collected from.</summary>
    /// <param name="Implementation">The name of the open generic implementation or decorator it was closed from.</param>
    /// <param name="TypeArgumentMap">The type argument name of each of its type parameters.</param>
    private sealed record class ClosingSource(string Implementation, Dictionary<string, string> TypeArgumentMap);

    /// <summary>The source of dependencies that only the nesting depth is checked for, such as those of closed decorators.</summary>
    private static readonly ClosingSource UnmodeledSource = new(string.Empty, []);

    /// <summary>A dependency of an open generic implementation or decorator, written with its type parameters.</summary>
    /// <param name="Dependency">The name of the dependency, such as <c>INode&lt;List&lt;T&gt;&gt;</c>.</param>
    /// <param name="IsExpanding">Whether closing it nests type arguments deeper on a cycle, so its closings never end.</param>
    /// <param name="IsDeferred">
    /// Whether each of its expanding cycles has a dependency resolved through <c>Lazy&lt;T&gt;</c> or <c>Func&lt;T&gt;</c>, so
    /// the object graph only grows when it is used.
    /// </param>
    internal readonly record struct DependencySite(string Dependency, bool IsExpanding, bool IsDeferred);

    /// <summary>A link from a type parameter to a type parameter whose type argument contains it.</summary>
    /// <param name="IsNested">Whether the type argument is not the type parameter itself, such as <c>List&lt;T&gt;</c>.</param>
    private readonly record struct TypeParameterLink(TypeParameterNode From, TypeParameterNode To, bool IsNested);

    /// <summary>A type parameter of an open generic implementation or decorator, by their names.</summary>
    private readonly record struct TypeParameterNode(string Owner, string TypeParameter);

    /// <summary>
    /// Finds the dependencies of open generic implementations and decorators whose closings never end, such as
    /// <c>INode&lt;List&lt;T&gt;&gt;</c> of <c>Node&lt;T&gt;</c>, independently of the order in which closed generic
    /// dependencies are discovered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each type parameter of an open generic implementation or decorator is a node. A dependency links each type
    /// parameter it contains to the type parameters of the implementations that can be closed for it whose type
    /// arguments, matched position by position, contain it. The link is nested when the type argument is not the type
    /// parameter itself, such as <c>Node.T</c> to <c>Node.T</c> through <c>INode&lt;List&lt;T&gt;&gt;</c>. A type
    /// parameter matched against a deeper part of the target, such as <c>T</c> against <c>List&lt;U&gt;</c>, gives parts of
    /// its type argument, which does not nest them. A dependency that is a type parameter can only become a generic type
    /// written as a type argument of a dependency that an implementation is closed for, of a root dependency, or of a
    /// decorated service type, so it links to the implementations registered for those types. Each decorator is linked
    /// the same way from the service types it decorates.
    /// </para>
    /// <para>
    /// A cycle with a nested link nests type arguments deeper on every round (the expanding cycles of ECMA-335 II.9.2). A
    /// dependency is expanding when it has a nested link within a strongly connected component, or any link within a
    /// component where a decorator is linked nested, since decorators are applied without a dependency. Without expanding
    /// dependencies, closings only take earlier type arguments, their parts, and types written in the source, so they end.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The dependencies of each implementation or decorator with an expanding dependency, or <see langword="null"/> when
    /// there are none.
    /// </returns>
    /// <param name="rootDependencies">The closed generic dependencies of non-generic registrations and invocations.</param>
    internal static Dictionary<string, List<DependencySite>>? FindExpandingDependencies(
        Dictionary<string, List<OpenGenericRegistrationInfo>> openGenericIndex,
        IEnumerable<ClosedGenericDependency> rootDependencies,
        CancellationToken ct)
    {
        // A registration is indexed under each of its service types, but analyzed once
        var registrations = new List<(OpenGenericRegistrationInfo Info, GenericTypeData Implementation)>();
        var seenRegistrations = new HashSet<OpenGenericRegistrationInfo>(ReferenceComparer.Instance);
        foreach(var list in openGenericIndex.Values)
        {
            foreach(var info in list)
            {
                if(info.ImplementationType is GenericTypeData { TypeParameters.Length: > 0 } implementation && seenRegistrations.Add(info))
                {
                    registrations.Add((info, implementation));
                }
            }
        }

        var typeParamsByOwner = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var ownerDependencies = new List<(string Owner, TypeData Dependency, bool IsDeferred)>();
        var dependencyTypes = new List<(TypeData Type, bool IsDeferred)>();
        var decoratorLinks = new List<TypeParameterLink>();
        var seenDecorators = new HashSet<string>(StringComparer.Ordinal);

        // The generic types written as type arguments, which are the only types a type parameter can become
        var argumentTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach(var (info, implementation) in registrations)
        {
            ct.ThrowIfCancellationRequested();
            var typeParams = GetOwnerTypeParameters(implementation);

            foreach(var param in implementation.ConstructorParameters ?? [])
            {
                AddDependencyType(implementation.Name, param.Type, typeParams);
            }
            AddInjectionMemberTypes(implementation.Name, info.InjectionMembers, typeParams);

            foreach(var serviceType in info.ServiceTypes)
            {
                if(serviceType is not GenericTypeData { GenericArity: > 0 } genericServiceType
                    || FindImplementedForm(implementation, info.AllInterfaces, genericServiceType) is not { } implementedForm)
                {
                    continue;
                }

                foreach(var decorator in info.Decorators)
                {
                    if(decorator is not GenericTypeData { IsOpenGeneric: true, TypeParameters.Length: > 0 } genericDecorator
                        || FindDeclaredForm((decorator.AllInterfaces ?? []).Concat(decorator.AllBaseClasses ?? []), implementedForm) is not { } declaredForm)
                    {
                        continue;
                    }

                    var decoratorTypeParams = GetOwnerTypeParameters(genericDecorator);
                    LinkTypeArguments(declaredForm, genericDecorator.Name, decoratorTypeParams, implementedForm, implementation.Name, typeParams, decoratorLinks);
                    AddArgumentTypes(implementedForm, argumentTypes);
                    AddArgumentTypes(declaredForm, argumentTypes);

                    if(!seenDecorators.Add($"{genericDecorator.Name}|{declaredForm.Name}"))
                    {
                        continue;
                    }

                    // The parameter that receives the decorated instance is not a dependency
                    var parameters = genericDecorator.ConstructorParameters ?? [];
                    var decoratedIndex = GetDecoratedParameterIndex(parameters, declaredForm.Name);
                    for(var i = 0; i < parameters.Length; i++)
                    {
                        if(i != decoratedIndex)
                        {
                            AddDependencyType(genericDecorator.Name, parameters[i].Type, decoratorTypeParams);
                        }
                    }
                    AddInjectionMemberTypes(genericDecorator.Name, genericDecorator.InjectionMembers, decoratorTypeParams);
                }
            }
        }

        // A type parameter can only become a type written as a type argument, so a dependency that is a type parameter,
        // such as T in W<T>(T inner), can be closed by the implementations registered for those types. The root
        // dependencies are only enumerated for such dependencies.
        if(ownerDependencies.Any(entry => typeParamsByOwner[entry.Owner].Contains(GetTypeParameterName(entry.Dependency))))
        {
            foreach(var root in rootDependencies)
            {
                AddMatchedArgumentTypes(root.ClosedType);
            }
        }

        var argumentTargets = new List<(OpenGenericRegistrationInfo Info, GenericTypeData Implementation)>();
        foreach(var argumentType in argumentTypes)
        {
            foreach(var target in openGenericIndex.TryGetValue(argumentType, out var list) ? list : [])
            {
                if(target.ImplementationType is GenericTypeData { TypeParameters.Length: > 0 } targetImplementation)
                {
                    argumentTargets.Add((target, targetImplementation));
                }
            }
        }

        var sites = new List<(string Owner, string Dependency, bool IsDeferred, List<TypeParameterLink> Links)>(ownerDependencies.Count);
        foreach(var (owner, dependency, isDeferred) in ownerDependencies)
        {
            var typeParams = typeParamsByOwner[owner];
            var links = new List<TypeParameterLink>();
            if(typeParams.Contains(GetTypeParameterName(dependency)))
            {
                var from = new TypeParameterNode(owner, GetTypeParameterName(dependency));
                foreach(var (_, target) in argumentTargets)
                {
                    foreach(var targetParam in target.TypeParameters ?? [])
                    {
                        links.Add(new(from, new(target.Name, targetParam.ParameterName), IsNested: false));
                    }
                }
            }
            else if(dependency is GenericTypeData genericDependency
                && openGenericIndex.TryGetValue(genericDependency.NameWithoutGeneric, out var targets))
            {
                foreach(var target in targets)
                {
                    // Like closing, the service type as the target declares it is preferred
                    if(target.ImplementationType is GenericTypeData { TypeParameters.Length: > 0 } targetImplementation
                        && (FindImplementedForm(targetImplementation, target.AllInterfaces, genericDependency)
                            ?? FindDeclaredForm(target.ServiceTypes, genericDependency)) is { } pattern)
                    {
                        LinkTypeArguments(pattern, targetImplementation.Name, GetOwnerTypeParameters(targetImplementation), dependency, owner, typeParams, links);
                    }
                }
            }

            sites.Add((owner, dependency.Name, isDeferred, links));
        }

        // Only a nested link can make a dependency expanding
        if(!sites.Any(static site => site.Links.Any(static link => link.IsNested)) && !decoratorLinks.Any(static link => link.IsNested))
        {
            return null;
        }

        var graph = new Dictionary<TypeParameterNode, List<TypeParameterNode>>();
        foreach(var link in sites.SelectMany(static site => site.Links).Concat(decoratorLinks))
        {
            if(!graph.TryGetValue(link.From, out var successors))
            {
                successors = [];
                graph[link.From] = successors;
            }
            successors.Add(link.To);
        }

        var components = FindStronglyConnectedComponents(graph);
        var nestedDecoratorComponents = new HashSet<int>();
        foreach(var link in decoratorLinks)
        {
            if(link.IsNested && components[link.From] == components[link.To])
            {
                nestedDecoratorComponents.Add(components[link.From]);
            }
        }

        // The cycles without a dependency resolved through Lazy<T> or Func<T>, which make the object graph itself infinite
        var eagerGraph = new Dictionary<TypeParameterNode, List<TypeParameterNode>>();
        foreach(var link in sites.Where(static site => !site.IsDeferred).SelectMany(static site => site.Links).Concat(decoratorLinks))
        {
            if(!eagerGraph.TryGetValue(link.From, out var successors))
            {
                successors = [];
                eagerGraph[link.From] = successors;
            }
            successors.Add(link.To);
        }

        var eagerComponents = FindStronglyConnectedComponents(eagerGraph);
        var nestedDecoratorEagerComponents = new HashSet<int>();
        foreach(var link in decoratorLinks)
        {
            if(link.IsNested && eagerComponents[link.From] == eagerComponents[link.To])
            {
                nestedDecoratorEagerComponents.Add(eagerComponents[link.From]);
            }
        }

        // Keep every dependency of the owners with an expanding one, since a closed dependency is only expanding when no
        // other dependency of the same owner needs it too
        Dictionary<string, List<DependencySite>>? result = null;
        var classified = new List<(string Owner, DependencySite Site)>(sites.Count);
        foreach(var (owner, dependency, isDeferredSite, links) in sites)
        {
            bool isExpanding = false, isEagerlyExpanding = false;
            foreach(var link in links)
            {
                var component = components[link.From];
                if(component == components[link.To] && (link.IsNested || nestedDecoratorComponents.Contains(component)))
                {
                    isExpanding = true;
                }

                if(!isDeferredSite
                    && eagerComponents[link.From] == eagerComponents[link.To]
                    && (link.IsNested || nestedDecoratorEagerComponents.Contains(eagerComponents[link.From])))
                {
                    isEagerlyExpanding = true;
                }
            }

            classified.Add((owner, new DependencySite(dependency, isExpanding, IsDeferred: isExpanding && !isEagerlyExpanding)));
            if(isExpanding)
            {
                result ??= new Dictionary<string, List<DependencySite>>(StringComparer.Ordinal);
                result[owner] = [];
            }
        }

        if(result is not null)
        {
            foreach(var (owner, site) in classified)
            {
                if(result.TryGetValue(owner, out var ownerSites))
                {
                    ownerSites.Add(site);
                }
            }
        }

        return result;

        HashSet<string> GetOwnerTypeParameters(GenericTypeData owner)
        {
            if(!typeParamsByOwner.TryGetValue(owner.Name, out var names))
            {
                names = new HashSet<string>(StringComparer.Ordinal);
                foreach(var typeParam in owner.TypeParameters ?? [])
                {
                    names.Add(typeParam.ParameterName);
                }
                typeParamsByOwner[owner.Name] = names;
            }

            return names;
        }

        void AddDependencyType(string owner, TypeData type, HashSet<string> typeParams)
        {
            dependencyTypes.Clear();
            CollectDependencyTypes(type, typeParams, isDeferred: false, dependencyTypes);
            foreach(var (dependency, isDeferred) in dependencyTypes)
            {
                ownerDependencies.Add((owner, dependency, isDeferred));
                AddMatchedArgumentTypes(dependency);
            }
        }

        void AddInjectionMemberTypes(string owner, ImmutableEquatableArray<InjectionMemberData>? injectionMembers, HashSet<string> typeParams)
        {
            foreach(var member in injectionMembers ?? [])
            {
                switch(member)
                {
                    case PropertyOrFieldInjectionData propertyOrField:
                        AddDependencyType(owner, propertyOrField.Type, typeParams);
                        break;

                    case MethodInjectionData method:
                        foreach(var param in method.Parameters)
                        {
                            AddDependencyType(owner, param.Type, typeParams);
                        }
                        break;
                }
            }
        }

        // Only the type arguments of a dependency that an implementation is closed for become type arguments of closings
        void AddMatchedArgumentTypes(TypeData dependency)
        {
            if(dependency is GenericTypeData genericDependency && openGenericIndex.ContainsKey(genericDependency.NameWithoutGeneric))
            {
                AddArgumentTypes(dependency, argumentTypes);
            }
        }

        static void AddArgumentTypes(TypeData type, HashSet<string> argumentTypes)
        {
            foreach(var typeParam in type is GenericTypeData { TypeParameters: { } typeParams } ? typeParams : [])
            {
                if(typeParam.Type is GenericTypeData { GenericArity: > 0 } genericArgument)
                {
                    argumentTypes.Add(genericArgument.NameWithoutGeneric);
                }

                AddArgumentTypes(typeParam.Type, argumentTypes);
            }
        }
    }

    /// <summary>
    /// Collects the types a dependency type is closed for: the type itself when it is generic or one of
    /// <paramref name="typeParams"/>, and, like <see cref="CollectNewDependencyFromType"/>, the types it wraps.
    /// </summary>
    /// <param name="isDeferred">Whether the type is wrapped in <c>Lazy&lt;T&gt;</c> or <c>Func&lt;T&gt;</c>.</param>
    internal static void CollectDependencyTypes(
        TypeData type,
        HashSet<string> typeParams,
        bool isDeferred,
        List<(TypeData Type, bool IsDeferred)> dependencies)
    {
        if(GetWrapperInnerType(type) is { } innerType)
        {
            CollectDependencyTypes(innerType, typeParams, isDeferred || type is LazyTypeData or FuncTypeData, dependencies);
        }

        if(GetEnumerableElementType(type) is { } elementType)
        {
            CollectDependencyTypes(elementType, typeParams, isDeferred, dependencies);
        }

        if((type is GenericTypeData { GenericArity: > 0 } && !type.IsArrayType) || typeParams.Contains(GetTypeParameterName(type)))
        {
            dependencies.Add((type, isDeferred));
        }
    }

    /// <summary>
    /// Links the type parameters of <paramref name="type"/> to the type parameters of <paramref name="pattern"/> at the same
    /// positions. Positions that do not match are skipped, since a closing cannot take type arguments from them.
    /// </summary>
    private static void LinkTypeArguments(
        TypeData pattern,
        string patternOwner,
        HashSet<string> patternTypeParams,
        TypeData type,
        string typeOwner,
        HashSet<string> typeParams,
        List<TypeParameterLink> links)
    {
        // The target's type parameter takes the whole type, nesting the type parameters it contains unless it is one
        if(patternTypeParams.Contains(GetTypeParameterName(pattern)))
        {
            var to = new TypeParameterNode(patternOwner, GetTypeParameterName(pattern));
            foreach(var typeParam in typeParams)
            {
                if(ContainsTypeParameter(type, typeParam))
                {
                    links.Add(new(new(typeOwner, typeParam), to, IsNested: GetTypeParameterName(type) != typeParam));
                }
            }
            return;
        }

        // A type parameter matched against a deeper part, such as T against List<U>, gives parts of its type argument
        if(typeParams.Contains(GetTypeParameterName(type)))
        {
            var from = new TypeParameterNode(typeOwner, GetTypeParameterName(type));
            foreach(var patternTypeParam in patternTypeParams)
            {
                if(ContainsTypeParameter(pattern, patternTypeParam))
                {
                    links.Add(new(from, new(patternOwner, patternTypeParam), IsNested: false));
                }
            }
            return;
        }

        if(pattern is GenericTypeData { TypeParameters: { Length: > 0 } patternArgs } genericPattern
            && type is GenericTypeData { TypeParameters: { } typeArgs } genericType
            && genericPattern.NameWithoutGeneric == genericType.NameWithoutGeneric
            && patternArgs.Length == typeArgs.Length)
        {
            for(var i = 0; i < patternArgs.Length; i++)
            {
                LinkTypeArguments(patternArgs[i].Type, patternOwner, patternTypeParams, typeArgs[i].Type, typeOwner, typeParams, links);
            }
        }
    }

    /// <summary>
    /// Gets whether a closed generic dependency is collected only from expanding dependencies of the closed generic
    /// registration or decorator it was collected from (see <see cref="FindExpandingDependencies"/>), or, for dependencies
    /// that are not modeled, nests its type arguments deeper than <see cref="MaxCollectedTypeArgumentDepth"/>.
    /// </summary>
    private static bool IsExpandingDependency(
        Dictionary<string, List<DependencySite>>? expandingDependencies,
        ClosingSource? source,
        ClosedGenericDependency dependency)
    {
        if(source is null)
        {
            return false;
        }

        if(IsNestedDeeperThan(dependency.ClosedType, MaxCollectedTypeArgumentDepth))
        {
            return true;
        }

        if(expandingDependencies is null || !expandingDependencies.TryGetValue(source.Implementation, out var sites))
        {
            return false;
        }

        var closedName = dependency.ClosedTypeName;
        var isExpanding = false;
        foreach(var site in sites)
        {
            // A generic dependency keeps its name up to its type arguments, which rules out most sites without substituting
            var genericStart = site.Dependency.IndexOf('<');
            if((genericStart > 0 && string.CompareOrdinal(site.Dependency, 0, closedName, 0, genericStart + 1) != 0)
                || SubstituteTypeArguments(site.Dependency, source.TypeArgumentMap) != closedName)
            {
                continue;
            }

            if(!site.IsExpanding)
            {
                return false;
            }

            isExpanding = true;
        }

        return isExpanding;

        // Arrays and tuples are generic type data too, so their nesting counts
        static bool IsNestedDeeperThan(TypeData type, int depth)
        {
            if(type is not GenericTypeData { TypeParameters: { Length: > 0 } typeArguments })
            {
                return false;
            }

            if(depth == 0)
            {
                return true;
            }

            foreach(var typeArgument in typeArguments)
            {
                if(IsNestedDeeperThan(typeArgument.Type, depth - 1))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Gets the closing source of the dependencies of a closed decorator. Without an expanding dependency of its open
    /// generic decorator, only the nesting depth of its dependencies is checked.
    /// </summary>
    private static ClosingSource GetDecoratorClosingSource(
        Dictionary<string, List<DependencySite>>? expandingDependencies,
        OpenGenericRegistrationInfo? registration,
        TypeData closedDecorator)
    {
        if(expandingDependencies is null
            || registration is null
            || closedDecorator is not GenericTypeData { TypeParameters: { Length: > 0 } typeArguments } closedGenericDecorator)
        {
            return UnmodeledSource;
        }

        foreach(var decorator in registration.Decorators)
        {
            if(decorator is GenericTypeData { IsOpenGeneric: true } openDecorator
                && openDecorator.NameWithoutGeneric == closedGenericDecorator.NameWithoutGeneric
                && openDecorator.GenericArity == closedGenericDecorator.GenericArity
                && expandingDependencies.ContainsKey(openDecorator.Name))
            {
                var typeArgumentMap = new Dictionary<string, string>(typeArguments.Length, StringComparer.Ordinal);
                foreach(var typeArgument in typeArguments)
                {
                    typeArgumentMap[typeArgument.ParameterName] = typeArgument.Type.Name;
                }

                return new ClosingSource(openDecorator.Name, typeArgumentMap);
            }
        }

        return UnmodeledSource;
    }

    /// <summary>Gets a type's name without a nullable annotation, which is the type parameter's name for <c>T?</c>.</summary>
    private static string GetTypeParameterName(TypeData type) => type.Name.TrimEnd('?');

    private static bool ContainsTypeParameter(TypeData type, string name)
    {
        if(GetTypeParameterName(type) == name)
        {
            return true;
        }

        foreach(var typeParam in type is GenericTypeData { TypeParameters: { } typeParams } ? typeParams : [])
        {
            if(ContainsTypeParameter(typeParam.Type, name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Numbers the strongly connected components of a graph (Tarjan's algorithm).
    /// </summary>
    /// <returns>The component number of each node.</returns>
    private static Dictionary<TypeParameterNode, int> FindStronglyConnectedComponents(Dictionary<TypeParameterNode, List<TypeParameterNode>> graph)
    {
        var index = new Dictionary<TypeParameterNode, int>();
        var lowLink = new Dictionary<TypeParameterNode, int>();
        var components = new Dictionary<TypeParameterNode, int>();
        var stack = new Stack<TypeParameterNode>();
        var onStack = new HashSet<TypeParameterNode>();
        var componentCount = 0;

        foreach(var node in graph.Keys)
        {
            if(!index.ContainsKey(node))
            {
                Visit(node);
            }
        }

        return components;

        void Visit(TypeParameterNode node)
        {
            index[node] = lowLink[node] = index.Count;
            stack.Push(node);
            onStack.Add(node);

            foreach(var successor in graph.TryGetValue(node, out var successors) ? successors : [])
            {
                if(!index.TryGetValue(successor, out var successorIndex))
                {
                    Visit(successor);
                    lowLink[node] = Math.Min(lowLink[node], lowLink[successor]);
                }
                else if(onStack.Contains(successor))
                {
                    lowLink[node] = Math.Min(lowLink[node], successorIndex);
                }
            }

            if(lowLink[node] != index[node])
            {
                return;
            }

            TypeParameterNode member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                components[member] = componentCount;
            }
            while(member != node);
            componentCount++;
        }
    }

    /// <summary>Compares registrations by reference, since each one is analyzed once.</summary>
    private sealed class ReferenceComparer : IEqualityComparer<OpenGenericRegistrationInfo>
    {
        public static ReferenceComparer Instance { get; } = new();

        public bool Equals(OpenGenericRegistrationInfo? x, OpenGenericRegistrationInfo? y) => ReferenceEquals(x, y);

        public int GetHashCode(OpenGenericRegistrationInfo obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
