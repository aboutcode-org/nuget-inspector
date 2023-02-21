using Microsoft.Build.Locator;

namespace NugetInspector;

internal static class Program
{
    public static void Main(string[] args)
    {
        RegisterMSBuild();
        var exitCode = 0;
        var options = ParseCliArgs(args: args);

        if (options.Success)
        {
            var execution = ExecuteInspector(options: options.Options);
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
            if (Config.TRACE) Console.WriteLine("Registering MSBuild defaults.");
            MSBuildLocator.RegisterDefaults();
        }
        catch (Exception e)
        {
            if (Config.TRACE)
            {
                Console.WriteLine("Failed to register defaults.");
                Console.Write(value: e);
            }
        }
    }

    private static ExecutionResult ExecuteInspector(Options options)
    {
        if (Config.TRACE)
        {
            Console.WriteLine("ExecuteInspector: options");
            options.Print();
        }

        var has_failed = false;
        try
        {
            var nuget_api_service = new NugetApi(
                nugetApiFeedUrl: options.NugetApiFeedUrl,
                nugetConfig: options.NugetConfigPath);

            var project_options = new ProjectScannerOptions(options: options);
            var inspector = new ProjectScanner(
                options: project_options,
                nuget_api_service: nuget_api_service);
            var scan = inspector.RunScan();
            inspector.FetchMetadata(scan_result: scan);

            try
            {
                if (scan.Status == ScanResult.ResultStatus.Success)
                {
                    if (Config.TRACE) Console.WriteLine("Scan Result: Success");
                    var writer = new OutputFormatJson(result: scan);
                    writer.Write();
                    if (Config.TRACE)
                        Console.WriteLine(format: $"JSON file created at {scan.Options!.OutputFilePath}");
                }
                else
                {
                    Console.WriteLine("Scan Result: Error");
                    if (scan.Exception != null)
                    {
                        if (Config.TRACE)
                        {
                            Console.WriteLine("Exception:");
                            Console.WriteLine(scan.Exception);
                        }

                        has_failed = true;
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

                has_failed = true;
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

            has_failed = true;
        }

        if (has_failed)
            return ExecutionResult.Failed();
        return ExecutionResult.Succeeded();
    }

    private static ParsedOptions ParseCliArgs(string[] args)
    {
        try
        {
            var options = Options.ParseArguments(args: args);

            if (options == null)
            {
                return ParsedOptions.Failed();
            }

            if (string.IsNullOrWhiteSpace(value: options.ProjectFilePath))
            {
                if (Config.TRACE)
                {
                    Console.WriteLine("Failed to parse options: missing ProjectFilePath");
                }

                return ParsedOptions.Failed();
            }

            if (options.Verbose)
            {
                Config.TRACE = true;
            }

            return ParsedOptions.Succeeded(options: options);
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
        public Options Options = null!;
        public bool Success;

        public static ParsedOptions Failed(int exitCode = -1)
        {
            return new ParsedOptions
            {
                ExitCode = exitCode,
                Options = new Options(),
                Success = false
            };
        }

        public static ParsedOptions Succeeded(Options options)
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