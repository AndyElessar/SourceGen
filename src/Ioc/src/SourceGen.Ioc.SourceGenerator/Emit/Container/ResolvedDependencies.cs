using InjectionMemberModel = SourceGen.Ioc.InjectionMemberData;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private abstract record class ResolvedDependency
    {
        public abstract string FormatExpression(bool isOptional);
    }

    private sealed record class DirectServiceDependency(string ResolverMethodName) : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            return $"{ResolverMethodName}()";
        }
    }

    private sealed record class CollectionDependency(string ArrayMethodName) : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            return $"{ArrayMethodName}()";
        }
    }

    private sealed record class LazyFieldReferenceDependency(string FieldName) : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            return FieldName;
        }
    }

    private sealed record class LazyInlineDependency(string ServiceTypeName, ResolvedDependency Inner) : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            var innerExpr = Inner.FormatExpression(isOptional);
            return $"new global::System.Lazy<{ServiceTypeName}>(() => {innerExpr}, global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication)";
        }
    }

    private sealed record class FuncFieldReferenceDependency(string FieldName) : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            return FieldName;
        }
    }

    private sealed record class FuncInlineDependency(string ServiceTypeName, ResolvedDependency Inner) : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            var innerExpr = Inner.FormatExpression(isOptional);
            return $"new global::System.Func<{ServiceTypeName}>(() => {innerExpr})";
        }
    }

    private sealed record class MultiParamFuncDependency(
        string ReturnTypeName,
        ImmutableEquatableArray<ParameterData> InputParameters,
        ImmutableEquatableArray<ResolvedConstructorParameter> ConstructorParameters,
        ImmutableEquatableArray<ResolvedInjectionMember> InjectionMembers,
        ImmutableEquatableArray<ResolvedDecorator> Decorators,
        string? ImplementationTypeName = null,
        string? RegistrationKey = null) : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            var inputArgNames = new string[InputParameters.Length];
            var inputArgTypeNames = new string[InputParameters.Length];
            var inputArgUsed = new bool[InputParameters.Length];

            for(var i = 0; i < InputParameters.Length; i++)
            {
                inputArgNames[i] = $"arg{i}";
                inputArgTypeNames[i] = InputParameters[i].Type.Name;
                inputArgUsed[i] = false;
            }

            var lambdaParams = string.Join(", ", InputParameters.Select(static (param, i) => $"{param.Type.Name} arg{i}"));

            var statements = new List<string>();
            var ctorEntries = new List<(string Name, string? Value)>(ConstructorParameters.Length);

            var resolvedParamIndex = 0;
            foreach(var param in ConstructorParameters)
            {
                var matchedArg = TryConsumeMatchingFuncInputArg(param.Parameter.Type.Name, inputArgNames, inputArgTypeNames, inputArgUsed);
                if(matchedArg is not null)
                {
                    ctorEntries.Add((param.Parameter.Name, matchedArg));
                    continue;
                }

                var paramVar = $"p{resolvedParamIndex}";
                // A null service key needs a type, because the value is assigned to an implicitly typed variable
                var expr = param.Dependency is ServiceKeyLiteralDependency { KeyValue: "null" } serviceKey
                    ? $"default({serviceKey.KeyType})"
                    : param.Dependency.FormatExpression(param.IsOptional);
                statements.Add($"var {paramVar} = {expr};");
                ctorEntries.Add((param.Parameter.Name, paramVar));
                resolvedParamIndex++;
            }

            var propertyInits = new List<string>();
            var propertyIndex = 0;
            foreach(var injectionMember in InjectionMembers)
            {
                var member = injectionMember.Member;
                if(member.MemberType is not (InjectionMemberType.Property or InjectionMemberType.Field))
                    continue;

                var memberType = member.Type;
                if(memberType is null)
                    continue;

                var matchedArg = TryConsumeMatchingFuncInputArg(memberType.Name, inputArgNames, inputArgTypeNames, inputArgUsed);
                if(matchedArg is not null)
                {
                    propertyInits.Add($"{member.Name} = {matchedArg}");
                    continue;
                }

                if(injectionMember.Dependency is null)
                {
                    throw new InvalidOperationException($"Missing resolved dependency for injection member '{member.Name}'.");
                }

                var memberVar = $"s0_p{propertyIndex}";
                var expr = injectionMember.Dependency.FormatExpression(member.IsNullable);
                statements.Add($"var {memberVar} = {expr};");
                propertyInits.Add($"{member.Name} = {memberVar}");
                propertyIndex++;
            }

            var implementationType = ImplementationTypeName ?? ReturnTypeName;
            var ctorArgs = BuildArgumentListFromEntries(ctorEntries);
            var initializerPart = propertyInits.Count > 0 ? $" {{ {string.Join(", ", propertyInits)} }}" : string.Empty;
            var ctorInvocation = BuildConstructorInvocation(implementationType, ctorArgs, initializerPart);
            statements.Add($"var s0 = {ctorInvocation};");

            var methodIndex = 0;
            foreach(var injectionMember in InjectionMembers)
            {
                var member = injectionMember.Member;
                if(member.MemberType != InjectionMemberType.Method)
                    continue;

                var methodParams = member.Parameters ?? [];
                var methodEntries = new List<(string Name, string? Value)>(methodParams.Length);
                foreach(var param in methodParams)
                {
                    var matchedArg = TryConsumeMatchingFuncInputArg(param.Type.Name, inputArgNames, inputArgTypeNames, inputArgUsed);
                    if(matchedArg is not null)
                    {
                        methodEntries.Add((param.Name, matchedArg));
                        continue;
                    }

                    if(injectionMember.Dependency is null)
                    {
                        throw new InvalidOperationException($"Missing resolved dependency for method parameter '{param.Name}' in member '{member.Name}'.");
                    }

                    var paramVar = $"s0_m{methodIndex}";
                    var expr = injectionMember.Dependency.FormatExpression(param.IsOptional);
                    statements.Add($"var {paramVar} = {expr};");
                    methodEntries.Add((param.Name, paramVar));
                    methodIndex++;
                }

                var methodArgs = BuildArgumentListFromEntries(methodEntries);
                statements.Add($"s0.{member.Name}({methodArgs});");
            }

            if(Decorators.Length > 0)
            {
                // The input arguments are for the implementation only; decorator dependencies are resolved from the container.
                // Names that cannot clash with the locals of the enclosing resolver method.
                statements.Add($"{ReturnTypeName} decorated = s0;");

                var decoratorWriter = new SourceWriter();
                ServiceContainerEntry.WriteDecoratorApplication(decoratorWriter, Decorators, RegistrationKey, "decorated", "decorated_");
                statements.AddRange(decoratorWriter.ToString()
                    .Split('\n')
                    .Select(static line => line.Trim())
                    .Where(static line => line.Length > 0));

                statements.Add("return decorated;");
            }
            else
            {
                statements.Add("return s0;");
            }

            var funcTypeName = BuildMultiParamFuncTypeName(InputParameters, ReturnTypeName);
            return $"new {funcTypeName}(({lambdaParams}) => {{ {string.Join(" ", statements)} }})";
        }

        private static string BuildMultiParamFuncTypeName(
            ImmutableEquatableArray<ParameterData> inputParameters,
            string returnTypeName)
        {
            if(inputParameters.Length == 0)
            {
                return $"global::System.Func<{returnTypeName}>";
            }

            var inputTypeList = string.Join(", ", inputParameters.Select(static p => p.Type.Name));
            return $"global::System.Func<{inputTypeList}, {returnTypeName}>";
        }
    }

    private sealed record class TaskFromResultDependency(ResolvedDependency Inner, string TypeName) : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            return $"global::System.Threading.Tasks.Task.FromResult(({TypeName}){Inner.FormatExpression(false)})";
        }
    }

    /// <summary>
    /// <c>Task&lt;TypeName&gt;</c> dependency on an async-init service whose resolver returns <c>Task&lt;ResultTypeName&gt;</c>.
    /// </summary>
    private sealed record class TaskAsyncDependency(string AsyncMethodName, string TypeName, string ResultTypeName) : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            // Task<T> is invariant: project only when the requested type differs from the resolver's result type
            return TypeName == ResultTypeName
                ? $"{AsyncMethodName}()"
                : $"{ProjectTaskMethodName}<{TypeName}, {ResultTypeName}>({AsyncMethodName}())";
        }
    }

    private sealed record class KvpInlineDependency(
        string KeyType,
        string ValueType,
        string KeyExpr,
        ResolvedDependency Inner) : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            return $"new global::System.Collections.Generic.KeyValuePair<{KeyType}, {ValueType}>({KeyExpr}, {Inner.FormatExpression(isOptional)})";
        }
    }

    private sealed record class KvpResolverDependency(string MethodName) : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            return $"{MethodName}()";
        }
    }

    private sealed record class DictionaryResolverDependency(string MethodName) : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            return $"{MethodName}()";
        }
    }

    private sealed record class DictionaryFallbackDependency(
        string KvpTypeName,
        bool IsKeyed,
        string? Key) : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            if(IsKeyed)
            {
                return $"GetKeyedServices<{KvpTypeName}>({Key}).ToDictionary()";
            }

            return $"GetServices<{KvpTypeName}>().ToDictionary()";
        }
    }

    private sealed record class ServiceProviderSelfDependency : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            return "this";
        }
    }

    /// <param name="FallbackValue">
    /// The value of an optional dependency that the provider does not resolve (its parameter's default value), or
    /// <see langword="null"/> when it is <c>null</c>.
    /// </param>
    private sealed record class FallbackProviderDependency(
        string TypeName,
        string? Key,
        bool IsOptional,
        string? FallbackValue = null) : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            return BuildServiceProviderFallbackExpression(TypeName, Key, IsOptional || isOptional, FallbackValue);
        }

        private static string BuildServiceProviderFallbackExpression(
            string typeName,
            string? key,
            bool isOptional,
            string? fallbackValue)
        {
            // The switch also compiles for value types, which 'as' does not accept, and types target-typed fallbacks
            // such as new() and [] by the T arm (see BuildOptionalServiceCallWithFallback)
            if(isOptional && fallbackValue is not null)
            {
                return key is not null
                    ? $"(GetKeyedService(typeof({typeName}), {key}) switch {{ {GetPatternTypeName(typeName)} service => service, _ => {fallbackValue} }})"
                    : $"(GetService(typeof({typeName})) switch {{ {GetPatternTypeName(typeName)} service => service, _ => {fallbackValue} }})";
            }

            if(key is not null)
            {
                return isOptional
                    ? $"GetKeyedService(typeof({typeName}), {key}) as {typeName}"
                    : $"({typeName})GetRequiredKeyedService(typeof({typeName}), {key})";
            }

            return isOptional
                ? $"GetService(typeof({typeName})) as {typeName}"
                : $"({typeName})GetRequiredService(typeof({typeName}))";
        }
    }

    private sealed record class CollectionFallbackDependency(
        string ElementType,
        bool IsKeyed,
        string? Key) : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            if(IsKeyed)
            {
                return $"GetKeyedServices<{ElementType}>({Key})";
            }

            return $"GetServices<{ElementType}>()";
        }
    }

    private sealed record class ServiceKeyLiteralDependency(string KeyType, string KeyValue) : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            return KeyValue;
        }
    }

    private sealed record class InstanceExpressionDependency(string Expression) : ResolvedDependency
    {
        public override string FormatExpression(bool isOptional)
        {
            return Expression;
        }
    }

    private readonly record struct ResolvedConstructorParameter(
        ParameterData Parameter,
        ResolvedDependency Dependency,
        bool IsOptional);

    private readonly record struct ResolvedInjectionMember(
        InjectionMemberModel Member,
        ResolvedDependency? Dependency,
        ImmutableEquatableArray<ResolvedDependency> ParameterDependencies);

    private sealed record class ResolvedDecorator(
        ServiceRegistrationModel Decorator,
        ImmutableEquatableArray<ResolvedConstructorParameter> Parameters,
        ImmutableEquatableArray<ResolvedInjectionMember> InjectionMembers);
}