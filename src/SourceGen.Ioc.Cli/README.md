# SourceGen.Ioc.Cli

A command-line tool that helps you quickly add `[IoCRegister]` attributes to existing classes in your project.

## Installation

```bash
dotnet tool install -g SourceGen.Ioc.Cli
```

## Usage

### Basic Command

```bash
sourcegen-ioc [options]
```

### Options

| Option | Description | Default |
|--------|-------------|---------|
| `-t`, `--target` | Target directory or file | Current directory |
| `-f`, `--file-pattern` | File pattern to filter files | `*.cs` |
| `-s`, `--search-sub-directories` | Search subdirectories | `false` |
| `-cn`, `--class-name-regex` | Regex pattern to match class names | `null` |
| `--full-regex` | Full regex pattern to match file content | `null` |
| `--attribute-name` | Name of the attribute to add | `IocRegister` |
| `-m`, `--max-apply` | Maximum matches to apply (0 = unlimited) | `0` |
| `-n`, `--dry-run` | Preview changes without modifying files | `false` |
| `-v`, `--verbose` | Enable detailed logging | `false` |
| `--log` | Log file path | `""` |

## Examples

### Add Attributes to Service Classes

```bash
# Preview changes first (dry-run)
sourcegen-ioc -t ./src/MyProject -s -cn ".*Service" -n -v

# Apply changes
sourcegen-ioc -t ./src/MyProject -s -cn ".*Service"
```

### Add Attributes to Handler Classes

```bash
sourcegen-ioc -t ./src/Handlers -s -cn ".*Handler"
```

### Add Attributes to Repository Classes

```bash
sourcegen-ioc -t ./src/Repositories -s -cn ".*Repository"
```

### Limit Number of Matches

```bash
sourcegen-ioc -cn ".*Service" -m 10
```

### Use Full Regex Pattern

```bash
sourcegen-ioc --full-regex "public\s+class\s+\w+Controller"
```

## Class Name Regex

When using `-cn` or `--class-name-regex`, the tool matches non-static `public` or `internal` classes:

```regex
(public|internal)\s+(?!static\s+).*class\s+(classNameRegex)(?=\s|:|$)
```

## CLI Schema Command

Output CLI schema in JSON format (useful for LLM/AI integration):

```bash
# Output schema for all commands
sourcegen-ioc cli-schema

# Write schema to file
sourcegen-ioc cli-schema -t ./cli-schema.json
```

## Workflow

1. **Preview** - Use `--dry-run` and `--verbose` to see which files will be modified
2. **Review** - Check the output to ensure correct files are targeted
3. **Apply** - Run without `--dry-run` to apply changes
4. **Build** - Build your project to generate DI registration code

## Related Packages

- **SourceGen.Ioc** - The source generator package that processes `[IoCRegister]` attributes

## Documentation

For complete documentation, see the [GitHub repository](https://github.com/AndyElessar/SourceGen).

## License

MIT License
