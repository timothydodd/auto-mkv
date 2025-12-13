using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AutoMk.Interfaces;
using AutoMk.Models;
using Spectre.Console;

namespace AutoMk.Services;

public class ConsolePromptService : IConsolePromptService
{
    private const string CancelKey = "__cancel__";

    public PromptResult<string> SelectPrompt(SelectPromptOptions options)
    {
        if (options.ClearScreenBefore)
            ClearScreen();

        if (!string.IsNullOrEmpty(options.HeaderText))
            DisplayHeader(options.HeaderText);

        if (!string.IsNullOrEmpty(options.FooterText))
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(options.FooterText)}[/]");

        var prompt = new SelectionPrompt<string>()
            .Title(Markup.Escape(options.Question))
            .PageSize(15)
            .HighlightStyle(new Style(Color.Cyan1));

        foreach (var choice in options.Choices)
        {
            var display = string.IsNullOrEmpty(choice.Description)
                ? choice.Display
                : $"{choice.Display} [dim]- {choice.Description}[/]";
            prompt.AddChoice(display);
        }

        if (options.AllowCancel)
            prompt.AddChoice($"[red]{Markup.Escape(options.CancelText)}[/]");

        var selected = AnsiConsole.Prompt(prompt);

        // Check if cancel was selected
        if (options.AllowCancel && selected.Contains(options.CancelText))
            return PromptResult<string>.CreateCancelled();

        // Find the matching choice by display text
        var selectedChoice = options.Choices.FirstOrDefault(c =>
            selected.StartsWith(c.Display) || selected == c.Display);

        if (selectedChoice != null)
            return PromptResult<string>.CreateSuccess(selectedChoice.Key);

