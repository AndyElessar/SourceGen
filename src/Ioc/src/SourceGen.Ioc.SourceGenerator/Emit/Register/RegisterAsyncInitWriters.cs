using static SourceGen.Ioc.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private const string AsyncInitHolderGlobalTypeName = "global::SourceGen.Ioc.AsyncInitHolder";

    /// <summary>
    /// Opens the registration of an async-init instance of <paramref name="typeName"/>: the lambda registering its
    /// <c>AsyncInitHolder&lt;T, TImplementation&gt;</c> with the service's lifetime and key, and the
    /// <c>async Task&lt;T&gt; Init()</c> local function whose body the caller writes, ending with <c>return</c> of the instance.
    /// </summary>
    /// <remarks>
    /// MS.DI disposes the object a factory returns, so the holder, not the <c>Task&lt;T&gt;</c>, is the tracked instance: it
    /// disposes the task's result with the scope or provider that owns the lifetime. <see cref="WriteAsyncInitHolderClose"/>
    /// registers <c>Task&lt;T&gt;</c> as the holder's task. The implementation type in the holder type gives each
    /// registration of the same <c>Task&lt;T&gt;</c> and key a holder of its own.
    /// </remarks>
    private static void WriteAsyncInitHolderOpen(
        SourceWriter writer,
        string lifetime,
        string typeName,
        string implementationTypeName,
        string? registrationKey)
    {
        writer.WriteServiceLambdaOpen(lifetime, $"{AsyncInitHolderGlobalTypeName}<{typeName}, {implementationTypeName}>", registrationKey);

        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine($"async global::System.Threading.Tasks.Task<{typeName}> Init()");
        writer.WriteLine("{");
        writer.Indentation++;
    }

    /// <summary>
    /// Closes the registration opened by <see cref="WriteAsyncInitHolderOpen"/>: returns the holder of the <c>Init()</c>
    /// task, and registers <c>Task&lt;T&gt;</c> with the same lifetime and key as the holder's task.
    /// </summary>
    private static void WriteAsyncInitHolderClose(
        SourceWriter writer,
        string lifetime,
        string typeName,
        string implementationTypeName,
        string? registrationKey)
    {
        writer.Indentation--;
        writer.WriteLine("}");

        writer.WriteLine($"return new {AsyncInitHolderGlobalTypeName}<{typeName}, {implementationTypeName}>(Init());");

        writer.Indentation--;
        writer.WriteLine("});");

        if(registrationKey is not null)
        {
            writer.WriteLine($"services.AddKeyed{lifetime}<global::System.Threading.Tasks.Task<{typeName}>>({registrationKey}, ({IServiceProviderGlobalTypeName} sp, object? key) => sp.{GetRequiredKeyedService}<{AsyncInitHolderGlobalTypeName}<{typeName}, {implementationTypeName}>>(key).Task);");
        }
        else
        {
            writer.WriteLine($"services.Add{lifetime}<global::System.Threading.Tasks.Task<{typeName}>>(({IServiceProviderGlobalTypeName} sp) => sp.{GetRequiredService}<{AsyncInitHolderGlobalTypeName}<{typeName}, {implementationTypeName}>>().Task);");
        }
    }
}
