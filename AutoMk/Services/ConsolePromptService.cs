using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AutoMk.Interfaces;
using AutoMk.Models;

namespace AutoMk.Services;

public class ConsolePromptService : IConsolePromptService
{
    public PromptResult<string> SelectPrompt(SelectPromptOptions options)
    {
        if (options.ClearScreenBefore)
            ClearScreen();

        if (!string.IsNullOrEmpty(options.HeaderText))
            DisplayHeader(options.HeaderText);

        Console.WriteLine(options.Question);
        Console.WriteLine();

        // Display choices
        for (int i = 0; i < options.Choices.Count; i++)
        {
            var choice = options.Choices[i];
            var prefix = options.ShowNumbers ? $"{i + 1}. " : "- ";
            var display = $"{prefix}{choice.Display}";
            
            if (!string.IsNullOrEmpty(choice.Description))
                display += $" - {choice.Description}";
                
            Console.WriteLine(display);
        }

        if (options.AllowCancel)
        {
            var cancelIndex = options.Choices.Count + 1;
            var prefix = options.ShowNumbers ? $"{cancelIndex}. " : "- ";
            Console.WriteLine($"{prefix}{options.CancelText}");
        }

        Console.WriteLine();
        if (!string.IsNullOrEmpty(options.FooterText))
        {
            Console.WriteLine(options.FooterText);
            Console.WriteLine();
        }

        while (true)
        {
            var promptRange = options.ShowNumbers 
                ? $"(1-{options.Choices.Count + (options.AllowCancel ? 1 : 0)})" 
                : "";
            Console.Write($"{options.PromptText} {promptRange}: ");
            
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input))
                continue;

            // Try to parse as number if showing numbers
            if (options.ShowNumbers && int.TryParse(input, out var choice))
            {
                if (choice >= 1 && choice <= options.Choices.Count)
                {
                    var selectedChoice = options.Choices[choice - 1];
                    return PromptResult<string>.CreateSuccess(selectedChoice.Key);
                }
                else if (options.AllowCancel && choice == options.Choices.Count + 1)
                {
                    return PromptResult<string>.CreateCancelled();
                }
            }
            else
            {
                // Try to match by key or display text
                var selectedChoice = options.Choices.FirstOrDefault(c => 
                    c.Key.Equals(input, StringComparison.OrdinalIgnoreCase) ||
                    c.Display.Equals(input, StringComparison.OrdinalIgnoreCase));
                    
                if (selectedChoice != null)
                    return PromptResult<string>.CreateSuccess(selectedChoice.Key);
                    
                if (options.AllowCancel && input.Equals("cancel", StringComparison.OrdinalIgnoreCase))
                    return PromptResult<string>.CreateCancelled();
            }

