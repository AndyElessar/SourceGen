
namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private static void WriteServiceResolverMethods(
        SourceWriter writer,
        ThreadSafeStrategy strategy,
        ContainerRegistrationGroups groups)
    {
        writer.WriteLine("#region Service Resolution");
        writer.WriteLine();

        var writtenMethods = new HashSet<string>();

        // All registrations are already filtered (no open generics)
        WriteServiceResolverGroup(writer, strategy, groups.Singletons, writtenMethods, groups);
        WriteServiceResolverGroup(writer, strategy, groups.Scoped, writtenMethods, groups);
        WriteServiceResolverGroup(writer, strategy, groups.Transients, writtenMethods, groups);

        // Write array resolver methods for IEnumerable<T>, IReadOnlyCollection<T>, IReadOnlyList<T>, T[] support
        foreach(var serviceType in groups.CollectionServiceTypes)
        {
            WriteArrayResolverMethod(writer, serviceType, groups);
            writer.WriteLine();
        }

        // Write KVP resolver methods for keyed services consumed as KeyValuePair/Dictionary
        WriteContainerKvpResolverMethods(writer, groups.KvpEntries);

        // Write Lazy wrapper field declarations and array resolvers
        WriteContainerLazyFields(writer, groups.LazyEntries);

        // Write Func wrapper field declarations and array resolvers
        WriteContainerFuncFields(writer, groups.FuncEntries);

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes implementations for user-declared partial methods and partial properties
    /// that serve as fast-path service accessors.
    /// </summary>
    private static void WriteServiceResolverGroup(
        SourceWriter writer,
        ThreadSafeStrategy strategy,
        IEnumerable<CachedRegistration> registrations,
        HashSet<string> writtenMethods,
        ContainerRegistrationGroups groups)
    {
        foreach(var cached in registrations)
        {
            if(!writtenMethods.Add(cached.ResolverMethodName))
                continue;
            WriteServiceResolverMethod(writer, strategy, cached, groups);
            writer.WriteLine();
        }
    }

    /// <summary>
    /// Writes an array resolver method for IEnumerable&lt;T&gt;, IReadOnlyCollection&lt;T&gt;, IReadOnlyList&lt;T&gt;, T[] resolution.
    /// </summary>
    private static void WriteArrayResolverMethod(
        SourceWriter writer,
        string serviceType,
        ContainerRegistrationGroups groups)
    {
        var methodName = GetArrayResolverMethodName(serviceType);
        var returnType = $"{serviceType}[]";

        // Use pre-computed collection registrations (already filtered for non-open-generics)
        if(!groups.CollectionRegistrations.TryGetValue(serviceType, out var registrations))
            return;

        // Deduplicate by resolver method name (or instance expression for instance registrations)
        var uniqueKeys = new HashSet<string>();
        var resolverEntries = new List<CachedRegistration>();

        foreach(var cached in registrations)
        {
            // Use instance expression as key for instance registrations, otherwise use method name
            var key = cached.Registration.Instance ?? cached.ResolverMethodName;
            if(uniqueKeys.Add(key))
            {
                resolverEntries.Add(cached);
            }
        }

        // Only generate collection if there are multiple unique entries
        if(resolverEntries.Count < 2)
            return;

        writer.WriteLine($"private {returnType} {methodName}() =>");
        writer.Indentation++;
        writer.WriteLine("[");
        writer.Indentation++;

        foreach(var cached in resolverEntries)
        {
            if(cached.Registration.Instance is not null)
            {
                // Instance registration: directly use the instance expression
                writer.WriteLine($"{cached.Registration.Instance},");
            }
            else
            {
                // Regular registration: call the resolver method
                writer.WriteLine($"{cached.ResolverMethodName}(),");
            }
        }

        writer.Indentation--;
        writer.WriteLine("];");
        writer.Indentation--;
    }

    /// <summary>
    /// Writes a single service resolver method.
    /// </summary>
    private static void WriteServiceResolverMethod(
        SourceWriter writer,
        ThreadSafeStrategy strategy,
        CachedRegistration cached,
        ContainerRegistrationGroups groups)
    {
        var reg = cached.Registration;
        var methodName = cached.ResolverMethodName;
        var fieldName = cached.FieldName;
        var isEager = cached.IsEager;

        // Check if factory or instance registration
        bool hasFactory = reg.Factory is not null;
        bool hasInstance = reg.Instance is not null;
        bool hasDecorators = reg.Decorators.Length > 0;

        // Return type: if there are decorators, return the ServiceType (interface), otherwise ImplementationType
        var returnType = hasDecorators ? reg.ServiceType.Name : reg.ImplementationType.Name;

        // Instance registration: no resolver method needed, will be inlined in _localResolvers
        if(hasInstance)
        {
            return;
        }

        switch((reg.Lifetime, cached.IsAsyncInit))
        {
            case (ServiceLifetime.Singleton, true) or (ServiceLifetime.Scoped, true):
                WriteAsyncServiceResolverMethod(writer, strategy, methodName, returnType, fieldName, reg, hasFactory, hasDecorators, groups);
                break;

            case (ServiceLifetime.Transient, true):
                WriteAsyncTransientResolverMethod(writer, methodName, returnType, reg, hasFactory, hasDecorators, groups);
                break;

            case (ServiceLifetime.Singleton, false) or (ServiceLifetime.Scoped, false):
                // Write field and synchronization field above the resolver method for better readability
                WriteServiceInstanceField(writer, strategy, reg, fieldName, hasDecorators, isEager);

                if(isEager)
                {
                    // Eager services: Get method still creates instance on first call (from constructor)
                    // but no synchronization needed since constructor runs single-threaded
                    WriteEagerResolverMethod(writer, methodName, returnType, fieldName, reg, hasFactory, hasDecorators, groups);
                }
                else
                {
                    // Lazy services: write resolver method based on thread-safe strategy
                    WriteResolverMethodWithThreadSafety(writer, strategy, methodName, returnType, fieldName, reg, hasFactory, hasDecorators, groups);
                }
                break;

            case (ServiceLifetime.Transient, false):
                WriteTransientResolverMethod(writer, methodName, returnType, reg, hasFactory, hasDecorators, groups);
                break;
        }
    }

    /// <summary>
    /// Writes resolver method for eager services.
    /// Eager services are initialized in the constructor, so no synchronization is needed.
    /// The Get method still handles first-call initialization for dependency resolution.
    /// </summary>
    private static void WriteEagerResolverMethod(
        SourceWriter writer,
        string methodName,
        string returnType,
        string fieldName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        writer.WriteLine($"private {returnType} {methodName}()");
        writer.WriteLine("{");
        writer.Indentation++;

        // For non-nullable reference types, we use object comparison instead of pattern matching
        // since the field is initialized to null! and will be set during constructor
        writer.WriteEarlyReturnIfNotNull(fieldName);
        writer.WriteLine();

        // No synchronization needed - constructor runs single-threaded
        var variableType = hasDecorators ? reg.ServiceType.Name : null;
        WriteInstanceCreationWithInjection(writer, "instance", reg, hasFactory, variableType, groups);

        if(hasDecorators)
        {
            writer.WriteLine();
            WriteDecoratorApplication(writer, "instance", reg, groups);
        }

        writer.WriteLine();
        writer.WriteFieldAssignAndReturn(fieldName, "instance");

        writer.Indentation--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes resolver method based on the specified thread-safety strategy.
    /// </summary>
    private static void WriteResolverMethodWithThreadSafety(
        SourceWriter writer,
        ThreadSafeStrategy strategy,
        string methodName,
        string returnType,
        string fieldName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        writer.WriteLine($"private {returnType} {methodName}()");
        writer.WriteLine("{");
        writer.Indentation++;

        // Early return check (common to all strategies)
        writer.WriteEarlyReturnIfNotNull(fieldName);
        writer.WriteLine();

        Action<SourceWriter, string, ServiceRegistrationModel, bool, bool, ContainerRegistrationGroups> writeResolverBody = strategy switch
        {
            ThreadSafeStrategy.None => WriteResolverBodyNone,
            ThreadSafeStrategy.Lock => WriteResolverBodyLock,
            ThreadSafeStrategy.SemaphoreSlim => WriteResolverBodySemaphoreSlim,
            ThreadSafeStrategy.SpinLock => WriteResolverBodySpinLock,
            ThreadSafeStrategy.CompareExchange => WriteResolverBodyCompareExchange,
            _ => WriteResolverBodyNone
        };

        writeResolverBody(writer, fieldName, reg, hasFactory, hasDecorators, groups);

        writer.Indentation--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes resolver body for ThreadSafeStrategy.None (no synchronization).
    /// </summary>
    private static void WriteResolverBodyNone(
        SourceWriter writer,
        string fieldName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        WriteInstanceCreationAndAssignment(writer, fieldName, reg, hasFactory, hasDecorators, groups);
    }

    /// <summary>
    /// Writes resolver body for ThreadSafeStrategy.Lock (using lock statement).
    /// </summary>
    private static void WriteResolverBodyLock(
        SourceWriter writer,
        string fieldName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        writer.WriteLine($"lock({fieldName}Lock)");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteEarlyReturnIfNotNull(fieldName);
        writer.WriteLine();

        WriteInstanceCreationAndAssignment(writer, fieldName, reg, hasFactory, hasDecorators, groups);

        writer.Indentation--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes resolver body for ThreadSafeStrategy.SemaphoreSlim.
    /// </summary>
    private static void WriteResolverBodySemaphoreSlim(
        SourceWriter writer,
        string fieldName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        writer.WriteLine($"{fieldName}Semaphore.Wait();");
        writer.WriteLine("try");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteEarlyReturnIfNotNull(fieldName);
        writer.WriteLine();

        WriteInstanceCreationAndAssignment(writer, fieldName, reg, hasFactory, hasDecorators, groups);

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine("finally");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine($"{fieldName}Semaphore.Release();");
        writer.Indentation--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes resolver body for ThreadSafeStrategy.SpinLock.
    /// </summary>
    private static void WriteResolverBodySpinLock(
        SourceWriter writer,
        string fieldName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        writer.WriteLine("bool lockTaken = false;");
        writer.WriteLine("try");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine($"{fieldName}SpinLock.Enter(ref lockTaken);");
        writer.WriteEarlyReturnIfNotNull(fieldName);
        writer.WriteLine();

        WriteInstanceCreationAndAssignment(writer, fieldName, reg, hasFactory, hasDecorators, groups);

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine("finally");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine($"if(lockTaken) {fieldName}SpinLock.Exit();");
        writer.Indentation--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes resolver body for ThreadSafeStrategy.CompareExchange (lock-free CAS pattern).
    /// Uses Interlocked.CompareExchange to atomically set the field. If another thread wins the race,
    /// the losing instance is disposed via DisposeService and the winning instance is returned.
    /// </summary>
    private static void WriteResolverBodyCompareExchange(
        SourceWriter writer,
        string fieldName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        var variableType = hasDecorators ? reg.ServiceType.Name : null;
        WriteInstanceCreationWithInjection(writer, "instance", reg, hasFactory, variableType, groups);

        if(hasDecorators)
        {
            writer.WriteLine();
            WriteDecoratorApplication(writer, "instance", reg, groups);
        }

        writer.WriteLine();
        writer.WriteLine($"var existing = Interlocked.CompareExchange(ref {fieldName}, instance, null);");
        writer.WriteLine("if(existing is not null)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("DisposeService(instance);");
        writer.WriteLine("return existing;");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine("return instance;");
    }

    /// <summary>
    /// Writes the shared instance creation tail used by synchronized resolver bodies.
    /// </summary>
    private static void WriteInstanceCreationAndAssignment(
        SourceWriter writer,
        string fieldName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        var variableType = hasDecorators ? reg.ServiceType.Name : null;
        WriteInstanceCreationWithInjection(writer, "instance", reg, hasFactory, variableType, groups);

        if(hasDecorators)
        {
            writer.WriteLine();
            WriteDecoratorApplication(writer, "instance", reg, groups);
        }

        writer.WriteLine();
        writer.WriteFieldAssignAndReturn(fieldName, "instance");
    }

    /// <summary>
    /// Writes transient resolver method (creates new instance each time).
    /// </summary>
    private static void WriteTransientResolverMethod(
        SourceWriter writer,
        string methodName,
        string returnType,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        writer.WriteLine($"private {returnType} {methodName}()");
        writer.WriteLine("{");
        writer.Indentation++;

        // Simple case: no injection members and no decorators
        if(reg.InjectionMembers.Length == 0 && !hasDecorators)
        {
            var instanceCreation = BuildInstanceCreationInline(reg, hasFactory, groups);
            writer.WriteLine($"return {instanceCreation};");
            writer.Indentation--;
            writer.WriteLine("}");
            return;
        }

        // Complex case: has injection members or decorators
        var variableType = hasDecorators ? reg.ServiceType.Name : null;
        WriteInstanceCreationWithInjection(writer, "instance", reg, hasFactory, variableType, groups);

        if(hasDecorators)
        {
            writer.WriteLine();
            WriteDecoratorApplication(writer, "instance", reg, groups);
        }

        writer.WriteLine("return instance;");
        writer.Indentation--;
        writer.WriteLine("}");
    }
}
