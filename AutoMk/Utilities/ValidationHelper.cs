using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace AutoMk.Utilities;

/// <summary>
/// Utility class for common validation operations
/// </summary>
public static class ValidationHelper
{
    /// <summary>
    /// Validates that the specified value is not null
    /// </summary>
    /// <typeparam name="T">The type of the value to validate</typeparam>
    /// <param name="value">The value to validate</param>
    /// <param name="parameterName">The name of the parameter (automatically inferred if not provided)</param>
    /// <returns>The validated value</returns>
    /// <exception cref="ArgumentNullException">Thrown when value is null</exception>
    [return: NotNull]
    public static T ValidateNotNull<T>([NotNull] T? value, [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        where T : class
    {
        return value ?? throw new ArgumentNullException(parameterName);
    }

    /// <summary>
    /// Validates that the specified string is not null or empty
    /// </summary>
    /// <param name="value">The string to validate</param>
    /// <param name="parameterName">The name of the parameter (automatically inferred if not provided)</param>
    /// <returns>The validated string</returns>
    /// <exception cref="ArgumentException">Thrown when value is null or empty</exception>
    [return: NotNull]
    public static string ValidateNotNullOrEmpty([NotNull] string? value, [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Value cannot be null or empty.", parameterName);
        return value;
    }

    /// <summary>
    /// Validates that the specified string is not null, empty, or whitespace
    /// </summary>
    /// <param name="value">The string to validate</param>
    /// <param name="parameterName">The name of the parameter (automatically inferred if not provided)</param>
    /// <returns>The validated string</returns>
    /// <exception cref="ArgumentException">Thrown when value is null, empty, or whitespace</exception>
    [return: NotNull]
    public static string ValidateNotNullOrWhiteSpace([NotNull] string? value, [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null, empty, or whitespace.", parameterName);
        return value;
    }
}