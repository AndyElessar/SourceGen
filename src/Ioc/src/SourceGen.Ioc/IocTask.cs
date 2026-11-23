using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace SourceGen.Ioc;

/// <summary>
/// Lets <c>Add{Name}</c> register the task of an asynchronous service by service type and key. It does not dispose anything.
/// </summary>
/// <typeparam name="T">The service type.</typeparam>
/// <param name="task">The task of the service.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
[Experimental("SGIOCEXP001", Message = "This type is only for the code that SourceGen.Ioc generates. Do not use it directly.")]
public sealed class IocTask<T>(Task<T> task)
{
    /// <summary>Gets the task of the service.</summary>
    public Task<T> Task { get; } = task;
}
