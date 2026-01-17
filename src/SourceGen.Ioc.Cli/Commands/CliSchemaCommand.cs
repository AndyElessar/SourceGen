using System.Text.Json;

namespace SourceGen.Ioc.Cli.Commands;

internal sealed class CliSchemaCommand(
    CliSchemaData cliSchemaData,
    GlobalOptions globalOptions,
    IFileSystem fileSystem,
    IEnvironmentProvider environmentProvider)
{
    private readonly CliSchemaData cliSchemaData = cliSchemaData;
    private readonly GlobalOptions globalOptions = globalOptions;
    private readonly IFileSystem fileSystem = fileSystem;
    private readonly IEnvironmentProvider environmentProvider = environmentProvider;

    /// <summary>
    /// Print CLI schema in JSON format.
    /// </summary>
    /// <param name="command">-c, Command name.</param>
    /// <param name="target">-t, Target file/folder to write cli schema.</param>
    [Command("cli-schema")]
    public void PrintSchema(string? command = null, string? target = null)
    {
        string json = string.Empty;
        if(string.IsNullOrWhiteSpace(command))
        {
            json = JsonSerializer.Serialize(cliSchemaData.CliSchema, CliSchemaJsonSerializerContext.Default.CommandHelpDefinitionArray);
        }
        else
        {
            var matchedCommand = cliSchemaData.CliSchema.FirstOrDefault(c => c.CommandName.Equals(command, StringComparison.OrdinalIgnoreCase));
            json = JsonSerializer.Serialize(matchedCommand, CliSchemaJsonSerializerContext.Default.CommandHelpDefinition);
        }

        Console.WriteLine(json);

        if(!globalOptions.DryRun && !string.IsNullOrWhiteSpace(target))
        {
            var targetPath = GetTargetFilePath(target);
            fileSystem.File.WriteAllText(targetPath, json);
        }
    }

    private string GetTargetFilePath(string target)
    {
        // 如果是相對路徑，則根據 CurrentDirectory 組合出完整路徑
        var fullPath = fileSystem.Path.IsPathRooted(target)
            ? target
            : fileSystem.Path.Combine(environmentProvider.CurrentDirectory, target);

        // 如果是資料夾，則建立檔案 SourceGen.Ioc.Cli-schema.json
        if(fileSystem.Directory.Exists(fullPath))
        {
            return fileSystem.Path.Combine(fullPath, "SourceGen.Ioc.Cli-schema.json");
        }

        return fullPath;
    }
}
