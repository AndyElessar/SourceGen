# Contributing to SourceGen

Thank you for your interest in contributing to SourceGen! 🎉

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A code editor (VS Code, Visual Studio, or Rider recommended)

### Building the Project

```bash
# Clone the repository
git clone https://github.com/AndyElessar/SourceGen.git
cd SourceGen

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run tests
dotnet test
```

## How to Contribute

### Reporting Bugs

1. Check if the issue already exists in [GitHub Issues](https://github.com/AndyElessar/SourceGen/issues)
2. If not, create a new issue with:
   - A clear and descriptive title
   - Steps to reproduce the issue
   - Expected vs actual behavior
   - Your environment (OS, .NET version)
   - Sample code if applicable

### Suggesting Features

1. Check existing [issues](https://github.com/AndyElessar/SourceGen/issues) for similar requests
2. Open a new issue with the `enhancement` label
3. Describe the feature and its use case

### Pull Requests

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes following our coding guidelines
4. Add or update tests as needed
5. Ensure all tests pass (`dotnet test`)
6. Commit your changes (`git commit -m 'Add amazing feature'`)
7. Push to your branch (`git push origin feature/amazing-feature`)
8. Open a Pull Request

## Coding Guidelines

### C# Style

- Follow .NET naming conventions
- Use C# 14 extensions members where applicable
- Use file-scoped namespaces
- Use nullable reference types (`#nullable enable`)
- Add XML documentation for public APIs

### Source Generator Guidelines

- Implement `IIncrementalGenerator`
- Use static lambdas to prevent captures
- Use value equality data models (`readonly record struct` or `sealed record class`)
- Use `PolyType.Roslyn.ImmutableEquatableArray<T>`, `PolyType.Roslyn.ImmutableEquatableDictionary<TKey, TValue>`, `PolyType.Roslyn.ImmutableEquatableSet<T>` for collections in data models
- Always check `CancellationToken`
- Read Andrew Lock's great blogs!  
  > [Creating a source generator](https://andrewlock.net/series/creating-a-source-generator/)

### Testing

- Write tests using `TUnit`
- Use `Verify` for snapshot testing
- Test both positive and negative cases
- Check [TUnit Best Practices](https://tunit.dev/docs/guides/best-practices)

## Code of Conduct

Please be respectful and constructive in all interactions. We welcome contributors of all skill levels.

## Questions?

Feel free to open an issue for any questions about contributing.

Thank you for contributing!
