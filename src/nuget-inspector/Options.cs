using System.Reflection;
using Mono.Options;

namespace NugetInspector;

public class Options
{
    [CommandLineArg(key: "project-file", description: "Path to a .NET project file.")]
    public string ProjectFilePath = "";

    [CommandLineArg(key: "target-framework",
        description:
        "Optional .NET Target framework. Defaults to the first project target framework. "+
        "See https://learn.microsoft.com/en-us/dotnet/standard/frameworks for values")]
    public string TargetFramework = "";

    [CommandLineArg(key: "json", description: "JSON output file path.")]
    public string OutputFilePath = "";

    [CommandLineArg(key: "nuget-config", description: "Path to a NuGet.config file")]
    public string NugetConfigPath = "";

    [CommandLineArg(key: "nuget-url",
        description: "NuGet API URL. [default: https://api.nuget.org/v3/index.json]")]
    public string NugetApiFeedUrl = "https://api.nuget.org/v3/index.json";

    public bool ShowHelp;
    public bool Verbose;
    public bool ShowVersion;
    public bool ShowAbout;

    /// <summary>
    /// Print the values of this options object to the console.
    /// </summary>
    public void Print()
    {
        Console.WriteLine($"  ProjectFilePath: {ProjectFilePath}");
        Console.WriteLine($"  TargetFramework: {TargetFramework}");
        Console.WriteLine($"  OutputFilePath: {OutputFilePath}");
        Console.WriteLine($"  NugetConfigPath: {NugetConfigPath}");
        Console.WriteLine($"  NugetApiFeedUrl: {NugetApiFeedUrl}");
        Console.WriteLine($"  Verbose: {Verbose}");
        Console.WriteLine($"  ShowVersion: {ShowVersion}");
        Console.WriteLine($"  ShowAbout: {ShowAbout}");
    }

    /// <summary>
    /// Return a list of command line-like option values.
    /// </summary>
    public List<string> AsCliList()
    {
        List<string> options = new List<string>
        {
            $"--project-file {ProjectFilePath}",
            $"--json {OutputFilePath}",
        };

        if (TargetFramework != "")
            options.Add($"--target-framework {TargetFramework}");

        if (NugetConfigPath != "")
            options.Add($"--nuget-config {NugetConfigPath}");

        if (NugetApiFeedUrl != "https://api.nuget.org/v3/index.json")
            options.Add($"--nuget-url {NugetApiFeedUrl}");

        if (Verbose)
            options.Add($"--verbose");

        return options;
    }


    public static Options? ParseArguments(string[] args)
    {
        var options = new Options();
        var command_options = new OptionSet();

        foreach (var field in typeof(Options).GetFields())
        {
            if (Config.TRACE) Console.WriteLine($"ParseArguments.field: {field}");
            var attr = GetAttr<CommandLineArgAttribute>(field: field);
            if (attr != null)
                command_options.Add(prototype: $"{attr.Key}=", description: attr.Description,
                    action: value => { field.SetValue(obj: options, value: value); });
        }

        command_options.Add(prototype: "h|help", description: "Show this message and exit.",
            action: value => options.ShowHelp = value != null);
        command_options.Add(prototype: "v|verbose", description: "Display more verbose output.",
            action: value => options.Verbose = value != null);
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
            ShowHelpMessage(message: "Error: Unexpected extra argument or option. usage is: nuget-inspector [OPTIONS]",
                optionSet: command_options);
            return null;
        }

        if (options.ShowHelp)
        {
            ShowHelpMessage(message: "Usage: nuget-inspector [OPTIONS]", optionSet: command_options);
            return null;
        }

        if (options.ShowVersion)
        {
            Console.Error.WriteLine(Config.NUGET_INSPECTOR_VERSION);
                return null;
        }
        if (options.ShowAbout)
        {
            string message = ($"nuget-inspector v{Config.NUGET_INSPECTOR_VERSION}\n"
                              + "Inspect .NET code and NuGet package manifests. Resolve NuGet dependencies.\n"
                              + "SPDX-License-Identifier: Apache-2.0 AND MIT\n"
                              + "Copyright (c) nexB Inc. and others.\n"
                              + "https://github.com/nexB/nuget-inspector");
            Console.Error.WriteLine(message);
            return null;
        }
        
        // TODO: raise error if input or output are missing

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