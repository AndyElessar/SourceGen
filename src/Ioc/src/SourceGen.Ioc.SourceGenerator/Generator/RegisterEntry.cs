using static SourceGen.Ioc.SourceGenerator.Models.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Shared context for writing register entries.
    /// </summary>
    private readonly record struct RegisterWriteContext(
        ImmutableEquatableSet<string>? AsyncInitServiceTypeNames);

    /// <summary>
    /// Base model for a single registration entry that can write itself.
    /// </summary>
    private abstract record class RegisterEntry(ServiceRegistrationModel Registration)
    {
        public abstract void WriteRegistration(SourceWriter writer, RegisterWriteContext context);
    }

    /// <summary>
    /// Registration entry for simple and open generic service registrations.
    /// </summary>
    private sealed record class SimpleRegisterEntry : RegisterEntry
    {
        public string ServiceTypeExpression { get; }

        public string ImplementationTypeExpression { get; }

        public bool IsKeyed { get; }

        public SimpleRegisterEntry(ServiceRegistrationModel registration)
            : base(registration)
        {
            IsKeyed = registration.Key is not null;

            if(registration.IsOpenGeneric)
            {
                ServiceTypeExpression = ConvertToTypeOf(registration.ServiceType);
                ImplementationTypeExpression = ConvertToTypeOf(registration.ImplementationType);
                return;
            }

            ServiceTypeExpression = registration.ServiceType.Name;
            ImplementationTypeExpression = registration.ImplementationType.Name;
        }

        public override void WriteRegistration(SourceWriter writer, RegisterWriteContext context)
        {
            var lifetime = Registration.Lifetime.Name;

            if(Registration.IsOpenGeneric)
            {
                if(IsKeyed)
                {
                    writer.WriteLine($"services.AddKeyed{lifetime}({ServiceTypeExpression}, {Registration.Key}, {ImplementationTypeExpression});");
                }
                else
                {
                    writer.WriteLine($"services.Add{lifetime}({ServiceTypeExpression}, {ImplementationTypeExpression});");
                }

                return;
            }

            if(IsKeyed)
            {
                writer.WriteLine($"services.AddKeyed{lifetime}<{ServiceTypeExpression}, {ImplementationTypeExpression}>({Registration.Key});");
            }
            else
            {
                writer.WriteLine($"services.Add{lifetime}<{ServiceTypeExpression}, {ImplementationTypeExpression}>();");
            }
        }
    }

    /// <summary>
    /// Registration entry for static instance registrations.
    /// </summary>
    private sealed record class InstanceRegisterEntry : RegisterEntry
    {
        public string InstanceExpression { get; }

        public bool IsKeyed { get; }

        public InstanceRegisterEntry(ServiceRegistrationModel registration)
            : base(registration)
        {
            InstanceExpression = registration.Instance!;
            IsKeyed = registration.Key is not null;
        }

        public override void WriteRegistration(SourceWriter writer, RegisterWriteContext context)
        {
            var serviceTypeName = Registration.ServiceType.Name;

            if(IsKeyed)
            {
                writer.WriteLine($"services.AddKeyedSingleton<{serviceTypeName}>({Registration.Key}, {InstanceExpression});");
            }
            else
            {
                writer.WriteLine($"services.AddSingleton<{serviceTypeName}>({InstanceExpression});");
            }
        }
    }

    /// <summary>
    /// Registration entry for service-type forwarding registrations.
    /// </summary>
    private sealed record class ForwardingRegisterEntry : RegisterEntry
    {
        public string? Key { get; }

        public ForwardingRegisterEntry(ServiceRegistrationModel registration)
            : base(registration)
        {
            Key = registration.Key;
        }

        public override void WriteRegistration(SourceWriter writer, RegisterWriteContext context)
        {
            var serviceTypeName = Registration.ServiceType.Name;
            var implTypeName = Registration.ImplementationType.Name;
            var lifetime = Registration.Lifetime.Name;
            var requiredResolutionMethod = GetServiceResolutionMethod(Key, isOptional: false);
            bool isAsyncInit = context.AsyncInitServiceTypeNames?.Contains(implTypeName) == true;

            if(isAsyncInit)
            {
                var taskServiceTypeName = $"global::System.Threading.Tasks.Task<{serviceTypeName}>";
                var taskImplTypeName = $"global::System.Threading.Tasks.Task<{implTypeName}>";
                if(Key is not null)
                {
                    var requiredCall = BuildServiceCall(requiredResolutionMethod, taskImplTypeName, serviceKey: "key");
                    writer.WriteLine($"services.AddKeyed{lifetime}<{taskServiceTypeName}>({Key}, async ({IServiceProviderGlobalTypeName} sp, object? key) => await {requiredCall});");
                }
                else
                {
                    var requiredCall = BuildServiceCall(requiredResolutionMethod, taskImplTypeName, serviceKey: null);
                    writer.WriteLine($"services.Add{lifetime}<{taskServiceTypeName}>(async ({IServiceProviderGlobalTypeName} sp) => await {requiredCall});");
                }

                return;
            }

            if(Key is not null)
            {
                var requiredCall = BuildServiceCall(requiredResolutionMethod, implTypeName, serviceKey: "key");
                writer.WriteLine($"services.AddKeyed{lifetime}<{serviceTypeName}>({Key}, ({IServiceProviderGlobalTypeName} sp, object? key) => {requiredCall});");
            }
            else
            {
                var requiredCall = BuildServiceCall(requiredResolutionMethod, implTypeName, serviceKey: null);
                writer.WriteLine($"services.Add{lifetime}<{serviceTypeName}>(({IServiceProviderGlobalTypeName} sp) => {requiredCall});");
            }
        }
    }

    /// <summary>
    /// Additional factory parameter binding with pre-computed temporary variable name.
    /// </summary>
    private readonly record struct FactoryAdditionalParameter(
        ParameterData Parameter,
        string TemporaryVariableName);

    /// <summary>
    /// Registration entry for factory method registrations.
    /// </summary>
    private sealed record class FactoryRegisterEntry : RegisterEntry
    {
        public FactoryMethodData FactoryMethodData { get; }

        public string Lifetime { get; }

        public string ServiceTypeName { get; }

        public string? Key { get; }

        public bool IsKeyedRegistration { get; }

        public bool HasServiceProvider { get; }

        public bool HasKey { get; }

        public bool NeedsCast { get; }

        public string FactoryCallPath { get; }

        public bool CanGenerate { get; }

        public ImmutableEquatableArray<FactoryAdditionalParameter> AdditionalParameters { get; }

        public FactoryRegisterEntry(ServiceRegistrationModel registration)
            : base(registration)
        {
            ServiceTypeName = registration.ServiceType.Name;
            Lifetime = registration.Lifetime.Name;
            Key = registration.Key;
            IsKeyedRegistration = registration.Key is not null;

            FactoryMethodData = registration.Factory!;
            HasServiceProvider = FactoryMethodData.HasServiceProvider;
            HasKey = FactoryMethodData.HasKey;
            NeedsCast = FactoryMethodData.ReturnTypeName is not null && FactoryMethodData.ReturnTypeName != ServiceTypeName;

            var genericTypeArgs = BuildGenericFactoryTypeArgs(FactoryMethodData, registration.ServiceType);
            if(FactoryMethodData.TypeParameterCount > 0 && genericTypeArgs is null)
            {
                CanGenerate = false;
                FactoryCallPath = FactoryMethodData.Path;
            }
            else
            {
                CanGenerate = true;
                FactoryCallPath = genericTypeArgs is not null
                    ? $"{FactoryMethodData.Path}<{genericTypeArgs}>"
                    : FactoryMethodData.Path;
            }

            var additional = new FactoryAdditionalParameter[FactoryMethodData.AdditionalParameters.Length];
            for(int i = 0; i < additional.Length; i++)
            {
                additional[i] = new FactoryAdditionalParameter(
                    FactoryMethodData.AdditionalParameters[i],
                    $"f_p{i}");
            }

            AdditionalParameters = additional.ToImmutableEquatableArray();
        }

        public override void WriteRegistration(SourceWriter writer, RegisterWriteContext context)
        {
            if(!CanGenerate)
            {
                return;
            }

            if(AdditionalParameters.Length > 0)
            {
                WriteRegistrationWithAdditionalParameters(writer, context);
                return;
            }

            var factoryInvocation = BuildFactoryInvocationExpression([]);
            WriteFactoryRegistrationLine(writer, Lifetime, ServiceTypeName, Key, factoryInvocation);
        }

        private void WriteRegistrationWithAdditionalParameters(SourceWriter writer, RegisterWriteContext context)
        {
            writer.WriteServiceLambdaOpen(Lifetime, ServiceTypeName, Key);

            writer.WriteLine("{");
            writer.Indentation++;

            var resolvedParameterNames = new List<string>(AdditionalParameters.Length);
            foreach(var additionalParam in AdditionalParameters)
            {
                var resolvedName = ResolveParamAndEmitVar(
                    writer,
                    additionalParam.Parameter,
                    additionalParam.TemporaryVariableName,
                    IsKeyedRegistration,
                    Key,
                    context.AsyncInitServiceTypeNames);
                resolvedParameterNames.Add(resolvedName);
            }

            var factoryInvocation = BuildFactoryInvocationExpression(resolvedParameterNames);
            writer.WriteLine($"return {factoryInvocation};");

            writer.Indentation--;
            writer.WriteLine("});");
        }

        private string BuildFactoryInvocationExpression(List<string> additionalArguments)
        {
            var args = new List<string>(additionalArguments.Count + 2);
            if(HasServiceProvider)
            {
                args.Add("sp");
            }

            if(HasKey && Key is not null)
            {
                args.Add(Key);
            }

            if(additionalArguments.Count > 0)
            {
                args.AddRange(additionalArguments);
            }

            var invocation = $"{FactoryCallPath}({string.Join(", ", args)})";
            if(NeedsCast)
            {
                invocation = $"({ServiceTypeName}){invocation}";
            }

            return invocation;
        }
    }

    /// <summary>
    /// Registration entry for services requiring constructor/property/method injection.
    /// </summary>
    private sealed record class InjectionRegisterEntry : RegisterEntry
    {
        public string Lifetime { get; }

        public string ServiceTypeName { get; }

        public string ImplementationTypeName { get; }

        public string? Key { get; }

        public bool IsKeyedRegistration { get; }

        public ImmutableEquatableArray<ParameterData> ConstructorParameters { get; }

        public ImmutableEquatableArray<InjectionMemberData> PropertyInjectionMembers { get; }

        public ImmutableEquatableArray<InjectionMemberData> MethodInjectionMembers { get; }

        public InjectionRegisterEntry(ServiceRegistrationModel registration)
            : base(registration)
        {
            Lifetime = registration.Lifetime.Name;
            ServiceTypeName = registration.ServiceType.Name;
            ImplementationTypeName = registration.ImplementationType.Name;
            Key = registration.Key;
            IsKeyedRegistration = registration.Key is not null;
            ConstructorParameters = registration.ImplementationType.ConstructorParameters ?? [];

            var (properties, methods) = CategorizeInjectionMembers(registration.InjectionMembers);
            PropertyInjectionMembers = properties?.ToImmutableEquatableArray() ?? [];
            MethodInjectionMembers = methods?.ToImmutableEquatableArray() ?? [];
        }

        public override void WriteRegistration(SourceWriter writer, RegisterWriteContext context)
        {
            writer.WriteServiceLambdaOpen(Lifetime, ServiceTypeName, Key);

            writer.WriteLine("{");
            writer.Indentation++;

            WriteInjectionBody(writer, context, null);

            writer.WriteLine("return s0;");

            writer.Indentation--;
            writer.WriteLine("});");
        }

        internal void WriteInjectionBody(
            SourceWriter writer,
            RegisterWriteContext context,
            ImmutableEquatableArray<InjectionMemberData>? asyncMethodInjectionMembers)
        {
            var constructorParamEntries = new List<(string Name, string? Value, bool NeedsConditional)>(ConstructorParameters.Length);

            for(int i = 0; i < ConstructorParameters.Length; i++)
            {
                var parameter = ConstructorParameters[i];
                var paramVarName = $"p{i}";
                var resolvedVar = ResolveParamAndEmitVar(
                    writer,
                    parameter,
                    paramVarName,
                    IsKeyedRegistration,
                    Key,
                    context.AsyncInitServiceTypeNames);

                constructorParamEntries.Add((parameter.Name, resolvedVar, false));
            }

            var propertyVarNames = new string[PropertyInjectionMembers.Length];
            for(int i = 0; i < PropertyInjectionMembers.Length; i++)
            {
                var member = PropertyInjectionMembers[i];
                var memberVarName = $"s0_p{i}";
                var memberType = member.Type;
                var memberTypeName = memberType is null ? "object" : memberType.Name;
                bool hasNonNullDefault = member.HasDefaultValue && !member.DefaultValueIsNull;

                ResolveMemberValue(
                    writer,
                    memberType,
                    memberTypeName,
                    memberVarName,
                    member.Key,
                    member.IsNullable,
                    hasNonNullDefault,
                    member.DefaultValue,
                    context.AsyncInitServiceTypeNames);

                propertyVarNames[i] = memberVarName;
            }

            int memberParamIndex = PropertyInjectionMembers.Length;
            var syncMethodResolutions = ResolveMethodResolutions(writer, MethodInjectionMembers, ref memberParamIndex, context);
            var asyncMethodResolutions = asyncMethodInjectionMembers is { Length: > 0 } asyncMembers
                ? ResolveMethodResolutions(writer, asyncMembers, ref memberParamIndex, context)
                : new List<(string MethodName, string?[] ParamVars, string[] ParamNames)>();

            var propertyInitializers = new string[propertyVarNames.Length];
            for(int i = 0; i < PropertyInjectionMembers.Length; i++)
            {
                propertyInitializers[i] = $"{PropertyInjectionMembers[i].Name} = {propertyVarNames[i]}";
            }

            var constructorArgs = BuildArgumentListFromEntries([.. constructorParamEntries]);
            var initializerPart = propertyInitializers.Length > 0
                ? $" {{ {string.Join(", ", propertyInitializers)} }}"
                : "";
            var constructorInvocation = BuildConstructorInvocation(ImplementationTypeName, constructorArgs, initializerPart);
            writer.WriteLine($"var s0 = {constructorInvocation};");

            WriteMethodInvocations(writer, syncMethodResolutions, useAwait: false);
            WriteMethodInvocations(writer, asyncMethodResolutions, useAwait: true);
        }

        private List<(string MethodName, string?[] ParamVars, string[] ParamNames)> ResolveMethodResolutions(
            SourceWriter writer,
            ImmutableEquatableArray<InjectionMemberData> methodMembers,
            ref int memberParamIndex,
            RegisterWriteContext context)
        {
            var methodResolutions = new List<(string MethodName, string?[] ParamVars, string[] ParamNames)>(methodMembers.Length);

            foreach(var method in methodMembers)
            {
                var parameters = method.Parameters ?? [];
                var paramVars = new string?[parameters.Length];
                var paramNames = new string[parameters.Length];

                for(int i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i];
                    var paramVarName = $"s0_m{memberParamIndex}";
                    paramNames[i] = parameter.Name;

                    paramVars[i] = method.Key is not null
                        ? ResolveMethodParameterWithKey(
                            writer,
                            parameter,
                            paramVarName,
                            method.Key,
                            IsKeyedRegistration,
                            Key,
                            typeNameResolver: null,
                            context.AsyncInitServiceTypeNames)
                        : ResolveParamAndEmitVar(
                            writer,
                            parameter,
                            paramVarName,
                            IsKeyedRegistration,
                            Key,
                            context.AsyncInitServiceTypeNames);

                    memberParamIndex++;
                }

                methodResolutions.Add((method.Name, paramVars, paramNames));
            }

            return methodResolutions;
        }

        private static void WriteMethodInvocations(
            SourceWriter writer,
            List<(string MethodName, string?[] ParamVars, string[] ParamNames)> methodResolutions,
            bool useAwait)
        {
            foreach(var (methodName, paramVars, paramNames) in methodResolutions)
            {
                var entries = new List<(string Name, string? Value, bool NeedsConditional)>(paramVars.Length);

                for(int i = 0; i < paramVars.Length; i++)
                {
                    entries.Add((paramNames[i], paramVars[i], false));
                }

                var args = BuildArgumentListFromEntries([.. entries]);
                writer.WriteLine(useAwait
                    ? $"await s0.{methodName}({args});"
                    : $"s0.{methodName}({args});");
            }
        }
    }

    /// <summary>
    /// Registration entry for services requiring async injection method initialization.
    /// </summary>
    private sealed record class AsyncInjectionRegisterEntry : RegisterEntry
    {
        private readonly InjectionRegisterEntry injectionEntry;

        public string TaskServiceTypeName { get; }

        public string TaskImplementationTypeName { get; }

        public ImmutableEquatableArray<InjectionMemberData> AsyncMethodInjectionMembers { get; }

        public AsyncInjectionRegisterEntry(ServiceRegistrationModel registration)
            : base(registration)
        {
            injectionEntry = new InjectionRegisterEntry(registration);
            TaskServiceTypeName = $"global::System.Threading.Tasks.Task<{registration.ServiceType.Name}>";
            TaskImplementationTypeName = $"global::System.Threading.Tasks.Task<{registration.ImplementationType.Name}>";
            AsyncMethodInjectionMembers = registration.InjectionMembers
                .Where(static member => member.MemberType == InjectionMemberType.AsyncMethod)
                .ToImmutableEquatableArray();
        }

        public override void WriteRegistration(SourceWriter writer, RegisterWriteContext context)
        {
            writer.WriteServiceLambdaOpen(Registration.Lifetime.Name, TaskServiceTypeName, Registration.Key);

            writer.WriteLine("{");
            writer.Indentation++;

            writer.WriteLine($"async {TaskImplementationTypeName} Init()");
            writer.WriteLine("{");
            writer.Indentation++;

            injectionEntry.WriteInjectionBody(writer, context, AsyncMethodInjectionMembers);

            writer.WriteLine("return s0;");

            writer.Indentation--;
            writer.WriteLine("}");

            writer.WriteLine("return Init();");

            writer.Indentation--;
            writer.WriteLine("});");
        }
    }

    /// <summary>
    /// Registration entry for decorator-chain registrations.
    /// </summary>
    private sealed record class DecoratorRegisterEntry : RegisterEntry
    {
        public string Lifetime { get; }

        public ImmutableEquatableArray<TypeData> Decorators { get; }

        public FactoryMethodData? Factory { get; }

        public DecoratorRegisterEntry(ServiceRegistrationModel registration)
            : base(registration)
        {
            Lifetime = registration.Lifetime.Name;
            Decorators = registration.Decorators;
            Factory = registration.Factory;
        }

        public override void WriteRegistration(SourceWriter writer, RegisterWriteContext context)
        {
            WriteDecoratorRegistration(writer, Registration, Lifetime, context.AsyncInitServiceTypeNames);
        }
    }
}