using System.Reflection;
using Mono.Options;

namespace NugetInspector;

public class Options
{
    [CommandLineArg(
        key: "project-file",
        description: "Path to a .NET project file.")]
    public string ProjectFilePath = "";

    [CommandLineArg(
        key: "target-framework",
        description: "Optional .NET Target framework. Defaults to the first project target framework. " +
            "See https://learn.microsoft.com/en-us/dotnet/standard/frameworks for values")]
    public string TargetFramework = "";

    [CommandLineArg(
        key: "json",
        description: "JSON output file path.")]
    public string OutputFilePath = "";

    [CommandLineArg(
        key: "nuget-config",
        description: "Path to a nuget.config file to use, ignoring all other nuget.config.")]
    public string NugetConfigPath = "";

    // If True, return extra metadata details when available such as SHA512
    public bool WithDetails;
    public bool WithFallback;
    public bool WithNuGetOrg;
    public bool ShowHelp;
    public bool Verbose;
    public bool Debug;
    public bool ShowVersion;
    public bool ShowAbout;

    /// <summary>
    /// Print the values of this options object to the console.
    /// </summary>
    public void Print(int indent=0)
    {
        string margin = new (' ', indent);
        foreach (var opt in AsCliList())
            Console.WriteLine($"{margin}{opt}");
    }

    /// <summary>
    /// Return a list of command line-like option values to display in the output.
    /// </summary>
    public List<string> AsCliList()
    {
        List<string> options = new()
        {
            $"--project-file {ProjectFilePath}",
            $"--json {OutputFilePath}",
        };

        if (!string.IsNullOrWhiteSpace(TargetFramework))
            options.Add($"--target-framework {TargetFramework}");

        if (NugetConfigPath != "")
            options.Add($"--nuget-config {NugetConfigPath}");

        if (Verbose)
            options.Add("--verbose");

        if (Debug)
            options.Add("--debug");

        if (WithDetails)
            options.Add("--with-details");

        if (WithFallback)
            options.Add("--with-fallback");

        if (WithNuGetOrg)
            options.Add("--with-nuget-org");

        return options;
    }

    public static Options? ParseArguments(string[] args)
    {
        var options = new Options();
        var command_options = new OptionSet();

        foreach (var field in typeof(Options).GetFields())
        {
            if (Config.TRACE_ARGS) Console.WriteLine($"ParseArguments.field: {field}");
            var attr = GetAttr<CommandLineArgAttribute>(field: field);
            if (attr != null)
            {
                command_options.Add(
                    prototype: $"{attr.Key}=",
                    description: attr.Description,
                    action: value => field.SetValue(obj: options, value: value));
            }
        }

        command_options.Add(prototype: "with-details", description: "Optionally include package metadata details (such as checksum and size) when available.",
            action: value => options.WithDetails = value != null);

        command_options.Add(prototype: "with-fallback", description: "Optionally use a plain XML project file parser as fallback from failures.",
            action: value => options.WithDetails = value != null);

        command_options.Add(prototype: "with-nuget-org", description: "Optionally use the official, public nuget.org API as a fallback in addition to nuget.config-configured API sources.",
            action: value => options.WithNuGetOrg = value != null);

        command_options.Add(prototype: "h|help", description: "Show this message and exit.",
            action: value => options.ShowHelp = value != null);
        command_options.Add(prototype: "v|verbose", description: "Display more verbose output.",
            action: value => options.Verbose = value != null);
        command_options.Add(prototype: "debug", description: "Display very verbose debug output.",
            action: value => options.Debug = value != null);
        command_options.Add(prototype: "version", description: "Display nuget-inspector version and exit.",
            action: value => options.ShowVersion = value != null);
        command_options.Add(prototype: "about", description: "Display information about nuget-inspector and exit.",
            action: value => options.ShowAbout = value != null);

        try
        {
            command_options.Parse(arguments: args);
        }
        catch (OptionException)
        {
            ShowHelpMessage(
                message: "Error: Unexpected extra argument or option. Usage is: nuget-inspector [OPTIONS]",
                optionSet: command_options);
            return null;
        }

        if (options.ShowHelp)
        {
            ShowHelpMessage(
                message: "Usage: nuget-inspector [OPTIONS]",
                optionSet: command_options);
            return null;
        }

        if (options.ShowVersion)
        {
            Console.Error.WriteLine(Config.NUGET_INSPECTOR_VERSION);
                return null;
        }
        if (options.ShowAbout)
        {
            Console.Error.WriteLine(
                $"nuget-inspector v{Config.NUGET_INSPECTOR_VERSION}\n"
                + "Inspect .NET and NuGet projects and package manifests. Resolve NuGet dependencies.\n"
                + "SPDX-License-Identifier: Apache-2.0 AND MIT\n"
                + "Copyright (c) nexB Inc. and others.\n"
                + "https://github.com/aboutcode-org/nuget-inspector");
            return null;
        }

        if (string.IsNullOrWhiteSpace(options.ProjectFilePath))
        {
            ShowHelpMessage(
                message: "Error: missing required --project-file option. Usage: nuget-inspector [OPTIONS]",
                optionSet: command_options);
            return null;
        }

        if (string.IsNullOrWhiteSpace(options.OutputFilePath))
        {
            ShowHelpMessage(
                message: "Error: missing required --json option. Usage: nuget-inspector [OPTIONS]",
                optionSet: command_options);
            return null;
        }

        return options;
    }

    private static void ShowHelpMessage(string message, OptionSet optionSet)
    {
        Console.Error.WriteLine(message);
        optionSet.WriteOptionDescriptions(o: Console.Error);
    }

    private static T? GetAttr<T>(FieldInfo field) where T : class
    {
        var attrs = field.GetCustomAttributes(attributeType: typeof(T), inherit: false);
        if (attrs.Length > 0) return attrs[0] as T;
        return null;
    }
}

internal class CommandLineArgAttribute : Attribute
{
    public string Description;
    public string Key;

    public CommandLineArgAttribute(string key, string description = "")
    {
        Key = key;
        Description = description;
    }
}
