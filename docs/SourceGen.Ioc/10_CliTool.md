# CLI Tool

`SourceGen.Ioc.Cli` is a command-line tool that helps you quickly add `[IoCRegister]` attributes to existing classes in your project.

## Installation

```bash
dotnet tool install -g SourceGen.Ioc.Cli
```

## Commands

### Default Command - Add Attribute

Add `[IoCRegister]` attribute to classes matching the specified criteria.

```bash
sourcegen-ioc [options]
```

#### Options

| Option | Description | Default |
|--------|-------------|---------|
| `-t`, `--target` | Target directory or file, defaults to current directory | `null` (current directory) |
| `-f`, `--file-pattern` | File pattern to filter files | `*.cs` |
| `-s`, `--search-sub-directories` | Whether to search sub directories | `false` |
| `-cn`, `--class-name-regex` | Regex pattern to match class names | `null` |
| `--full-regex` | Full regex pattern to match file content | `null` |
| `--attribute-name` | Name of the attribute to add | `IocRegister` |
| `-m`, `--max-apply` | How many matches should apply, 0 means unlimited | `0` |
| `-n`, `--dry-run` | Dry run, does not modify files | `false` |
| `-v`, `--verbose` | Detailed logging message | `false` |
| `--log` | Log file path | `""` |

#### Class Name Regex

When using `-cn` or `--class-name-regex` option, the actual regex used is:

```
(public|internal)\s+(?!static\s+).*class\s+(classNameRegex)(?=\s|:|$)
```

This matches non-static `public` or `internal` classes.

#### Examples

```bash
# Search all .cs files in current directory and add attributes
sourcegen-ioc

# Search specified directory including subdirectories
sourcegen-ioc -t ./src -s

# Match only classes ending with Service
sourcegen-ioc -cn ".*Service"

# Match only classes ending with Handler, including subdirectories
sourcegen-ioc -cn ".*Handler" -s

# Dry run to see which files would be modified
sourcegen-ioc -cn ".*Repository" -n -v

# Limit to 10 matches maximum
sourcegen-ioc -cn ".*Service" -m 10

# Use full regex pattern
sourcegen-ioc --full-regex "public\s+class\s+\w+Controller"
```

### cli-schema Command

Output CLI schema in JSON format for LLM AI.

```bash
sourcegen-ioc cli-schema [options]
```

#### Options

| Option | Description | Default |
|--------|-------------|---------|
| `-c`, `--command` | Command name | `null` (all) |
| `-t`, `--target` | Target file/folder to write CLI schema | `null` (stdout) |
| `-n`, `--dry-run` | Dry run | `false` |
| `-v`, `--verbose` | Detailed logging message | `false` |
| `--log` | Log file path | `""` |

#### Examples

```bash
# Output schema for all commands
sourcegen-ioc cli-schema

# Output schema for specific command
sourcegen-ioc cli-schema -c ""

# Write schema to file
sourcegen-ioc cli-schema -t ./cli-schema.json
```

## Use Cases

### Add DI Registration to Existing Project

When you have an existing project and want to batch add `[IoCRegister]` attributes:

```bash
# 1. Use dry-run first to preview which files will be affected
sourcegen-ioc -t ./src/MyProject -s -cn ".*Service" -n -v

# 2. Execute after confirming the changes
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

---

[← Back to Overview](01_Overview.md)
