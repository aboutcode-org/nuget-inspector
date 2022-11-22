using Microsoft.Build.Locator;

namespace NugetInspector;

internal static class Program
{
    public static void Main(string[] args)
    {
        RegisterMSBuild();
        var exitCode = 0;
        var options = ParseOptions(args);

        if (options.Success && options.Options is not null)
        {
            var execution = ExecuteInspectors(options.Options);
            if (execution.ExitCode != 0) exitCode = execution.ExitCode;
        }
        else
        {
            exitCode = options.ExitCode;
        }

        Environment.Exit(exitCode);
    }

    private static void RegisterMSBuild()
    {
        try
        {
            if (Config.TRACE) Console.WriteLine("Registering MSBuild defaults.");
            MSBuildLocator.RegisterDefaults();
        }
        catch (Exception e)
        {
            if (Config.TRACE)
            {
                Console.WriteLine("Failed to register defaults.");
                Console.Write(e);
            }
        }
    }

    private static ExecutionResult ExecuteInspectors(ScanOptions options)
    {
        var anyFailed = false;
        try
        {
            var dispatch = new InputScanner();
            var searchService = new NugetApi(options.NugetApiFeedUrl, options.NugetConfigPath);
            var inspectionResults = dispatch.Inspect(options, searchService);
            if (inspectionResults != null)
                foreach (var result in inspectionResults)
                    try
                    {
                        if (result.ResultName != null && Config.TRACE)
                        {
                            Console.WriteLine("Scan: " + result.ResultName);
                        }

                        if (result.Status == Scan.ResultStatus.Success)
                        {
                            if (Config.TRACE) Console.WriteLine("Scan Result: Success");
                            var writer = new OutputFormatJson(result);
                            writer.Write();
                            if (Config.TRACE) Console.WriteLine("Info file created at {0}", writer.OutputFilePath());
                        }
                        else
                        {
                            Console.WriteLine("Scan Result: Error");
                            if (result.Exception != null)
                            {
                                if (Config.TRACE)
                                {
                                    Console.WriteLine("Exception:");
                                    Console.WriteLine(result.Exception);
                                }

                                anyFailed = true;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        if (Config.TRACE)
                        {
                            Console.WriteLine("Error processing inspection result.");
                            Console.WriteLine(e.Message);
                            Console.WriteLine(e.StackTrace);
                        }

                        anyFailed = true;
                    }
        }
        catch (Exception e)
        {
            if (Config.TRACE)
            {
                Console.WriteLine("Error iterating inspection results.");
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
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
            var parsedOptions = CliOptions.ParseArguments(args);

            if (parsedOptions == null)
            {
                return ParsedOptions.Failed();
            }

            if (string.IsNullOrWhiteSpace(parsedOptions.ProjectFilePath))
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

            return ParsedOptions.Succeeded(options);
        }
        catch (Exception e)
        {
            if (Config.TRACE)
            {
                Console.WriteLine("Failed to parse options.");
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
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