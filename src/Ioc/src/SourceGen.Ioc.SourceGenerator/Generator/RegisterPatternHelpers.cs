using static SourceGen.Ioc.SourceGenerator.Models.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private static void WriteRegistration(SourceWriter writer, RegisterOutputEntry outputEntry, ImmutableEquatableSet<string>? asyncInitServiceTypeNames = null)
    {
        var registration = outputEntry.Registration;
        var lifetime = registration.Lifetime.Name;
        bool hasFactory = outputEntry.HasFactory;
        bool hasInstance = outputEntry.HasInstance;
        bool hasClosedDecorators = outputEntry.HasClosedDecorators;
        bool needsFactoryConstruction = outputEntry.NeedsFactoryConstruction;
        bool hasAsyncInjectionMembers = outputEntry.HasAsyncInjectionMembers;
        bool shouldForwardServiceType = outputEntry.ShouldForwardServiceType;

        if(hasFactory)
        {
            WriteFactoryMethodRegistration(writer, registration, lifetime, asyncInitServiceTypeNames);
            return;
        }

        if(hasInstance)
        {
            if(registration.Lifetime == ServiceLifetime.Singleton)
            {
                WriteInstanceRegistration(writer, registration);
            }

            return;
        }

        if(hasClosedDecorators)
        {
            WriteDecoratorRegistration(writer, registration, lifetime, asyncInitServiceTypeNames);
            return;
        }

        if(shouldForwardServiceType)
        {
            WriteServiceTypeForwardingRegistration(writer, registration, lifetime, asyncInitServiceTypeNames);
            return;
        }

        if(needsFactoryConstruction && hasAsyncInjectionMembers && !registration.IsOpenGeneric)
        {
            WriteAsyncInjectionRegistration(writer, registration, lifetime, asyncInitServiceTypeNames);
            return;
        }

        if(needsFactoryConstruction && !hasAsyncInjectionMembers && !registration.IsOpenGeneric)
        {
            WriteInjectionRegistration(writer, registration, lifetime, asyncInitServiceTypeNames);
            return;
        }

        if(registration.IsOpenGeneric && registration.Key is not null)
        {
            var serviceTypeOf = ConvertToTypeOf(registration.ServiceType);
            var implTypeOf = ConvertToTypeOf(registration.ImplementationType);
            writer.WriteLine($"services.AddKeyed{lifetime}({serviceTypeOf}, {registration.Key}, {implTypeOf});");
            return;
        }

        if(registration.IsOpenGeneric && registration.Key is null)
        {
            var serviceTypeOf = ConvertToTypeOf(registration.ServiceType);
            var implTypeOf = ConvertToTypeOf(registration.ImplementationType);
            writer.WriteLine($"services.Add{lifetime}({serviceTypeOf}, {implTypeOf});");
            return;
        }

        if(registration.Key is not null)
        {
            writer.WriteLine($"services.AddKeyed{lifetime}<{registration.ServiceType.Name}, {registration.ImplementationType.Name}>({registration.Key});");
            return;
        }

        writer.WriteLine($"services.Add{lifetime}<{registration.ServiceType.Name}, {registration.ImplementationType.Name}>();");
    }

    /// <summary>
    /// Writes registration code using a factory method specified in the attribute.
    /// </summary>
    /// <remarks>
    /// Supports factory methods with different parameter combinations:
    /// <list type="bullet">
    ///   <item>No parameters: <c>services.AddSingleton&lt;IService&gt;(Factory());</c></item>
    ///   <item>IServiceProvider only: <c>services.AddSingleton&lt;IService&gt;(sp => Factory(sp));</c></item>
    ///   <item>object key only (keyed): <c>services.AddKeyedSingleton&lt;IService&gt;("key", (sp, key) => Factory(key));</c></item>
    ///   <item>Both (keyed): <c>services.AddKeyedSingleton&lt;IService&gt;("key", (sp, key) => Factory(sp, key));</c></item>
    ///   <item>Additional parameters: resolved from IServiceProvider using the same logic as [IocInject] methods</item>
    ///   <item>Generic factory with [IocGenericFactory]: <c>services.AddSingleton&lt;IHandler&lt;Request&gt;&gt;(sp => Factory&lt;Request&gt;(sp));</c></item>
    /// </list>
    /// If the factory return type differs from the service type, adds a cast.
    /// </remarks>
    private static void WriteFactoryMethodRegistration(SourceWriter writer, ServiceRegistrationModel registration, string lifetime, ImmutableEquatableSet<string>? asyncInitServiceTypeNames = null)
    {
        var serviceTypeName = registration.ServiceType.Name;
        var factory = registration.Factory!;
        var factoryPath = factory.Path;
        var hasServiceProvider = factory.HasServiceProvider;
        var hasKey = factory.HasKey;
        var returnTypeName = factory.ReturnTypeName;
        var additionalParameters = factory.AdditionalParameters;
        bool isKeyedRegistration = registration.Key is not null;

        var genericTypeArgs = BuildGenericFactoryTypeArgs(factory, registration.ServiceType);
        if(factory.TypeParameterCount > 0 && genericTypeArgs is null)
        {
            return;
        }

        bool hasAdditionalParameters = additionalParameters.Length > 0;
        if(hasAdditionalParameters)
        {
            WriteFactoryMethodRegistrationWithAdditionalParams(
                writer,
                registration,
                lifetime,
                serviceTypeName,
                factoryPath,
                hasServiceProvider,
                hasKey,
                returnTypeName,
                additionalParameters,
                isKeyedRegistration,
                genericTypeArgs,
                asyncInitServiceTypeNames);
            return;
        }

        var factoryInvocation = BuildFactoryInvocation(
            factoryPath,
            genericTypeArgs,
            hasServiceProvider,
            hasKey,
            registration.Key,
            [],
            returnTypeName,
            serviceTypeName);

        WriteFactoryRegistrationLine(writer, lifetime, serviceTypeName, registration.Key, factoryInvocation);
    }

    /// <summary>
    /// Writes factory method registration with additional parameters that need to be resolved from IServiceProvider.
    /// </summary>
    private static void WriteFactoryMethodRegistrationWithAdditionalParams(
        SourceWriter writer,
        ServiceRegistrationModel registration,
        string lifetime,
        string serviceTypeName,
        string factoryPath,
        bool hasServiceProvider,
        bool hasKey,
        string? returnTypeName,
        ImmutableEquatableArray<ParameterData> additionalParameters,
        bool isKeyedRegistration,
        string? genericTypeArgs = null,
        ImmutableEquatableSet<string>? asyncInitServiceTypeNames = null)
    {
        writer.WriteServiceLambdaOpen(lifetime, serviceTypeName, registration.Key);

        writer.WriteLine("{");
        writer.Indentation++;

        var paramVars = new List<string>(additionalParameters.Length);
        int paramIndex = 0;
        foreach(var param in additionalParameters)
        {
            var paramVar = $"f_p{paramIndex}";
            var varName = ResolveParamAndEmitVar(writer, param, paramVar, isKeyedRegistration, registration.Key, asyncInitServiceTypeNames);
            paramVars.Add(varName);
            paramIndex++;
        }

        var factoryInvocation = BuildFactoryInvocation(
            factoryPath,
            genericTypeArgs,
            hasServiceProvider,
            hasKey,
            registration.Key,
            paramVars,
            returnTypeName,
            serviceTypeName);

        writer.WriteLine($"return {factoryInvocation};");

        writer.Indentation--;
        writer.WriteLine("});");
    }

    /// <summary>
    /// Builds the factory invocation expression with optional generic arguments, service provider, key, and additional parameters.
    /// Adds a cast when the factory return type differs from the service type.
    /// </summary>
    private static string BuildFactoryInvocation(
        string factoryPath,
        string? genericTypeArgs,
        bool hasServiceProvider,
        bool hasKey,
        string? registrationKey,
        List<string> additionalArgs,
        string? returnTypeName,
        string serviceTypeName)
    {
        var args = new List<string>(additionalArgs.Count + 2);
        if(hasServiceProvider)
        {
            args.Add("sp");
        }

        if(hasKey && registrationKey is not null)
        {
            args.Add(registrationKey);
        }

        if(additionalArgs.Count > 0)
        {
            args.AddRange(additionalArgs);
        }

        var factoryCallPath = genericTypeArgs is not null ? $"{factoryPath}<{genericTypeArgs}>" : factoryPath;
        var factoryInvocation = $"{factoryCallPath}({string.Join(", ", args)})";

        if(returnTypeName is not null && returnTypeName != serviceTypeName)
        {
            factoryInvocation = $"({serviceTypeName}){factoryInvocation}";
        }

        return factoryInvocation;
    }

    /// <summary>
    /// Writes a factory registration line for keyed or non-keyed services.
    /// </summary>
    private static void WriteFactoryRegistrationLine(
        SourceWriter writer,
        string lifetime,
        string serviceTypeName,
        string? registrationKey,
        string factoryInvocation)
    {
        if(registrationKey is not null)
        {
            writer.WriteLine($"services.AddKeyed{lifetime}<{serviceTypeName}>({registrationKey}, ({IServiceProviderGlobalTypeName} sp, object? key) => {factoryInvocation});");
            return;
        }

        writer.WriteLine($"services.Add{lifetime}<{serviceTypeName}>(({IServiceProviderGlobalTypeName} sp) => {factoryInvocation});");
    }

    /// <summary>
    /// Writes registration code using a static instance specified in the attribute.
    /// Instance registrations are only valid for Singleton lifetime.
    /// </summary>
    /// <remarks>
    /// Generates code like:
    /// <code>
    /// services.AddSingleton&lt;IMySrevice&gt;(MyService.Default);
    /// // or for keyed:
    /// services.AddKeyedSingleton&lt;IMySrevice&gt;("key", MyService.Default);
    /// </code>
    /// </remarks>
    private static void WriteInstanceRegistration(SourceWriter writer, ServiceRegistrationModel registration)
    {
        var serviceTypeName = registration.ServiceType.Name;
        var instance = registration.Instance!;

        if(registration.Key is not null)
        {
            writer.WriteLine($"services.AddKeyedSingleton<{serviceTypeName}>({registration.Key}, {instance});");
        }
        else
        {
            writer.WriteLine($"services.AddSingleton<{serviceTypeName}>({instance});");
        }
    }

    /// <summary>
    /// Writes registration code for service types (interfaces/base classes) that forward to an already-registered implementation.
    /// The implementation is already registered with its own factory method, so we just resolve it from the service provider.
    /// </summary>
    /// <remarks>
    /// Generates code like:
    /// <code>
    /// services.AddTransient&lt;IMyService&gt;(sp => sp.GetRequiredService&lt;MyService&gt;());
    /// </code>
    /// </remarks>
    private static void WriteServiceTypeForwardingRegistration(SourceWriter writer, ServiceRegistrationModel registration, string lifetime, ImmutableEquatableSet<string>? asyncInitServiceTypeNames = null)
    {
        var serviceTypeName = registration.ServiceType.Name;
        var implTypeName = registration.ImplementationType.Name;
        var requiredResolutionMethod = GetServiceResolutionMethod(registration.Key, isOptional: false);

        bool isAsyncInit = asyncInitServiceTypeNames?.Contains(implTypeName) == true;
        if(isAsyncInit)
        {
            var taskServiceTypeName = $"global::System.Threading.Tasks.Task<{serviceTypeName}>";
            var taskImplTypeName = $"global::System.Threading.Tasks.Task<{implTypeName}>";
            if(registration.Key is not null)
            {
                var requiredCall = BuildServiceCall(requiredResolutionMethod, taskImplTypeName, serviceKey: "key");
                writer.WriteLine($"services.AddKeyed{lifetime}<{taskServiceTypeName}>({registration.Key}, async ({IServiceProviderGlobalTypeName} sp, object? key) => await {requiredCall});");
            }
            else
            {
                var requiredCall = BuildServiceCall(requiredResolutionMethod, taskImplTypeName, serviceKey: null);
                writer.WriteLine($"services.Add{lifetime}<{taskServiceTypeName}>(async ({IServiceProviderGlobalTypeName} sp) => await {requiredCall});");
            }
            return;
        }

        if(registration.Key is not null)
        {
            var requiredCall = BuildServiceCall(requiredResolutionMethod, implTypeName, serviceKey: "key");
            writer.WriteLine($"services.AddKeyed{lifetime}<{serviceTypeName}>({registration.Key}, ({IServiceProviderGlobalTypeName} sp, object? key) => {requiredCall});");
        }
        else
        {
            var requiredCall = BuildServiceCall(requiredResolutionMethod, implTypeName, serviceKey: null);
            writer.WriteLine($"services.Add{lifetime}<{serviceTypeName}>(({IServiceProviderGlobalTypeName} sp) => {requiredCall});");
        }
    }

    /// <summary>
    /// Writes registration code for services with injection members (properties, fields, methods marked with InjectAttribute).
    /// </summary>
    private static void WriteInjectionRegistration(SourceWriter writer, ServiceRegistrationModel registration, string lifetime, ImmutableEquatableSet<string>? asyncInitServiceTypeNames = null)
    {
        var serviceTypeName = registration.ServiceType.Name;
        var implTypeName = registration.ImplementationType.Name;
        var injectionMembers = registration.InjectionMembers;

        writer.WriteServiceLambdaOpen(lifetime, serviceTypeName, registration.Key);

        writer.WriteLine("{");
        writer.Indentation++;

        bool isKeyedRegistration = registration.Key is not null;
        WriteConstructInstanceWithInjection(
            writer,
            instanceVarName: "s0",
            implTypeName: implTypeName,
            constructorParams: registration.ImplementationType.ConstructorParameters,
            injectionMembers: injectionMembers,
            isKeyedRegistration: isKeyedRegistration,
            registrationKey: registration.Key,
            serviceTypeNames: null,
            ctorTypeNameResolver: null,
            memberTypeNameResolver: null,
            decoratedPrevVar: null,
            asyncInitServiceTypeNames: asyncInitServiceTypeNames);

        writer.WriteLine("return s0;");

        writer.Indentation--;
        writer.WriteLine("});");
    }

    /// <summary>
    /// Writes registration code for services that have async injection methods.
    /// Generates a <c>Task&lt;T&gt;</c> registration with an async local <c>Init()</c> function
    /// that performs construction, sync injection, and awaited async injection in order.
    /// </summary>
    private static void WriteAsyncInjectionRegistration(SourceWriter writer, ServiceRegistrationModel registration, string lifetime, ImmutableEquatableSet<string>? asyncInitServiceTypeNames = null)
    {
        var serviceTypeName = registration.ServiceType.Name;
        var implTypeName = registration.ImplementationType.Name;
        var injectionMembers = registration.InjectionMembers;
        bool isKeyedRegistration = registration.Key is not null;

        var taskServiceTypeName = $"global::System.Threading.Tasks.Task<{serviceTypeName}>";
        var taskImplTypeName = $"global::System.Threading.Tasks.Task<{implTypeName}>";

        writer.WriteServiceLambdaOpen(lifetime, taskServiceTypeName, registration.Key);

        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine($"async {taskImplTypeName} Init()");
        writer.WriteLine("{");
        writer.Indentation++;

        WriteConstructInstanceWithInjection(
            writer,
            instanceVarName: "s0",
            implTypeName: implTypeName,
            constructorParams: registration.ImplementationType.ConstructorParameters,
            injectionMembers: injectionMembers,
            isKeyedRegistration: isKeyedRegistration,
            registrationKey: registration.Key,
            serviceTypeNames: null,
            ctorTypeNameResolver: null,
            memberTypeNameResolver: null,
            decoratedPrevVar: null,
            asyncInitServiceTypeNames: asyncInitServiceTypeNames,
            isAsyncMode: true);

        writer.WriteLine("return s0;");

        writer.Indentation--;
        writer.WriteLine("}");

        writer.WriteLine("return Init();");

        writer.Indentation--;
        writer.WriteLine("});");
    }
}
