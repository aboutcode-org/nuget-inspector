using System.Diagnostics;
using Microsoft.Build.Locator;
using Newtonsoft.Json;
using NuGet.Frameworks;

namespace NugetInspector;

internal static class Program
{
    public static void Main(string[] args)
    {
        try
        {
            if (Config.TRACE) Console.WriteLine("Registering MSBuild defaults.");
            MSBuildLocator.RegisterDefaults();
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to register MSBuild defaults: {e}");
            Environment.Exit(exitCode: -1);
        }

        var exit_code = 0;
        var options = ParseCliArgs(args: args);

        if (options.Success)
        {
            var execution = ExecuteInspector(options: options.Options);
            if (execution.ExitCode != 0) exit_code = execution.ExitCode;
        }
        else
        {
            exit_code = options.ExitCode;
        }

        Environment.Exit(exitCode: exit_code);
    }

    /// <summary>
    /// Return True if there is a warning in the results.
    /// </summary>
    public static bool Has_warnings(OutputFormatJson output)
    {
        var has_top_level = output.scan_result.warnings.Any();
        if (has_top_level)
            return true;
        bool has_package_level =  output.scan_result.project_package.warnings.Any();
        if (has_package_level)
            return true;
        bool has_dep_level = false;
        foreach (var dep in output.scan_output.Dependencies)
        {
            if (dep.warnings.Any())
                has_dep_level = true;
            break;
        }
        return has_dep_level;
    }

    /// <summary>
    /// Return True if there is an error in the results.
    /// </summary>
    public static bool Has_errors(OutputFormatJson output)
    {
        var has_top_level = output.scan_result.errors.Any();
        if (has_top_level)
            return true;
        bool has_package_level =  output.scan_result.project_package.errors.Any();
        if (has_package_level)
            return true;
        bool has_dep_level = false;
        foreach (var dep in output.scan_output.Dependencies)
        {
            if (dep.errors.Any())
                has_dep_level = true;
            break;
        }
        return has_dep_level;
    }