        return PromptResult<string>.CreateError("Selection not found");
    }

    public PromptResult<T> SelectPrompt<T>(SelectPromptOptions options)
    {
        var result = SelectPrompt(options);
        if (!result.Success)
            return PromptResult<T>.CreateError(result.ErrorMessage ?? "Selection failed");

        if (result.Cancelled)
            return PromptResult<T>.CreateCancelled();

        var selectedChoice = options.Choices.FirstOrDefault(c => c.Key == result.Value);
        if (selectedChoice?.Value is T value)
            return PromptResult<T>.CreateSuccess(value);

        return PromptResult<T>.CreateError("Could not convert selected value to requested type");
    }

    public PromptResult<List<string>> MultiSelectPrompt(SelectPromptOptions options)
    {
        if (options.ClearScreenBefore)
            ClearScreen();

        if (!string.IsNullOrEmpty(options.HeaderText))
            DisplayHeader(options.HeaderText);

        var footerText = string.IsNullOrEmpty(options.FooterText)
            ? "[dim]Use space to select, enter to confirm[/]"
            : $"[dim]{Markup.Escape(options.FooterText)}[/]\n[dim]Use space to select, enter to confirm[/]";
        AnsiConsole.MarkupLine(footerText);

        var prompt = new MultiSelectionPrompt<string>()
            .Title(Markup.Escape(options.Question))
            .PageSize(15)
            .HighlightStyle(new Style(Color.Cyan1))
            .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]");

        foreach (var choice in options.Choices)
        {
            var display = string.IsNullOrEmpty(choice.Description)
                ? choice.Display
                : $"{choice.Display} [dim]- {choice.Description}[/]";
            prompt.AddChoice(display);
        }

        if (options.AllowCancel)
            prompt.AddChoice($"[red]{Markup.Escape(options.CancelText)}[/]");

        var selections = AnsiConsole.Prompt(prompt);

        // Check if cancel was selected
        if (options.AllowCancel && selections.Any(s => s.Contains(options.CancelText)))
            return PromptResult<List<string>>.CreateCancelled();

        var selectedKeys = new List<string>();
        foreach (var selection in selections)
        {
            var selectedChoice = options.Choices.FirstOrDefault(c =>
                selection.StartsWith(c.Display) || selection == c.Display);
            if (selectedChoice != null && !selectedKeys.Contains(selectedChoice.Key))
                selectedKeys.Add(selectedChoice.Key);
        }

        if (selectedKeys.Any())
            return PromptResult<List<string>>.CreateSuccess(selectedKeys);

        return PromptResult<List<string>>.CreateError("No valid selections made");
    }

    public PromptResult<List<T>> MultiSelectPrompt<T>(SelectPromptOptions options)
    {
        var result = MultiSelectPrompt(options);
        if (!result.Success)
            return PromptResult<List<T>>.CreateError(result.ErrorMessage ?? "Multi-selection failed");

        if (result.Cancelled)
            return PromptResult<List<T>>.CreateCancelled();

        var values = new List<T>();
        foreach (var key in result.Value!)
        {
            var selectedChoice = options.Choices.FirstOrDefault(c => c.Key == key);
            if (selectedChoice?.Value is T value)
                values.Add(value);
        }

        return PromptResult<List<T>>.CreateSuccess(values);
    }

    public PromptResult<string> TextPrompt(TextPromptOptions options)
    {
        var promptText = Markup.Escape(options.Question);

        var prompt = new TextPrompt<string>(promptText);

        if (!options.Required)
            prompt.AllowEmpty();

        if (!string.IsNullOrEmpty(options.DefaultValue))
            prompt.DefaultValue(options.DefaultValue);

        if (options.IsPassword)
            prompt.Secret();

        if (!string.IsNullOrEmpty(options.ValidationPattern))
        {
            prompt.Validate(input =>
            {
                if (string.IsNullOrEmpty(input) && !options.Required)
                    return ValidationResult.Success();

                if (!Regex.IsMatch(input ?? "", options.ValidationPattern))
                    return ValidationResult.Error(options.ValidationMessage ?? "[red]Invalid format[/]");

                if (options.MaxLength.HasValue && (input?.Length ?? 0) > options.MaxLength.Value)
                    return ValidationResult.Error($"[red]Maximum length is {options.MaxLength.Value} characters[/]");

                return ValidationResult.Success();
            });
        }
        else if (options.MaxLength.HasValue)
        {
            prompt.Validate(input =>
            {
                if ((input?.Length ?? 0) > options.MaxLength.Value)
                    return ValidationResult.Error($"[red]Maximum length is {options.MaxLength.Value} characters[/]");
                return ValidationResult.Success();
            });
        }

        try
        {
            var result = AnsiConsole.Prompt(prompt);
            return PromptResult<string>.CreateSuccess(result);
        }
        catch (Exception)
        {
            if (options.AllowCancel)
                return PromptResult<string>.CreateCancelled();
            throw;
        }
    }

    public PromptResult<bool> ConfirmPrompt(ConfirmPromptOptions options)
    {
        var result = AnsiConsole.Confirm(Markup.Escape(options.Question), options.DefaultValue);
        return PromptResult<bool>.CreateSuccess(result);
    }

    public PromptResult<int> NumberPrompt(NumberPromptOptions options)
    {
        var promptText = Markup.Escape(options.Question);

        if (options.MinValue.HasValue && options.MaxValue.HasValue)
            promptText += $" [dim](Range: {options.MinValue.Value} - {options.MaxValue.Value})[/]";
        else if (options.MinValue.HasValue)
            promptText += $" [dim](Min: {options.MinValue.Value})[/]";
        else if (options.MaxValue.HasValue)
            promptText += $" [dim](Max: {options.MaxValue.Value})[/]";

        // For optional numbers, use a string prompt and parse manually
        // because TextPrompt<int>.AllowEmpty() doesn't work correctly
        if (!options.Required)
        {
            promptText += " [dim](press Enter to skip)[/]";
            var stringPrompt = new TextPrompt<string>(promptText)
                .AllowEmpty();

            if (options.DefaultValue.HasValue)
                stringPrompt.DefaultValue(options.DefaultValue.Value.ToString());

            try
            {
                var stringResult = AnsiConsole.Prompt(stringPrompt);

                // Empty string means user skipped
                if (string.IsNullOrWhiteSpace(stringResult))
                {
                    if (options.DefaultValue.HasValue)
                        return PromptResult<int>.CreateSuccess(options.DefaultValue.Value);
                    // Return a "skipped" result - Success is false but not an error
                    return PromptResult<int>.CreateCancelled();
                }

                // Try to parse as int
                if (int.TryParse(stringResult, out var parsedValue))
                {
                    // Validate range
                    if (options.MinValue.HasValue && parsedValue < options.MinValue.Value)
                    {
                        AnsiConsole.MarkupLine($"[red]Must be at least {options.MinValue.Value}[/]");
                        return NumberPrompt(options); // Retry
                    }

                    if (options.MaxValue.HasValue && parsedValue > options.MaxValue.Value)
                    {
                        AnsiConsole.MarkupLine($"[red]Must be no more than {options.MaxValue.Value}[/]");
                        return NumberPrompt(options); // Retry
                    }

                    return PromptResult<int>.CreateSuccess(parsedValue);
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]Please enter a valid number[/]");
                    return NumberPrompt(options); // Retry
                }
            }
            catch (Exception)
            {
                if (options.AllowCancel)
                    return PromptResult<int>.CreateCancelled();

                return PromptResult<int>.CreateError("No value provided");
            }
        }

        // Required number - use the standard int prompt
        var prompt = new TextPrompt<int>(promptText);

        if (options.DefaultValue.HasValue)
            prompt.DefaultValue(options.DefaultValue.Value);

        prompt.Validate(value =>
        {
            if (options.MinValue.HasValue && value < options.MinValue.Value)
                return ValidationResult.Error($"[red]Must be at least {options.MinValue.Value}[/]");

            if (options.MaxValue.HasValue && value > options.MaxValue.Value)
                return ValidationResult.Error($"[red]Must be no more than {options.MaxValue.Value}[/]");

            return ValidationResult.Success();
        });

        try
        {
            var result = AnsiConsole.Prompt(prompt);
            return PromptResult<int>.CreateSuccess(result);
        }
        catch (Exception)
        {
            if (options.AllowCancel)
                return PromptResult<int>.CreateCancelled();

            if (options.DefaultValue.HasValue)
                return PromptResult<int>.CreateSuccess(options.DefaultValue.Value);

            return PromptResult<int>.CreateError("No value provided");
        }
    }

    // Display methods
    public void DisplayHeader(string title, char borderChar = '=')
    {
        var rule = new Rule($"[yellow]{Markup.Escape(title)}[/]")
        {
            Justification = Justify.Left,
            Style = Style.Parse("yellow")
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();
    }

    public void DisplayMessage(string message, ConsoleColor? color = null)
    {
        if (color.HasValue)
        {
            var spectreColor = ConvertConsoleColor(color.Value);
            AnsiConsole.MarkupLine($"[{spectreColor}]{Markup.Escape(message)}[/]");
        }
        else
        {
            AnsiConsole.WriteLine(message);
        }
    }

    public void DisplayError(string error)
    {
        AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(error)}[/]");
    }

    public void DisplaySuccess(string message)
    {
        AnsiConsole.MarkupLine($"[green]{Markup.Escape(message)}[/]");
    }

    public void DisplayWarning(string warning)
    {
        AnsiConsole.MarkupLine($"[yellow]Warning: {Markup.Escape(warning)}[/]");
    }

    public void ClearScreen()
    {
        AnsiConsole.Clear();
    }

    private static string ConvertConsoleColor(ConsoleColor color)
    {
        return color switch
        {
            ConsoleColor.Black => "black",
            ConsoleColor.DarkBlue => "navy",
            ConsoleColor.DarkGreen => "green",
            ConsoleColor.DarkCyan => "teal",
            ConsoleColor.DarkRed => "maroon",
            ConsoleColor.DarkMagenta => "purple",
            ConsoleColor.DarkYellow => "olive",
            ConsoleColor.Gray => "silver",
            ConsoleColor.DarkGray => "grey",
            ConsoleColor.Blue => "blue",
            ConsoleColor.Green => "lime",
            ConsoleColor.Cyan => "aqua",
            ConsoleColor.Red => "red",
            ConsoleColor.Magenta => "fuchsia",
            ConsoleColor.Yellow => "yellow",
            ConsoleColor.White => "white",
            _ => "default"
        };
    }
}
