using System.IO.Abstractions;
using ConsoleAppFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SourceGen.Ioc.Cli;
using ZLogger;

var app = ConsoleApp.Create();
app.ConfigureGlobalOptions((ref builder) =>
{
    var dryRun = builder.AddGlobalOption<bool>("-n|--dry-run", "Dry run.", false);
    var verbose = builder.AddGlobalOption<bool>("-v|--verbose", "Detailed logging message.", false);
    var log = builder.AddGlobalOption<string>("--log", "Log file path.", "");

    return new GlobalOptions(dryRun, verbose, log);
}).ConfigureServices((context, services) =>
{
    var globalOptions = (GlobalOptions)context.GlobalOptions!;
    services.AddSingleton(globalOptions);
    services.AddSingleton<IFileSystem, FileSystem>();
    services.AddSingleton<IEnvironmentProvider, EnvironmentProvider>();
    services.AddSingleton(new CliSchemaData(app.GetCliSchema()));

    services.AddLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddZLoggerConsole();

        if(!string.IsNullOrWhiteSpace(globalOptions.LogFile))
        {
            logging.AddZLoggerFile(globalOptions.LogFile);
        }

        if(globalOptions.Verbose)
        {
            logging.SetMinimumLevel(LogLevel.Trace);
        }
    });
});

app.Add<AddAttributeCommands>();
app.Add<CliSchemaCommand>();

app.Run(args);