    private static ExecutionResult ExecuteInspector(Options options)
    {
        if (Config.TRACE)
        {
            Console.WriteLine("\nnuget-inspector options:");
            options.Print(indent: 4);
        }

        try
        {
            var project_options = new ProjectScannerOptions(options: options);

            (string? framework_warning, NuGetFramework project_framework) = FrameworkFinder.GetFramework(
                RequestedFramework: options.TargetFramework,
                ProjectFilePath: options.ProjectFilePath);

            project_options.ProjectFramework = project_framework.GetShortFolderName();

            var nuget_api_service = new NugetApi(
                nuget_config_path: options.NugetConfigPath,
                project_root_path: project_options.ProjectDirectory,
                project_framework: project_framework,
                with_nuget_org: options.WithNuGetOrg);

            var scanner = new ProjectScanner(
                options: project_options,
                nuget_api_service: nuget_api_service,
                project_framework: project_framework);

            Stopwatch scan_timer = Stopwatch.StartNew();

            Stopwatch deps_timer = Stopwatch.StartNew();
            ScanResult scan_result = scanner.RunScan();

            deps_timer.Stop();

            Stopwatch meta_timer = Stopwatch.StartNew();
            scanner.FetchDependenciesMetadata(
                scan_result: scan_result,
                with_details: options.WithDetails);
            meta_timer.Stop();

            scan_timer.Stop();

            if (framework_warning != null)
                scan_result.warnings.Add(framework_warning);

            if (Config.TRACE)
            {
                Console.WriteLine("Run summary:");
                Console.WriteLine($"    Dependencies resolved in: {deps_timer.ElapsedMilliseconds} ms.");
                Console.WriteLine($"    Metadata collected in:    {meta_timer.ElapsedMilliseconds} ms.");
                Console.WriteLine($"    Scan completed in:        {scan_timer.ElapsedMilliseconds} ms.");
            }

            var output_formatter = new OutputFormatJson(scan_result: scan_result);
            output_formatter.Write();

            if (Config.TRACE_OUTPUT)
            {
                Console.WriteLine("\n=============JSON OUTPUT================");
                string output = JsonConvert.SerializeObject(
                    value: output_formatter.scan_output,
                    formatting: Formatting.Indented);
                Console.WriteLine(output);

                Console.WriteLine("=======================================\n");
            }

            BasePackage project_package = scan_result.project_package;

            bool success = scan_result.Status == ScanResult.ResultStatus.Success;

            var with_warnings = Has_warnings(output_formatter);
            var with_errors = Has_errors(output_formatter);

            // also consider other errors
            if (success && with_errors)
                success = false;

            if (success)
            {
                Console.WriteLine($"\nScan Result: success: JSON file created at: {scan_result.Options!.OutputFilePath}");
                if (with_warnings)
                    PrintWarnings(scan_result, project_package);

                return ExecutionResult.Succeeded();
            }
            else
            {
                Console.WriteLine($"\nScan completed with Errors or Warnings: JSON file created at: {scan_result.Options!.OutputFilePath}");
                if (with_warnings)
                    PrintWarnings(scan_result, project_package);
                if (with_errors)
                    PrintErrors(scan_result, project_package);

                return ExecutionResult.Failed();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nERROR: scan failed:  {ex}");
            return ExecutionResult.Failed();
        }

        static void PrintWarnings(ScanResult scan_result, BasePackage project_package)
        {
            if (scan_result.warnings.Any())
                Console.WriteLine("    WARNING: " + string.Join(", ", scan_result.warnings));
            if (scan_result.errors.Any())
                Console.WriteLine("    ERROR: " + string.Join(", ", scan_result.errors));

            Console.WriteLine("\n    Errors or Warnings at the package level");
            Console.WriteLine($"       {project_package.name}@{project_package.version} with purl: {project_package.purl}");
            if (project_package.warnings.Any())
                Console.WriteLine("        WARNING: " + string.Join(", ", project_package.warnings));
            if (project_package.errors.Any())
                Console.WriteLine("        ERROR: " + string.Join(", ", project_package.errors));

            Console.WriteLine("\n        Errors or Warnings at the dependencies level");
            foreach (var dep in project_package.GetFlatDependencies())
            {
                if (dep.warnings.Any() || dep.errors.Any())
                {
                    Console.WriteLine($"            {dep.name}@{dep.version} with purl: {dep.purl}");
                    if (dep.warnings.Any())
                        Console.WriteLine("            WARNING: " + string.Join(", ", dep.warnings));
                    if (dep.errors.Any())
                        Console.WriteLine("            ERROR: " + string.Join(", ", dep.errors));
                }
            }
        }

        static void PrintErrors(ScanResult scan_result, BasePackage project_package)
        {
            if (scan_result.errors.Any())
                Console.WriteLine("\nERROR: " + string.Join(", ", scan_result.errors));

            if (project_package.errors.Any())
            {
                Console.WriteLine("\nERRORS at the package level:");
                Console.WriteLine($"    {project_package.name}@{project_package.version} with purl: {project_package.purl}");
                Console.WriteLine("    ERROR: " + string.Join(", ", project_package.errors));
            }

            Console.WriteLine("\nERRORS at the dependencies level:");
            foreach (var dep in project_package.GetFlatDependencies())
            {
                if (dep.errors.Any())
                {
                    Console.WriteLine($"    ERRORS for dependency: {dep.name}@{dep.version} with purl: {dep.purl}");
                    Console.WriteLine("    ERROR: " + string.Join(", ", dep.errors));
                }
            }
        }
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
            if (options.Debug)
            {
                Config.TRACE = true;
                Config.TRACE_DEEP = true;
                Config.TRACE_META = true;
                Config.TRACE_NET = true;
                Config.TRACE_OUTPUT = true;
            }

            if (Config.TRACE_ARGS)
                Console.WriteLine($"argument: with-details: {options.WithDetails}");

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

        public static ExecutionResult Failed(int exit_code = -1)
        {
            return new ExecutionResult
            {
                Success = false,
                ExitCode = exit_code
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