            DisplayError("Invalid choice. Please try again.");
        }
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
        var modifiedOptions = new SelectPromptOptions
        {
            Question = options.Question,
            Choices = options.Choices,
            AllowCancel = options.AllowCancel,
            CancelText = options.CancelText,
            PromptText = "Enter choices separated by commas (e.g., 1,3,5)",
            ShowNumbers = options.ShowNumbers,
            ClearScreenBefore = options.ClearScreenBefore,
            HeaderText = options.HeaderText,
            FooterText = options.FooterText + "\nMultiple selections allowed (separate with commas)"
        };

        if (modifiedOptions.ClearScreenBefore)
            ClearScreen();

        if (!string.IsNullOrEmpty(modifiedOptions.HeaderText))
            DisplayHeader(modifiedOptions.HeaderText);

        Console.WriteLine(modifiedOptions.Question);
        Console.WriteLine();

        // Display choices
        for (int i = 0; i < modifiedOptions.Choices.Count; i++)
        {
            var choice = modifiedOptions.Choices[i];
            var prefix = modifiedOptions.ShowNumbers ? $"{i + 1}. " : "- ";
            var display = $"{prefix}{choice.Display}";
            
            if (!string.IsNullOrEmpty(choice.Description))
                display += $" - {choice.Description}";
                
            Console.WriteLine(display);
        }

        if (modifiedOptions.AllowCancel)
        {
            var cancelIndex = modifiedOptions.Choices.Count + 1;
            var prefix = modifiedOptions.ShowNumbers ? $"{cancelIndex}. " : "- ";
            Console.WriteLine($"{prefix}{modifiedOptions.CancelText}");
        }

        Console.WriteLine();
        if (!string.IsNullOrEmpty(modifiedOptions.FooterText))
        {
            Console.WriteLine(modifiedOptions.FooterText);
            Console.WriteLine();
        }

        while (true)
        {
            Console.Write($"{modifiedOptions.PromptText}: ");
            var input = Console.ReadLine()?.Trim();
            
            if (string.IsNullOrEmpty(input))
                continue;

            if (modifiedOptions.AllowCancel && input.Equals("cancel", StringComparison.OrdinalIgnoreCase))
                return PromptResult<List<string>>.CreateCancelled();

            var selectedKeys = new List<string>();
            var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var isValid = true;

            foreach (var part in parts)
            {
                var trimmedPart = part.Trim();
                
                if (modifiedOptions.ShowNumbers && int.TryParse(trimmedPart, out var choice))
                {
                    if (choice >= 1 && choice <= modifiedOptions.Choices.Count)
                    {
                        var selectedChoice = modifiedOptions.Choices[choice - 1];
                        if (!selectedKeys.Contains(selectedChoice.Key))
                            selectedKeys.Add(selectedChoice.Key);
                    }
                    else if (modifiedOptions.AllowCancel && choice == modifiedOptions.Choices.Count + 1)
                    {
                        return PromptResult<List<string>>.CreateCancelled();
                    }
                    else
                    {
                        isValid = false;
                        break;
                    }
                }
                else
                {
                    var selectedChoice = modifiedOptions.Choices.FirstOrDefault(c => 
                        c.Key.Equals(trimmedPart, StringComparison.OrdinalIgnoreCase) ||
                        c.Display.Equals(trimmedPart, StringComparison.OrdinalIgnoreCase));
                        
                    if (selectedChoice != null)
                    {
                        if (!selectedKeys.Contains(selectedChoice.Key))
                            selectedKeys.Add(selectedChoice.Key);
                    }
                    else
                    {
                        isValid = false;
                        break;
                    }
                }
            }

            if (isValid && selectedKeys.Any())
                return PromptResult<List<string>>.CreateSuccess(selectedKeys);

            DisplayError("Invalid selection(s). Please try again.");
        }
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
        Console.WriteLine(options.Question);
        
        if (!string.IsNullOrEmpty(options.DefaultValue))
            Console.WriteLine($"Default: {options.DefaultValue}");

        while (true)
        {
            Console.Write($"{options.PromptText}: ");
            
            var input = options.IsPassword ? ReadPassword() : Console.ReadLine();
            
            if (string.IsNullOrEmpty(input))
            {
                if (!string.IsNullOrEmpty(options.DefaultValue))
                    return PromptResult<string>.CreateSuccess(options.DefaultValue);
                    
                if (options.AllowCancel)
                    return PromptResult<string>.CreateCancelled();
                    
                if (options.Required)
                {
                    DisplayError("This field is required.");
                    continue;
                }
                
                return PromptResult<string>.CreateSuccess(string.Empty);
            }

            if (options.AllowCancel && input.Equals("cancel", StringComparison.OrdinalIgnoreCase))
                return PromptResult<string>.CreateCancelled();

            if (options.MaxLength.HasValue && input.Length > options.MaxLength.Value)
            {
                DisplayError($"Input too long. Maximum length is {options.MaxLength.Value} characters.");
                continue;
            }

            if (!string.IsNullOrEmpty(options.ValidationPattern))
            {
                if (!Regex.IsMatch(input, options.ValidationPattern))
                {
                    DisplayError(options.ValidationMessage ?? "Invalid format.");
                    continue;
                }
            }

            return PromptResult<string>.CreateSuccess(input);
        }
    }

    public PromptResult<bool> ConfirmPrompt(ConfirmPromptOptions options)
    {
        var defaultIndicator = options.DefaultValue ? "(Y/n)" : "(y/N)";
        
        while (true)
        {
            Console.Write($"{options.Question} {options.PromptText} {defaultIndicator}: ");
            var input = Console.ReadLine()?.Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(input))
                return PromptResult<bool>.CreateSuccess(options.DefaultValue);

            if (options.AllowCancel && input == "cancel")
                return PromptResult<bool>.CreateCancelled();

            if (input is "y" or "yes" or "true" or "1")
                return PromptResult<bool>.CreateSuccess(true);
                
            if (input is "n" or "no" or "false" or "0")
                return PromptResult<bool>.CreateSuccess(false);

            DisplayError("Please enter y/yes or n/no.");
        }
    }

    public PromptResult<int> NumberPrompt(NumberPromptOptions options)
    {
        Console.WriteLine(options.Question);
        
        if (options.DefaultValue.HasValue)
            Console.WriteLine($"Default: {options.DefaultValue.Value}");

        if (options.MinValue.HasValue || options.MaxValue.HasValue)
        {
            var range = "";
            if (options.MinValue.HasValue && options.MaxValue.HasValue)
                range = $"Range: {options.MinValue.Value} - {options.MaxValue.Value}";
            else if (options.MinValue.HasValue)
                range = $"Minimum: {options.MinValue.Value}";
            else if (options.MaxValue.HasValue)
                range = $"Maximum: {options.MaxValue.Value}";
                
            Console.WriteLine(range);
        }

        while (true)
        {
            Console.Write($"{options.PromptText}: ");
            var input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input))
            {
                if (options.DefaultValue.HasValue)
                    return PromptResult<int>.CreateSuccess(options.DefaultValue.Value);
                    
                if (options.AllowCancel)
                    return PromptResult<int>.CreateCancelled();
                    
                if (options.Required)
                {
                    DisplayError("This field is required.");
                    continue;
                }
                
                return PromptResult<int>.CreateError("No value provided");
            }

            if (options.AllowCancel && input.Equals("cancel", StringComparison.OrdinalIgnoreCase))
                return PromptResult<int>.CreateCancelled();

            if (!int.TryParse(input, out var value))
            {
                DisplayError("Please enter a valid number.");
                continue;
            }

            if (options.MinValue.HasValue && value < options.MinValue.Value)
            {
                DisplayError($"Number must be at least {options.MinValue.Value}.");
                continue;
            }

            if (options.MaxValue.HasValue && value > options.MaxValue.Value)
            {
                DisplayError($"Number must be no more than {options.MaxValue.Value}.");
                continue;
            }

            return PromptResult<int>.CreateSuccess(value);
        }
    }

    // Display methods
    public void DisplayHeader(string title, char borderChar = '=')
    {
        var border = new string(borderChar, title.Length + 4);
        Console.WriteLine(border);
        Console.WriteLine($"  {title}  ");
        Console.WriteLine(border);
        Console.WriteLine();
    }

    public void DisplayMessage(string message, ConsoleColor? color = null)
    {
        if (color.HasValue)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color.Value;
            Console.WriteLine(message);
            Console.ForegroundColor = originalColor;
        }
        else
        {
            Console.WriteLine(message);
        }
    }

    public void DisplayError(string error)
    {
        DisplayMessage($"Error: {error}", ConsoleColor.Red);
    }

    public void DisplaySuccess(string message)
    {
        DisplayMessage(message, ConsoleColor.Green);
    }

    public void DisplayWarning(string warning)
    {
        DisplayMessage($"Warning: {warning}", ConsoleColor.Yellow);
    }

    public void ClearScreen()
    {
        Console.Clear();
    }


    private static string ReadPassword()
    {
        var password = "";
        ConsoleKeyInfo keyInfo;
        
        do
        {
            keyInfo = Console.ReadKey(true);
            
            if (keyInfo.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password = password[..^1];
                Console.Write("\b \b");
            }
            else if (!char.IsControl(keyInfo.KeyChar))
            {
                password += keyInfo.KeyChar;
                Console.Write("*");
            }
        } while (keyInfo.Key != ConsoleKey.Enter);
        
        Console.WriteLine();
        return password;
    }
}