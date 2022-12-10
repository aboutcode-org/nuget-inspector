using Microsoft.Build.Locator;

namespace NugetInspector;

internal static class Program
{
    public static void Main(string[] args)
    {
        RegisterMSBuild();
        var exitCode = 0;
        var options = ParseOptions(args: args);

        if (options.Success && options.Options is not null)
        {
            var execution = ExecuteInspectors(options: options.Options);
            if (execution.ExitCode != 0) exitCode = execution.ExitCode;
        }
        else
        {
            exitCode = options.ExitCode;
        }

        Environment.Exit(exitCode: exitCode);
    }

    private static void RegisterMSBuild()
    {
        try
        {
            if (Config.TRACE) Console.WriteLine(value: "Registering MSBuild defaults.");
            MSBuildLocator.RegisterDefaults();
        }
        catch (Exception e)
        {
            if (Config.TRACE)
            {
                Console.WriteLine(value: "Failed to register defaults.");
                Console.Write(value: e);
            }
        }
    }

    private static ExecutionResult ExecuteInspectors(ScanOptions options)
    {
        var anyFailed = false;
        try
        {
            var dispatch = new InputScanner();
            var searchService = new NugetApi(nugetApiFeedUrl: options.NugetApiFeedUrl,
                nugetConfig: options.NugetConfigPath);
            var inspectionResults = dispatch.Inspect(options: options, nugetService: searchService);
            if (inspectionResults != null)
                foreach (var result in inspectionResults)
                    try
                    {
                        if (result.ResultName != null && Config.TRACE)
                        {
                            Console.WriteLine(value: $"Scan: {result.ResultName}");
                        }

                        if (result.Status == Scan.ResultStatus.Success)
                        {
                            if (Config.TRACE) Console.WriteLine(value: "Scan Result: Success");
                            var writer = new OutputFormatJson(result: result);
                            writer.Write();
                            if (Config.TRACE)
                                Console.WriteLine(format: "Info file created at {0}", arg0: writer.OutputFilePath());
                        }
                        else
                        {
                            Console.WriteLine(value: "Scan Result: Error");
                            if (result.Exception != null)
                            {
                                if (Config.TRACE)
                                {
                                    Console.WriteLine(value: "Exception:");
                                    Console.WriteLine(value: result.Exception);
                                }

                                anyFailed = true;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        if (Config.TRACE)
                        {
                            Console.WriteLine(value: "Error processing inspection result.");
                            Console.WriteLine(value: e.Message);
                            Console.WriteLine(value: e.StackTrace);
                        }

                        anyFailed = true;
                    }
        }
        catch (Exception e)
        {
            if (Config.TRACE)
            {
                Console.WriteLine(value: "Error iterating inspection results.");
                Console.WriteLine(value: e.Message);
                Console.WriteLine(value: e.StackTrace);
            }

            anyFailed = true;
        }

        if (anyFailed)
            return ExecutionResult.Failed();
        return ExecutionResult.Succeeded();
    }

    private static ParsedOptions ParseOptions(string[] args)
    {
        try
        {
            var parsedOptions = CliOptions.ParseArguments(args: args);

            if (parsedOptions == null)
            {
                return ParsedOptions.Failed();
            }

            if (string.IsNullOrWhiteSpace(value: parsedOptions.ProjectFilePath))
                parsedOptions.ProjectFilePath = Directory.GetCurrentDirectory();

            ScanOptions options = new ScanOptions
            {
                OutputFilePath = parsedOptions.OutputFilePath,
                NugetApiFeedUrl = parsedOptions.NugetApiFeedUrl,
                NugetConfigPath = parsedOptions.NugetConfigPath,
                ProjectFilePath = parsedOptions.ProjectFilePath,
                Verbose = parsedOptions.Verbose
            };
            if (parsedOptions.Verbose)
            {
                Config.TRACE = true;
            }

            return ParsedOptions.Succeeded(options: options);
        }
        catch (Exception e)
        {
            if (Config.TRACE)
            {
                Console.WriteLine(value: "Failed to parse options.");
                Console.WriteLine(value: e.Message);
                Console.WriteLine(value: e.StackTrace);
            }

            return ParsedOptions.Failed();
        }
    }

    private class ParsedOptions
    {
        public int ExitCode;
        public ScanOptions Options = null!;
        public bool Success;

        public static ParsedOptions Failed(int exitCode = -1)
        {
            return new ParsedOptions
            {
                ExitCode = exitCode,
                Options = new ScanOptions(),
                Success = false
            };
        }

        public static ParsedOptions Succeeded(ScanOptions options)
        {
            return new ParsedOptions
            {
                Options = options,
                Success = true
            };
        }
    }

    private class ExecutionResult
    {
        public int ExitCode;
        public bool Success;

        public static ExecutionResult Failed(int exitCode = -1)
        {
            return new ExecutionResult
            {
                Success = false,
                ExitCode = exitCode
            };
        }

        public static ExecutionResult Succeeded()
        {
            return new ExecutionResult
            {
                Success = true
            };
        }
    }
}