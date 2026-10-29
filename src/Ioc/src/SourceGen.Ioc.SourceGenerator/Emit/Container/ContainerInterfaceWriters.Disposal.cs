namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Writes Disposal implementation (IDisposable and IAsyncDisposable).
    /// </summary>
    private static void WriteDisposalImplementation(
        SourceWriter writer,
        ContainerModel container,
        ContainerRegistrationGroups groups,
        bool ownsFallbackProvider)
    {
        writer.WriteLine("#region Disposal");
        writer.WriteLine();

        // IDisposable.Dispose
        writer.WriteLine("public void Dispose()");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(global::System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;");
        writer.WriteLine();
        WriteDisposalBody(writer, container, groups, ownsFallbackProvider, isAsync: false);
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // IAsyncDisposable.DisposeAsync
        writer.WriteLine("public async global::System.Threading.Tasks.ValueTask DisposeAsync()");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(global::System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;");
        writer.WriteLine();
        WriteDisposalBody(writer, container, groups, ownsFallbackProvider, isAsync: true);
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        var hasAsyncInitServices = groups.SingletonEntries.Any(static e => e is AsyncContainerEntry)
            || groups.ScopedEntries.Any(static e => e is AsyncContainerEntry);
        WriteDisposalHelperMethods(writer, hasAsyncInitServices);

        writer.WriteLine("#endregion");
    }

    /// <summary>
    /// Writes the disposal body for both sync and async disposal methods.
    /// </summary>
    private static void WriteDisposalBody(
        SourceWriter writer,
        ContainerModel container,
        ContainerRegistrationGroups groups,
        bool ownsFallbackProvider,
        bool isAsync)
    {
        // Scoped services first, in both scopes and the root (the root caches scoped services resolved from it)
        WriteServiceDisposalCalls(writer, groups.ScopedEntries, isAsync);

        writer.WriteLine("if(!_isRootScope)");
        writer.WriteLine("{");
        writer.Indentation++;

        WriteModuleDisposalCalls(writer, container.ImportedModules, isAsync);

        // Fallback scope last: container services may depend on its services
        if(container.IntegrateServiceProvider)
        {
            WriteDisposeCall(writer, "_fallbackScope", isAsync);
        }

        writer.WriteLine("return;");

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // Root scope disposal
        WriteServiceDisposalCalls(writer, groups.SingletonEntries, isAsync);
        WriteModuleDisposalCalls(writer, container.ImportedModules, isAsync);

        if(ownsFallbackProvider)
        {
            WriteDisposeCall(writer, "_ownedFallbackProvider", isAsync);
        }
    }

    /// <summary>
    /// Writes disposal calls for services in reverse registration order.
    /// </summary>
    private static void WriteServiceDisposalCalls(
        SourceWriter writer,
        ImmutableEquatableArray<ContainerEntry> services,
        bool isAsync)
    {
        for(var i = services.Length - 1; i >= 0; i--)
        {
            services[i].WriteDisposal(writer, isAsync);
        }
    }

    /// <summary>
    /// Writes disposal calls for imported modules.
    /// </summary>
    private static void WriteModuleDisposalCalls(
        SourceWriter writer,
        ImmutableEquatableArray<TypeData> modules,
        bool isAsync)
    {
        foreach(var module in modules)
        {
            var fieldName = GetModuleFieldName(module.Name);
            if(isAsync)
            {
                writer.WriteLine($"await {fieldName}.DisposeAsync().ConfigureAwait(false);");
            }
            else
            {
                writer.WriteLine($"{fieldName}.Dispose();");
            }
        }
    }

    /// <summary>
    /// Writes a sync or async disposal statement for a service, scope, or provider field.
    /// </summary>
    private static void WriteDisposeCall(SourceWriter writer, string target, bool isAsync)
    {
        if(isAsync)
        {
            writer.WriteLine($"await DisposeServiceAsync({target}).ConfigureAwait(false);");
        }
        else
        {
            writer.WriteLine($"DisposeService({target});");
        }
    }

    /// <summary>
    /// Writes the static helper methods for disposal.
    /// </summary>
    private static void WriteDisposalHelperMethods(SourceWriter writer, bool hasAsyncInitServices)
    {
        // Helper method to throw ObjectDisposedException if disposed
        writer.WriteLine("private void ThrowIfDisposed()");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("global::System.ObjectDisposedException.ThrowIf(_disposed != 0, GetType());");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // Helper method for async disposal
        writer.WriteLine("private static async global::System.Threading.Tasks.ValueTask DisposeServiceAsync(object? service)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(service is global::System.IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);");
        writer.WriteLine("else if(service is global::System.IDisposable disposable) disposable.Dispose();");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // Helper method for sync disposal; like Microsoft.Extensions.DependencyInjection, an
        // IAsyncDisposable-only service cannot be disposed synchronously
        writer.WriteLine("private static void DisposeService(object? service)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(service is global::System.IDisposable disposable) disposable.Dispose();");
        writer.WriteLine("else if(service is global::System.IAsyncDisposable) throw new global::System.InvalidOperationException($\"'{service.GetType()}' type only implements IAsyncDisposable. Use DisposeAsync to dispose the container.\");");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        if(hasAsyncInitServices)
        {
            // Overload for async-init services stored as Task<T>?
            writer.WriteLine("private static async global::System.Threading.Tasks.ValueTask DisposeServiceAsync<T>(global::System.Threading.Tasks.Task<T>? task)");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("if(task is { IsCompletedSuccessfully: true })");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("try");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("await DisposeServiceAsync(await task.ConfigureAwait(false)).ConfigureAwait(false);");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine("catch(global::System.Exception ex)");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("global::SourceGen.Ioc.IocContainerGlobalOptions.OnDisposeException?.Invoke(ex);");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine();

            writer.WriteLine("private static void DisposeService<T>(global::System.Threading.Tasks.Task<T>? task)");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("if(task is { IsCompletedSuccessfully: true })");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("try");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("DisposeService(task.ConfigureAwait(false).GetAwaiter().GetResult());");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine("catch(global::System.Exception ex)");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("global::SourceGen.Ioc.IocContainerGlobalOptions.OnDisposeException?.Invoke(ex);");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine();
        }
    }
}
