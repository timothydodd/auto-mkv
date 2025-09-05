using System;
using System.Collections.Generic;

namespace AutoMk.Models;

public class PromptChoice
{
    public string Key { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
    public string? Description { get; set; }
    public object? Value { get; set; }

    public PromptChoice() { }
    
    public PromptChoice(string key, string display, object? value = null, string? description = null)
    {
        Key = key;
        Display = display;
        Value = value ?? key;
        Description = description;
    }
}

public class PromptResult<T>
{
    public bool Success { get; set; }
    public T? Value { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Cancelled { get; set; }

    public static PromptResult<T> CreateSuccess(T value) => new() { Success = true, Value = value };
    public static PromptResult<T> CreateError(string error) => new() { Success = false, ErrorMessage = error };
    public static PromptResult<T> CreateCancelled() => new() { Success = false, Cancelled = true };
}

public class SelectPromptOptions
{
    public string Question { get; set; } = string.Empty;
    public List<PromptChoice> Choices { get; set; } = new();
    public bool IsMultiSelect { get; set; } = false;
    public bool AllowCancel { get; set; } = true;
    public string CancelText { get; set; } = "Cancel";
    public string PromptText { get; set; } = "Enter your choice";
    public bool ShowNumbers { get; set; } = true;
    public bool ClearScreenBefore { get; set; } = false;
    public string? HeaderText { get; set; }
    public string? FooterText { get; set; }
}

public class TextPromptOptions
{
    public string Question { get; set; } = string.Empty;
    public string? DefaultValue { get; set; }
    public bool Required { get; set; } = false;
    public bool AllowCancel { get; set; } = true;
    public string? ValidationPattern { get; set; }
    public string? ValidationMessage { get; set; }
    public bool IsPassword { get; set; } = false;
    public int? MaxLength { get; set; }
    public string PromptText { get; set; } = "Enter value";
}

public class ConfirmPromptOptions
{
    public string Question { get; set; } = string.Empty;
    public bool DefaultValue { get; set; } = false;
    public bool AllowCancel { get; set; } = true;
    public string YesText { get; set; } = "Yes";
    public string NoText { get; set; } = "No";
    public string PromptText { get; set; } = "(y/n)";
}

public class NumberPromptOptions
{
    public string Question { get; set; } = string.Empty;
    public int? DefaultValue { get; set; }
    public int? MinValue { get; set; }
    public int? MaxValue { get; set; }
    public bool Required { get; set; } = false;
    public bool AllowCancel { get; set; } = true;
    public string PromptText { get; set; } = "Enter number";
}