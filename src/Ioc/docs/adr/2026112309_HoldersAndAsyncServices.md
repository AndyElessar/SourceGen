---
status: accepted
date: 2026-11-23
---

# Holders and asynchronous services

## Context and problem statement

MS.DI addresses a registration only by service type and key. One closed definition can have several service types, decorator chains, and wrappers, and all of them must share one instance. The service collection extension therefore registers hidden objects under private keys.

MS.DI disposes only the objects that it tracks: those that a factory delegate returns. An object that generated code creates inside a factory delegate and does not return is never disposed, unless something that MS.DI tracks disposes it.

An earlier draft had two types: `IocHolder<T>` for synchronous services and `AsyncInitHolder<T>` for asynchronous services. Each had its own disposal rules, and the rules for exceptions differed.

Creation can also fail after `new`: an injection method throws, or the constructor of a later decorator layer throws. The objects that already exist then have no owner.

## Decision outcome

1. **`IocHolder<T>` is the only type that disposes.** The static class `IocHolder` creates it in three ways:
   - `Own(value)`: disposal disposes `value`.
   - `Chain(value, inner...)`: disposal does not dispose `value`. It disposes the `inner` objects in order: the middle layers from the outside in, and for a Transient chain the implementation instance last.
   - `OwnTask(task)`: holds the task of an asynchronous service. Disposal acts on the result of the task.
2. **A holder throws its disposal exceptions.** It follows the container rules: continue with the other objects, then throw one exception as it is or an `AggregateException`. A synchronous `Dispose` that meets an object with only `IAsyncDisposable` adds an `InvalidOperationException`, as MS.DI does for the objects that it tracks.
3. **`Task<T>` is not registered as a service.** `IocTask<S>` is a stand-in that is not disposable. A dependency site that asks for `Task<S>` resolves `IocTask<S>` and reads its `Task`. A hidden `IocHolder<Task<T>>` owns the task.
4. **Disposal of `OwnTask` depends on the state of the task.**

   | State | `DisposeAsync` | `Dispose` |
   | --- | --- | --- |
   | Completed successfully | Dispose the result | Dispose the result |
   | Faulted or canceled | Nothing | Nothing |
   | Not completed | Wait for it, ignore its exception, dispose the result on success | Do not wait. Attach a continuation that disposes the result after success. |

   The continuation guarantees only that each object is disposed exactly once. It does not guarantee the order, and it does not guarantee that dependencies used during creation are still valid. Its exceptions go to the dispose-exception callback. Use `DisposeAsync` when asynchronous services exist.
5. **An asynchronous decorator chain has an `OwnTask` owner too.** Its type is `IocHolder<Task<IocHolder<S>>>`. The inner holder is a `Chain` whose `inner` list also contains the outermost layer, because MS.DI never sees that layer.
6. **A generated asynchronous method resolves all dependencies before its first `await`.** The chain-building method resolves the owner of the instance last, after all dependencies of the decorators.
7. **The holder of a Transient decorator chain disposes the implementation instance.** A Transient definition has no holder for the instance, and no factory delegate returns the implementation instance.
8. **When creation fails after `new`, the generated code disposes what it created and throws the original exception again.**
   - A failed chain disposes its created layers from the outside in. It disposes the implementation instance too only for Transient. For Singleton and Scoped the instance is already in its holder or in the container.
   - The generator emits this handling only when the object is disposable and a step that can fail follows `new`.
   - A synchronous method cannot wait. It calls `Dispose` when the object has `IDisposable`. When the object has only `IAsyncDisposable`, it calls `DisposeAsync` and does not wait.
   - An exception from this disposal never replaces the original exception. It goes to the dispose-exception callback. With several objects, each is disposed in turn and each exception goes to the callback separately.
   - The container stores nothing from a failed creation. It stores the layers of a decorator chain only after the complete chain exists.

## Rejected options

