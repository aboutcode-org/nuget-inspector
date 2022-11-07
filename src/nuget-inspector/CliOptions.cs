using System.Reflection;
using Mono.Options;

namespace NugetInspector;


public class CliOptions
{
    [CommandLineArg(key: "project-file", description: "Path to a .NET solution or project file.")]
    public string ProjectFilePath = "";

    [CommandLineArg(key: "json", description: "JSON output file path.")]
    public string OutputFilePath = "";

    [CommandLineArg(key: "nuget-config", description: "Path to NuGet config file")]
    public string NugetConfigPath = "";

    [CommandLineArg(key: "nuget-url",
        description: "NuGet API URL feed [default: https://api.nuget.org/v3/index.json]")]
    public string NugetApiFeedUrl = "https://api.nuget.org/v3/index.json";

    public bool ShowHelp;

    public bool Verbose;

    public static CliOptions? ParseArguments(string[] args)
    {
        var result = new CliOptions();
        var commandOptions = new OptionSet();

        foreach (var field in typeof(CliOptions).GetFields())
        {
            var attr = GetAttr<CommandLineArgAttribute>(field);
            if (attr != null)
                commandOptions.Add($"{attr.Key}=", attr.Description, value => { field.SetValue(result, value); });
        }

        commandOptions.Add("h|help", "Show this message and exit.", value => result.ShowHelp = value != null);
        commandOptions.Add("v|verbose", "Display more verbose output.", value => result.Verbose = value != null);

        try
        {
            commandOptions.Parse(args);
        }
        catch (OptionException)
        {
            ShowHelpMessage("Error: Unexpected extra argument or option. usage is: nuget-inspector [OPTIONS]",
                commandOptions);
            return null;
        }

        if (result.ShowHelp)
        {
            ShowHelpMessage("Usage: nuget-inspector [OPTIONS]", commandOptions);
            return null;
        }

        return result;
    }

    private static void ShowHelpMessage(string message, OptionSet optionSet)
    {
        Console.Error.WriteLine(message);
        optionSet.WriteOptionDescriptions(Console.Error);
    }

    private static  T? GetAttr<T>(FieldInfo field) where T : class
    {
        var attrs = field.GetCustomAttributes(typeof(T), false);
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
