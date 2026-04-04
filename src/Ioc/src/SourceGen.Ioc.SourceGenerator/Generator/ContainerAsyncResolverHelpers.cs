namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    // ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the async routing resolver method name by appending "Async" to the sync method name.
    /// </summary>
    private static string GetAsyncResolverMethodName(string syncMethodName)
        => syncMethodName + "Async";

    /// <summary>
    /// Returns the async creation method name (e.g. "CreateFooBarAsync" from "GetFooBar").
    /// </summary>
    private static string GetAsyncCreateMethodName(string syncMethodName)
    {
        if(syncMethodName.Length > 3 && syncMethodName.StartsWith("Get", StringComparison.Ordinal))
            return "Create" + syncMethodName[3..] + "Async";
        return syncMethodName + "_CreateAsync";
    }

    /// <summary>
    /// Returns the effective thread-safety strategy for a registration.
    /// Async-init services auto-upgrade async-incompatible strategies to <see cref="ThreadSafeStrategy.SemaphoreSlim"/>.
    /// </summary>
    private static ThreadSafeStrategy GetEffectiveThreadSafeStrategy(
        ThreadSafeStrategy strategy,
        bool isAsyncInit)
    {
        if(!isAsyncInit)
            return strategy;

        return strategy is ThreadSafeStrategy.None ? ThreadSafeStrategy.None : ThreadSafeStrategy.SemaphoreSlim;
    }

    /// <summary>
    /// Writes the field declaration for an async-init service's cached <c>Task&lt;T&gt;</c>.
    /// The caller must pass the effective async-init strategy.
    /// </summary>
    private static void WriteAsyncServiceInstanceField(
        SourceWriter writer,
        ThreadSafeStrategy strategy,
        string fieldName,
        string taskFieldTypeName)
    {
        writer.WriteLine($"private {taskFieldTypeName}? {fieldName};");

        // Only SemaphoreSlim is async-compatible; others fall back to unsynchronized access.
        if(strategy == ThreadSafeStrategy.SemaphoreSlim)
        {
            writer.WriteLine($"private readonly global::System.Threading.SemaphoreSlim {fieldName}Semaphore = new(1, 1);");
        }
    }

    /// <summary>
    /// Writes an async routing resolver + async creation method for a singleton/scoped async-init service.
    /// </summary>
    private static void WriteAsyncServiceResolverMethod(
        SourceWriter writer,
        ThreadSafeStrategy strategy,
        string syncMethodName,
        string returnType,
        string fieldName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        var asyncMethodName = GetAsyncResolverMethodName(syncMethodName);
        var createMethodName = GetAsyncCreateMethodName(syncMethodName);
        var taskReturnType = $"global::System.Threading.Tasks.Task<{returnType}>";
        var effectiveStrategy = GetEffectiveThreadSafeStrategy(strategy, true);

        // Write the Task<T>? instance field (+ semaphore if SemaphoreSlim)
        WriteAsyncServiceInstanceField(writer, effectiveStrategy, fieldName, taskReturnType);
        writer.WriteLine();

        // ── Routing resolver method ──
        writer.WriteLine($"private async {taskReturnType} {asyncMethodName}()");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine($"if({fieldName} is not null)");
        writer.Indentation++;
        writer.WriteLine($"return await {fieldName};");
        writer.Indentation--;
        writer.WriteLine();

        if(effectiveStrategy == ThreadSafeStrategy.SemaphoreSlim)
        {
            WriteAsyncResolverBodySemaphoreSlim(writer, fieldName, createMethodName);
        }
        else
        {
            WriteAsyncResolverBodyNone(writer, fieldName, createMethodName);
        }

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // ── Creation method ──
        writer.WriteLine($"private async {taskReturnType} {createMethodName}()");
        writer.WriteLine("{");
        writer.Indentation++;

        WriteAsyncInstanceCreationBody(writer, reg, hasFactory, hasDecorators, groups);

        writer.Indentation--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes an async creation method for a transient async-init service.
    /// Each call produces a new Task (no caching).
    /// </summary>
    private static void WriteAsyncTransientResolverMethod(
        SourceWriter writer,
        string syncMethodName,
        string returnType,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        var createMethodName = GetAsyncCreateMethodName(syncMethodName);
        var taskReturnType = $"global::System.Threading.Tasks.Task<{returnType}>";

        writer.WriteLine($"private async {taskReturnType} {createMethodName}()");
        writer.WriteLine("{");
        writer.Indentation++;

        WriteAsyncInstanceCreationBody(writer, reg, hasFactory, hasDecorators, groups);

        writer.Indentation--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes the instance creation body for an async-init service:
    /// constructor, sync injection (properties + sync methods), await async methods, optional decorators.
    /// </summary>
    private static void WriteAsyncInstanceCreationBody(
        SourceWriter writer,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        var (properties, syncMethods, asyncMethods) = CategorizeInjectionMembersAsync(reg.InjectionMembers);
        var args = BuildConstructorArgumentsString(reg, groups);

        // When decorators are present AND there is method injection (sync or async), we cannot
        // type the instance as the service interface because [IocInject] methods may be on the
        // concrete implementation only.
        //
        // Two-variable pattern:
        //   var baseInstance = new Impl(args) { Props... };
        //   baseInstance.SyncMethod(...);
        //   await baseInstance.AsyncMethod(...);
        //   ServiceType instance = baseInstance;
        //   instance = new Decorator(instance);   // decorator chain
        //
        // Single-variable pattern (no decorators, or decorators + pure property injection):
        //   var instance = new Impl(args) { Props... };
        //   await instance.AsyncInit(...);
        bool hasMethods = syncMethods is { Count: > 0 } || asyncMethods is { Count: > 0 };
        bool needsTwoVarPattern = hasDecorators && hasMethods;

        // ── Create the instance ──
        string injectionVar = needsTwoVarPattern ? "baseInstance" : "instance";
        string varTypeDecl = (hasDecorators && !needsTwoVarPattern) ? reg.ServiceType.Name : "var";

        if(hasFactory)
        {
            var factoryCall = BuildFactoryCallForContainer(reg.Factory!, reg, groups);
            writer.WriteLine($"{varTypeDecl} {injectionVar} = ({reg.ImplementationType.Name}){factoryCall};");
        }
        else
        {
            WriteConstructorWithPropertyInitializers(writer, injectionVar, varTypeDecl, reg.ImplementationType.Name, args, properties, groups);
        }

        // ── Sync method injection ──
        if(syncMethods is { Count: > 0 })
        {
            foreach(var method in syncMethods)
            {
                var methodArgs = method.Parameters is { Length: > 0 }
                    ? string.Join(", ", method.Parameters.Select(p => BuildParameterForContainer(p, reg, groups)))
                    : "";
                writer.WriteLine($"{injectionVar}.{method.Name}({methodArgs});");
            }
        }

        // ── Awaited async method injection ──
        if(asyncMethods is { Count: > 0 })
        {
            foreach(var method in asyncMethods)
            {
                var methodArgs = method.Parameters is { Length: > 0 }
                    ? string.Join(", ", method.Parameters.Select(p => BuildParameterForContainer(p, reg, groups)))
                    : "";
                writer.WriteLine($"await {injectionVar}.{method.Name}({methodArgs});");
            }
        }

        // ── Apply decorators after all injection ──
        if(hasDecorators)
        {
            writer.WriteLine();
            if(needsTwoVarPattern)
            {
                // Convert the concrete implementation variable to the service type
                // so the decorator chain can reassign the variable.
                writer.WriteLine($"{reg.ServiceType.Name} instance = {injectionVar};");
            }
            WriteDecoratorApplication(writer, "instance", reg, groups);
        }

        writer.WriteLine("return instance;");
    }

    /// <summary>
    /// Writes the async routing resolver body for <see cref="ThreadSafeStrategy.None"/> (no synchronization).
    /// </summary>
    private static void WriteAsyncResolverBodyNone(
        SourceWriter writer,
        string fieldName,
        string createMethodName)
    {
        writer.WriteLine($"{fieldName} = {createMethodName}();");
        writer.WriteLine($"return await {fieldName};");
    }

    /// <summary>
    /// Writes the async routing resolver body for <see cref="ThreadSafeStrategy.SemaphoreSlim"/>.
    /// Uses <c>WaitAsync()</c> for async-compatible locking.
    /// </summary>
    private static void WriteAsyncResolverBodySemaphoreSlim(
        SourceWriter writer,
        string fieldName,
        string createMethodName)
    {
        writer.WriteLine($"await {fieldName}Semaphore.WaitAsync();");
        writer.WriteLine("try");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine($"if({fieldName} is null)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine($"{fieldName} = {createMethodName}();");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine("finally");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine($"{fieldName}Semaphore.Release();");
        writer.Indentation--;
        writer.WriteLine("}");

        writer.WriteLine($"return await {fieldName};");
    }

    /// <summary>
    /// Categorizes injection members into properties/fields, sync methods, and async methods.
    /// </summary>
    private static (List<InjectionMemberData>? Properties, List<InjectionMemberData>? SyncMethods, List<InjectionMemberData>? AsyncMethods) CategorizeInjectionMembersAsync(
        ImmutableEquatableArray<InjectionMemberData> injectionMembers)
    {
        List<InjectionMemberData>? properties = null;
        List<InjectionMemberData>? syncMethods = null;
        List<InjectionMemberData>? asyncMethods = null;

        foreach(var member in injectionMembers)
        {
            switch(member.MemberType)
            {
                case InjectionMemberType.Property:
                case InjectionMemberType.Field:
                    properties ??= [];
                    properties.Add(member);
                    break;
                case InjectionMemberType.Method:
                    syncMethods ??= [];
                    syncMethods.Add(member);
                    break;
                case InjectionMemberType.AsyncMethod:
                    asyncMethods ??= [];
                    asyncMethods.Add(member);
                    break;
            }
        }

        return (properties, syncMethods, asyncMethods);
    }

}