- **Two holder types (`IocHolder`, `AsyncInitHolder`).** Two sets of disposal rules drifted apart during the review. The rule for exceptions of the asynchronous holder changed three times.
- **Send all holder exceptions to the callback.** The callback is `null` by default, so a disposal failure would be silent. MS.DI itself throws.
- **Register the holder under the real service type.** `GetKeyedServices<S>(KeyedService.AnyKey)` would return the hidden registrations.
- **Register `Task<T>`.** MS.DI disposes the `Task` that a factory delegate returns. `Task.Dispose` throws for a task that is not completed, and MS.DI then stops and leaves the other services undisposed.
- **Do not dispose the result when the task is not completed at a synchronous `Dispose`.** The object would leak when the task completes later.
- **Block in a synchronous `Dispose` until the task completes.** The task can need the calling thread, which gives a deadlock.
- **Leave the Transient implementation instance to MS.DI.** MS.DI does not track it (see Evidence).
- **Do nothing when creation fails.** The created objects leak (see Evidence).

## Evidence

Tested with `Microsoft.Extensions.DependencyInjection` 10.0.15 on .NET 10.0.15, JIT and NativeAOT. NativeAOT publish had no warnings. The generated code and the library types were hand-written stand-ins.

Decorator chains on MS.DI:

- For all three lifetimes, the disposal order is the outermost layer, the middle layers, then the implementation instance. Each object is disposed exactly once.
- A Transient chain is independent for each resolution.
- Without the rule of decision 7, `Dispose` of the Transient implementation instance is never called.
- Keyed definitions behave the same way.
- Holders do not appear in the result of `GetServices<S>()` or `GetKeyedServices<S>(KeyedService.AnyKey)`.
- A Singleton or Scoped instance with several service types is disposed more than once: once by its holder and once for each service type registration that returns it. The constitution lists this as a documented difference.

Asynchronous services on MS.DI (160 checks):

- When a dependency is resolved after an `await`, MS.DI tracks it later and disposes it earlier than the object that uses it. This happens when the task of the instance is not completed at the time of resolution.
- When the chain-building method resolves the owner of the instance first, the dependencies of the decorators are disposed between the chain and the implementation instance.
- A synchronous `Dispose` with a task that is not completed: each object is disposed exactly once after the task completes, but the implementation instance can be disposed before a decorator, and a decorator created after the scope disposal can get a disposed dependency.
- Without the handling of decision 8, a failed asynchronous injection method or a failed decorator constructor leaks the created objects. With it, nothing leaks in these three cases: the asynchronous injection method of the instance fails, a decorator constructor throws, and the asynchronous injection method of a decorator fails.
- One check passed in an earlier run only because of timing. Three creation methods awaited the same task, and their continuations ran in an order that is not fixed. The probe now releases them one at a time. After that change, six NativeAOT runs passed all 160 checks.

Synchronous creation failure (67 checks):

- Without the handling, the instance is disposed 0 times after its injection method throws. After the constructor of the third layer throws, the first two layers are disposed 0 times. For Transient, the implementation instance is also disposed 0 times.
- With the handling, each object is disposed exactly once, also after the later disposal of the scope and the provider.
- The caller gets the same exception object that user code threw. MS.DI does not wrap it.
- After a failed chain, the Singleton or Scoped instance is still in its holder. A retry uses the same instance.
- MS.DI does not cache a failure. Each retry calls the factory delegate again. The dependencies that a failed attempt resolved stay tracked until the scope or the provider is disposed. For a Singleton that fails many times, they accumulate in the root. The handling does not change this behavior of MS.DI.
- A `DisposeAsync` that completes later is started, is not awaited, and does run. An exception that it throws, synchronously or later, reaches the callback exactly once.
- A hand-written minimal container (scoped field with double-checked `System.Threading.Lock`, singletons with one lock each) leaves the field empty and the lock free after a failure. A retry succeeds.
- Two threads that resolve for the first time while the first creation fails: 500 runs for each of Scoped, Singleton, and a Scoped chain. Exactly one thread fails each time, and nothing leaks or is disposed twice.

Not tested:

- In the container: the `CompareExchange` and `None` strategies, creation that runs at the same time as disposal, and asynchronous creation.
- A failed assignment of a field or property. The probes used an injection method and a decorator constructor, which are at the same position in the generated code.
- MS.DI 11.0.0.
