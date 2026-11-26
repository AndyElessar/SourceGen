---
status: accepted
date: 2026-11-27
---

# Disposal does not observe the failure of an asynchronous creation

## Context and problem statement

The task of an asynchronous service can fail. At disposal, the container and the `OwnTask` holder of the service collection extension skip a failed task, because no instance exists (see [Container output: Asynchronous closed definitions](../../spec/Container.Output.spec.md#asynchronous-closed-definitions-1) and [Service collection extension output: Disposal of `OwnTask`](../../spec/ServiceCollection.Output.spec.md#disposal-of-owntask)).

The specs did not say whether disposal observes the exception of the failed task. When no caller awaits the task, an exception that nobody observes reaches `TaskScheduler.UnobservedTaskException` when the task is collected. The two paths of disposal were not the same:

- The task failed before disposal. Disposal skips it and does not read its exception.
- The task is not complete at disposal. Disposal waits for it (`DisposeAsync`, or the continuation of `Dispose`) and ignores its exception. The wait observes the exception.

This ADR records which side observes the exception.

## Decision outcome

Disposal never observes the exception of a failed task on purpose. The caller that got the `Task<S>` awaits it and receives the exception. This is the usual rule of .NET: the code that gets a task is responsible for it.

- When no caller awaits a task that failed before disposal, the exception reaches `TaskScheduler.UnobservedTaskException`. The library does not hide it.
- When disposal waits for a task that is not complete, the wait observes the exception as a side effect. Disposal does not report it.
- The dispose-exception callback does not receive the failure of a creation. It is only for the failures of disposal.

### Consequences

- Good: a failure of creation that no code awaited still gives a signal when the task failed before disposal.
- Good: the dispose-exception callback keeps one meaning.
- Bad: the two paths of disposal differ. A failure that no code awaits and that occurs after disposal started gives no signal.

## Rejected options

- **Observe the exception in both paths.** Disposal reads the exception of each failed task that it skips. The two paths are then the same, but a failure of creation that no code awaited gives no signal at all.
- **Give the failure of creation to the dispose-exception callback.** This changes the meaning of the callback. A failure of creation that a caller already received through `await` also reaches the callback a second time.

## Evidence

Tested with SourceGen.Ioc after commit `7c9f6d0`, .NET SDK 11.0.100, on net10.0 and net11.0. The tests are the unit tests of `IocHolderTests`. To see unobserved exceptions, a temporary hook of the test session wrote each `TaskScheduler.UnobservedTaskException` to a file, and a hook after each test called `GC.Collect` and `GC.WaitForPendingFinalizers`. The hooks are not part of the test project.

| Case | Result |
| --- | --- |
| `IocHolder.OwnTask` with a task that already failed, then `Dispose`, and the test does not read the exception | One unobserved `FormatException` in the test session (the cause of the message that each run of the tests showed before commit `7c9f6d0`). |
| The same, and the test reads `Task.Exception` | No unobserved exception. |
| `Dispose` on a task that is not complete, and the task fails later (`OwnTask_DisposeOnTaskThatFaultsLater_ReportsNothing`) | The callback receives nothing. The hooks recorded no unobserved exception for the complete test session. |

Not tested: the generated container, because it does not yet generate asynchronous closed definitions.
