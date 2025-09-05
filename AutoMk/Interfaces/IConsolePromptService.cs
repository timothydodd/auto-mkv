using System;
using System.Collections.Generic;
using AutoMk.Models;

namespace AutoMk.Interfaces;

public interface IConsolePromptService
{
    // Single select prompt
    PromptResult<string> SelectPrompt(SelectPromptOptions options);
    PromptResult<T> SelectPrompt<T>(SelectPromptOptions options);
    
    // Multi-select prompt
    PromptResult<List<string>> MultiSelectPrompt(SelectPromptOptions options);
    PromptResult<List<T>> MultiSelectPrompt<T>(SelectPromptOptions options);
    
    // Text input prompt
    PromptResult<string> TextPrompt(TextPromptOptions options);
    
    // Yes/No confirmation prompt
    PromptResult<bool> ConfirmPrompt(ConfirmPromptOptions options);
    
    // Number input prompt
    PromptResult<int> NumberPrompt(NumberPromptOptions options);
    
    // Display methods
    void DisplayHeader(string title, char borderChar = '=');
    void DisplayMessage(string message, ConsoleColor? color = null);
    void DisplayError(string error);
    void DisplaySuccess(string message);
    void DisplayWarning(string warning);
    void ClearScreen();
}