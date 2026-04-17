using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoMk.Models;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

namespace AutoMk.Services;

/// <summary>
/// Validates application settings at startup and provides detailed error messages.
/// </summary>
public static class StartupValidator
{
    /// <summary>
    /// Gets the correct path for appsettings.json, handling single-file extraction.
    /// </summary>
    public static string GetAppsettingsPath()
    {
        // For single-file apps, AppContext.BaseDirectory points to temp extraction folder
        // We need to find the actual executable location
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            var exeDir = Path.GetDirectoryName(exePath);
            if (!string.IsNullOrEmpty(exeDir))
            {
                var configPath = Path.Combine(exeDir, "appsettings.json");
                if (File.Exists(configPath))
                {
                    return configPath;
                }
            }
        }

        // Fallback to AppContext.BaseDirectory
        return Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    }

    /// <summary>
    /// Checks if initial setup is required and prompts the user to configure settings.
    /// Returns true if setup was performed and app should reload config.
    /// </summary>
    public static bool CheckAndRunInitialSetup(IConfigurationRoot config)
    {
        var appsettingsPath = GetAppsettingsPath();

        // Check if critical settings are missing
        var ripSection = config.GetSection("RipSettings");
        var omdbSection = config.GetSection("OmdbSettings");

        var makeMkvPath = ripSection["MakeMKVPath"];
        var outputPath = ripSection["Output"];
        var apiKey = omdbSection["ApiKey"];

        bool needsSetup = string.IsNullOrWhiteSpace(apiKey) ||
                          string.IsNullOrWhiteSpace(makeMkvPath) ||
                          string.IsNullOrWhiteSpace(outputPath) ||
                          !File.Exists(makeMkvPath);

        if (!needsSetup)
        {
            return false;
        }

        // Display setup prompt
        AnsiConsole.WriteLine();
        var setupPanel = new Panel(
            new Markup("[yellow]Initial configuration is required before AutoMk can run.[/]\n\n" +
                      "Please provide the following settings:"))
        {
            Header = new PanelHeader("[cyan] First-Time Setup [/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Cyan1),
            Padding = new Padding(2, 1, 2, 1)
        };
        AnsiConsole.Write(setupPanel);
        AnsiConsole.WriteLine();

        // Prompt for MakeMKV path
        var defaultMakeMkvPath = @"C:\Program Files (x86)\MakeMKV\makemkvcon64.exe";
        if (!string.IsNullOrWhiteSpace(makeMkvPath))
        {
            defaultMakeMkvPath = makeMkvPath;
        }

        string newMakeMkvPath;
        while (true)
        {
            newMakeMkvPath = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]MakeMKV Path[/] [dim](makemkvcon64.exe)[/]:")
                    .DefaultValue(defaultMakeMkvPath)
                    .PromptStyle("green"));

            if (File.Exists(newMakeMkvPath))
            {
                break;
            }
            AnsiConsole.MarkupLine($"[red]File not found:[/] {Markup.Escape(newMakeMkvPath)}");
            AnsiConsole.MarkupLine("[yellow]Please ensure MakeMKV is installed and provide the correct path.[/]");
        }

        // Prompt for output directory
        var defaultOutputPath = @"C:\Videos";
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            defaultOutputPath = outputPath;
        }

        string newOutputPath;
        while (true)
        {
            newOutputPath = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]Output Directory[/] [dim](where ripped files are saved)[/]:")
                    .DefaultValue(defaultOutputPath)
                    .PromptStyle("green"));

            try
            {
                if (!Directory.Exists(newOutputPath))
                {
                    Directory.CreateDirectory(newOutputPath);
                    AnsiConsole.MarkupLine($"[green]Created directory:[/] {Markup.Escape(newOutputPath)}");
                }
                break;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Cannot create directory:[/] {Markup.Escape(ex.Message)}");
            }
        }

        // Prompt for OMDB API key
        var defaultApiKey = string.IsNullOrWhiteSpace(apiKey) ? "" : apiKey;

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Get a free OMDB API key at:[/] [link]https://www.omdbapi.com/apikey.aspx[/]");

        string newApiKey;
        while (true)
        {
            var prompt = new TextPrompt<string>("[cyan]OMDB API Key[/]:")
                .PromptStyle("green");

            if (!string.IsNullOrWhiteSpace(defaultApiKey))
            {
                prompt.DefaultValue(defaultApiKey);
            }

            newApiKey = AnsiConsole.Prompt(prompt);

            if (!string.IsNullOrWhiteSpace(newApiKey))
            {
                break;
            }
            AnsiConsole.MarkupLine("[red]OMDB API Key is required for media identification.[/]");
        }

        // Save settings to appsettings.json
        try
        {
            AnsiConsole.MarkupLine($"[dim]Saving to:[/] {Markup.Escape(appsettingsPath)}");

            JsonObject configJson;

            if (File.Exists(appsettingsPath))
            {
                var existingJson = File.ReadAllText(appsettingsPath);
                var parseOptions = new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };
                configJson = JsonNode.Parse(existingJson, documentOptions: parseOptions) as JsonObject ?? new JsonObject();
            }
            else
            {
                configJson = new JsonObject();
            }

            // Update RipSettings
            if (configJson["RipSettings"] == null)
            {
                configJson["RipSettings"] = new JsonObject();
            }
            var ripSettings = configJson["RipSettings"] as JsonObject;
            if (ripSettings != null)
            {
                ripSettings["MakeMKVPath"] = newMakeMkvPath;
                ripSettings["Output"] = newOutputPath;
            }

            // Update OmdbSettings
            if (configJson["OmdbSettings"] == null)
            {
                configJson["OmdbSettings"] = new JsonObject();
            }
            var omdbSettings = configJson["OmdbSettings"] as JsonObject;
            if (omdbSettings != null)
            {
                omdbSettings["ApiKey"] = newApiKey;
                if (omdbSettings["BaseUrl"] == null)
                {
                    omdbSettings["BaseUrl"] = "https://www.omdbapi.com/";
                }
            }

            // Write back to file
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(appsettingsPath, configJson.ToJsonString(options));

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]Configuration saved successfully![/]");
            AnsiConsole.MarkupLine($"[dim]Settings saved to:[/] {Markup.Escape(appsettingsPath)}");
            AnsiConsole.WriteLine();

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[red]Access denied![/] Cannot write to configuration file.");
            AnsiConsole.MarkupLine($"[yellow]File:[/] {Markup.Escape(appsettingsPath)}");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[cyan]Please run AutoMk as Administrator to save settings.[/]");
            AnsiConsole.MarkupLine("[dim]Right-click the application and select 'Run as administrator'[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Press any key to exit...[/]");
            Console.ReadKey(true);
            Environment.Exit(1);
            return false;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to save configuration:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine($"[dim]File:[/] {Markup.Escape(appsettingsPath)}");
            AnsiConsole.MarkupLine("[yellow]Please manually edit appsettings.json with your settings.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Press any key to exit...[/]");
            Console.ReadKey(true);
            Environment.Exit(1);
            return false;
        }
    }

    /// <summary>
    /// Validates all required application settings and returns validation results.
    /// </summary>
    /// <param name="config">The configuration root to validate.</param>
    /// <returns>A validation result containing any errors found.</returns>
    public static StartupValidationResult Validate(IConfigurationRoot config)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        // Check if appsettings.json exists
        var appsettingsPath = GetAppsettingsPath();
        if (!File.Exists(appsettingsPath))
        {
            errors.Add(new ValidationError(
                "Configuration File Missing",
                "appsettings.json not found in the application directory.",
                $"Create an appsettings.json file at: {appsettingsPath}"));
        }

        // Validate RipSettings section
        var ripSection = config.GetSection("RipSettings");
        if (!ripSection.Exists())
        {
            errors.Add(new ValidationError(
                "RipSettings Missing",
                "The RipSettings section is required but not found in configuration.",
                "Add a 'RipSettings' section to your appsettings.json with MakeMKVPath and Output properties."));
        }
        else
        {
            ValidateRipSettings(ripSection, errors, warnings);
        }

        // Validate OmdbSettings section
        var omdbSection = config.GetSection("OmdbSettings");
        if (!omdbSection.Exists())
        {
            errors.Add(new ValidationError(
                "OmdbSettings Missing",
                "The OmdbSettings section is required but not found in configuration.",
                "Add an 'OmdbSettings' section to your appsettings.json with ApiKey and BaseUrl properties."));
        }
        else
        {
            ValidateOmdbSettings(omdbSection, errors, warnings);
        }

        return new StartupValidationResult(errors, warnings);
    }

    private static void ValidateRipSettings(IConfigurationSection section, List<ValidationError> errors, List<ValidationWarning> warnings)
    {
        // Validate MakeMKVPath
        var makeMkvPath = section["MakeMKVPath"];
        if (string.IsNullOrWhiteSpace(makeMkvPath))
        {
            errors.Add(new ValidationError(
                "MakeMKVPath Missing",
                "RipSettings.MakeMKVPath is required but not configured.",
                "Set RipSettings.MakeMKVPath to the full path of makemkvcon64.exe (e.g., \"C:\\\\Program Files (x86)\\\\MakeMKV\\\\makemkvcon64.exe\")"));
        }
        else if (!File.Exists(makeMkvPath))
        {
            errors.Add(new ValidationError(
                "MakeMKV Not Found",
                $"MakeMKV executable not found at configured path: {makeMkvPath}",
                "Ensure MakeMKV is installed and the path is correct. Download from: https://www.makemkv.com/"));
        }

        // Validate Output directory
        var outputPath = section["Output"];
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            errors.Add(new ValidationError(
                "Output Path Missing",
                "RipSettings.Output is required but not configured.",
                "Set RipSettings.Output to a directory path where ripped files will be saved."));
        }
        else
        {
            // Check if directory exists or can be created
            if (!Directory.Exists(outputPath))
            {
                try
                {
                    Directory.CreateDirectory(outputPath);
                    warnings.Add(new ValidationWarning(
                        "Output Directory Created",
                        $"The output directory did not exist and was created: {outputPath}"));
                }
                catch (Exception ex)
                {
                    errors.Add(new ValidationError(
                        "Output Directory Invalid",
                        $"Cannot create output directory: {outputPath}",
                        $"Error: {ex.Message}. Ensure the path is valid and you have write permissions."));
                }
            }
            else
            {
                // Check if directory is writable
                try
                {
                    var testFile = Path.Combine(outputPath, ".automk_write_test");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                }
                catch (Exception ex)
                {
                    errors.Add(new ValidationError(
                        "Output Directory Not Writable",
                        $"Cannot write to output directory: {outputPath}",
                        $"Error: {ex.Message}. Ensure you have write permissions to this directory."));
                }
            }
        }

        // Validate MediaStateDirectory if specified
        var stateDir = section["MediaStateDirectory"] ?? "state";
        if (!Directory.Exists(stateDir))
        {
            try
            {
                Directory.CreateDirectory(stateDir);
            }
            catch (Exception ex)
            {
                warnings.Add(new ValidationWarning(
                    "State Directory Issue",
                    $"Cannot create state directory '{stateDir}': {ex.Message}. State persistence may not work."));
            }
        }

        // Validate FileTransferSettings if enabled
        var enableFileTransfer = section.GetValue<bool>("EnableFileTransfer");
        if (enableFileTransfer)
        {
            var transferSection = section.GetSection("FileTransferSettings");
            if (transferSection.Exists())
            {
                var targetUrl = transferSection["TargetServiceUrl"];
                if (string.IsNullOrWhiteSpace(targetUrl))
                {
                    errors.Add(new ValidationError(
                        "File Transfer URL Missing",
                        "FileTransferSettings.TargetServiceUrl is required when file transfer is enabled.",
                        "Set FileTransferSettings.TargetServiceUrl to the URL of the file transfer service."));
                }
                else if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out _))
                {
                    errors.Add(new ValidationError(
                        "Invalid File Transfer URL",
                        $"FileTransferSettings.TargetServiceUrl is not a valid URL: {targetUrl}",
                        "Provide a valid URL (e.g., \"http://fileserver:5000\")"));
                }
            }
            else
            {
                errors.Add(new ValidationError(
                    "FileTransferSettings Missing",
                    "EnableFileTransfer is true but FileTransferSettings section is not configured.",
                    "Add a FileTransferSettings section with TargetServiceUrl, or set EnableFileTransfer to false."));
            }
        }

        // Validate size filtering settings
        var minSize = section.GetValue<double>("MinSizeGB");
        var maxSize = section.GetValue<double>("MaxSizeGB");
        if (minSize > 0 && maxSize > 0 && minSize > maxSize)
        {
            warnings.Add(new ValidationWarning(
                "Invalid Size Range",
                $"MinSizeGB ({minSize}) is greater than MaxSizeGB ({maxSize}). This may filter out all tracks."));
        }

        // Validate chapter filtering settings
        var minChapters = section.GetValue<int>("MinChapters");
        var maxChapters = section.GetValue<int>("MaxChapters");
        if (minChapters > 0 && maxChapters > 0 && minChapters > maxChapters)
        {
            warnings.Add(new ValidationWarning(
                "Invalid Chapter Range",
                $"MinChapters ({minChapters}) is greater than MaxChapters ({maxChapters}). This may filter out all tracks."));
        }
    }

    private static void ValidateOmdbSettings(IConfigurationSection section, List<ValidationError> errors, List<ValidationWarning> warnings)
    {
        // Validate ApiKey
        var apiKey = section["ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            errors.Add(new ValidationError(
                "OMDB API Key Missing",
                "OmdbSettings.ApiKey is required but not configured.",
                "Get a free API key from https://www.omdbapi.com/apikey.aspx and add it to OmdbSettings.ApiKey"));
        }

        // Validate BaseUrl
        var baseUrl = section["BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            errors.Add(new ValidationError(
                "OMDB BaseUrl Missing",
                "OmdbSettings.BaseUrl is required but not configured.",
                "Set OmdbSettings.BaseUrl to \"https://www.omdbapi.com/\""));
        }
        else if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            errors.Add(new ValidationError(
                "Invalid OMDB BaseUrl",
                $"OmdbSettings.BaseUrl is not a valid URL: {baseUrl}",
                "Set OmdbSettings.BaseUrl to \"https://www.omdbapi.com/\""));
        }
    }

    /// <summary>
    /// Displays validation results to the console and returns whether validation passed.
    /// </summary>
    public static bool DisplayResults(StartupValidationResult result)
    {
        if (result.IsValid && result.Warnings.Count == 0)
        {
            return true;
        }

        AnsiConsole.WriteLine();

        // Display warnings first
        if (result.Warnings.Count > 0)
        {
            var warningPanel = new Panel(
                BuildWarningContent(result.Warnings))
            {
                Header = new PanelHeader("[yellow] Warnings [/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Yellow),
                Padding = new Padding(1, 0, 1, 0)
            };
            AnsiConsole.Write(warningPanel);
            AnsiConsole.WriteLine();
        }

        // Display errors
        if (result.Errors.Count > 0)
        {
            var errorPanel = new Panel(
                BuildErrorContent(result.Errors))
            {
                Header = new PanelHeader("[red] Configuration Errors [/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Red),
                Padding = new Padding(1, 0, 1, 0)
            };
            AnsiConsole.Write(errorPanel);
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine("[red]Application cannot start due to configuration errors.[/]");
            AnsiConsole.MarkupLine("[dim]Please fix the issues above and restart the application.[/]");
            AnsiConsole.WriteLine();

            return false;
        }

        return true;
    }

    private static Markup BuildWarningContent(List<ValidationWarning> warnings)
    {
        var lines = new List<string>();
        foreach (var warning in warnings)
        {
            lines.Add($"[yellow]! {Markup.Escape(warning.Title)}[/]");
            lines.Add($"  [dim]{Markup.Escape(warning.Message)}[/]");
            lines.Add("");
        }
        return new Markup(string.Join("\n", lines).TrimEnd());
    }

    private static Markup BuildErrorContent(List<ValidationError> errors)
    {
        var lines = new List<string>();
        for (int i = 0; i < errors.Count; i++)
        {
            var error = errors[i];
            lines.Add($"[red]{i + 1}. {Markup.Escape(error.Title)}[/]");
            lines.Add($"   [white]{Markup.Escape(error.Message)}[/]");
            lines.Add($"   [green]Fix:[/] [dim]{Markup.Escape(error.Suggestion)}[/]");
            if (i < errors.Count - 1)
            {
                lines.Add("");
            }
        }
        return new Markup(string.Join("\n", lines));
    }
}

public class StartupValidationResult
{
    public List<ValidationError> Errors { get; }
    public List<ValidationWarning> Warnings { get; }
    public bool IsValid => Errors.Count == 0;

    public StartupValidationResult(List<ValidationError> errors, List<ValidationWarning> warnings)
    {
        Errors = errors;
        Warnings = warnings;
    }
}

public class ValidationError
{
    public string Title { get; }
    public string Message { get; }
    public string Suggestion { get; }

    public ValidationError(string title, string message, string suggestion)
    {
        Title = title;
        Message = message;
        Suggestion = suggestion;
    }
}

public class ValidationWarning
{
    public string Title { get; }
    public string Message { get; }

    public ValidationWarning(string title, string message)
    {
        Title = title;
        Message = message;
    }
}
