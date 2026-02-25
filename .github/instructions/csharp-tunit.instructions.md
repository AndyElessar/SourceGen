---
description: "Use when writing or reviewing TUnit tests. Covers TUnit assertions, data-driven tests, lifecycle hooks, and test filtering."
---

# TUnit Best Practices

## Running Tests

TUnit uses `dotnet run` (NOT `dotnet test`):

```powershell
# Correct: Use dotnet run with --treenode-filter
dotnet run --project path/to/TestProject.csproj -- --treenode-filter "/*/*/TestClass/*"

# WRONG: Do NOT use dotnet test with --filter
```

## Test Structure

- Use `[Test]` attribute for test methods
- Name tests: `MethodName_Scenario_ExpectedBehavior`
- Lifecycle hooks: `[Before(Test)]` / `[After(Test)]` for setup/teardown
- Class-level: `[Before(Class)]` / `[After(Class)]`
- Assembly-level: `[Before(Assembly)]` / `[After(Assembly)]`

## Assertions

All assertions are async and must be awaited:

```csharp
await Assert.That(value).IsEqualTo(expected);
await Assert.That(value).IsTrue();
await Assert.That(collection).Contains(item);
await Assert.That(action).ThrowsAsync<TException>();

// Chaining
await Assert.That(value).IsNotNull().And.IsEqualTo(expected);
await Assert.That(value).IsEqualTo(1).Or.IsEqualTo(2);
```

## Data-Driven Tests

- `[Arguments(...)]` for inline test data
- `[MethodData(nameof(...))]` for method-based data
- `[ClassData<T>]` for class-based data
- Multiple `[Arguments]` can be applied to the same test

## Test Filtering

Pattern: `/Assembly/Namespace/Class/Method[Property=Value]`

```powershell
# All tests in a class
dotnet run --project Test.csproj -- --treenode-filter "/*/*/MyTestClass/*"

# Specific method
dotnet run --project Test.csproj -- --treenode-filter "/*/*/MyTestClass/MyMethod"

# By category
dotnet run --project Test.csproj -- --treenode-filter "/*/*/*/*[Category=Integration]"

# Multiple filters (OR)
dotnet run --project Test.csproj -- --treenode-filter "/*/*/ClassA/*|/*/*/ClassB/*"
```

## Parallel Execution

- Tests run in parallel by default
- `[NotInParallel]` to disable for specific tests
- `[ParallelLimit<T>]` to control concurrency

## References

- [TUnit Best Practices](https://tunit.dev/docs/guides/best-practices)
- [TUnit Troubleshooting & FAQ](https://tunit.dev/docs/troubleshooting)
