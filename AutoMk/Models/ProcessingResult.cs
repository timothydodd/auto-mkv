using System;
using System.Collections.Generic;

namespace AutoMk.Models;

/// <summary>
/// Represents the result of a processing operation with consistent error handling
/// </summary>
/// <typeparam name="T">The type of the result value</typeparam>
public class ProcessingResult<T>
{
    /// <summary>
    /// Indicates whether the operation was successful
    /// </summary>
    public bool IsSuccess { get; private set; }

    /// <summary>
    /// The result value if the operation was successful
    /// </summary>
    public T? Value { get; private set; }

    /// <summary>
    /// Error message if the operation failed
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Exception that caused the failure, if any
    /// </summary>
    public Exception? Exception { get; private set; }

    /// <summary>
    /// Additional context or warnings
    /// </summary>
    public List<string> Warnings { get; private set; } = new();

    /// <summary>
    /// Additional metadata about the operation
    /// </summary>
    public Dictionary<string, object> Metadata { get; private set; } = new();

    private ProcessingResult(bool isSuccess, T? value, string? errorMessage, Exception? exception)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
        Exception = exception;
    }

    /// <summary>
    /// Creates a successful result
    /// </summary>
    /// <param name="value">The result value</param>
    /// <returns>A successful ProcessingResult</returns>
    public static ProcessingResult<T> Success(T value)
    {
        return new ProcessingResult<T>(true, value, null, null);
    }

    /// <summary>
    /// Creates a successful result without a value (for void operations)
    /// </summary>
    /// <returns>A successful ProcessingResult</returns>
    public static ProcessingResult<T> Success()
    {
        return new ProcessingResult<T>(true, default, null, null);
    }

    /// <summary>
    /// Creates a failed result with an error message
    /// </summary>
    /// <param name="errorMessage">The error message</param>
    /// <returns>A failed ProcessingResult</returns>
    public static ProcessingResult<T> Failure(string errorMessage)
    {
        return new ProcessingResult<T>(false, default, errorMessage, null);
    }

    /// <summary>
    /// Creates a failed result with an exception
    /// </summary>
    /// <param name="exception">The exception that caused the failure</param>
    /// <returns>A failed ProcessingResult</returns>
    public static ProcessingResult<T> Failure(Exception exception)
    {
        return new ProcessingResult<T>(false, default, exception.Message, exception);
    }

    /// <summary>
    /// Creates a failed result with both an error message and exception
    /// </summary>
    /// <param name="errorMessage">The error message</param>
    /// <param name="exception">The exception that caused the failure</param>
    /// <returns>A failed ProcessingResult</returns>
    public static ProcessingResult<T> Failure(string errorMessage, Exception exception)
    {
        return new ProcessingResult<T>(false, default, errorMessage, exception);
    }

    /// <summary>
    /// Adds a warning to the result
    /// </summary>
    /// <param name="warning">The warning message</param>
    /// <returns>This ProcessingResult for chaining</returns>
    public ProcessingResult<T> WithWarning(string warning)
    {
        Warnings.Add(warning);
        return this;
    }

    /// <summary>
    /// Adds metadata to the result
    /// </summary>
    /// <param name="key">The metadata key</param>
    /// <param name="value">The metadata value</param>
    /// <returns>This ProcessingResult for chaining</returns>
    public ProcessingResult<T> WithMetadata(string key, object value)
    {
        Metadata[key] = value;
        return this;
    }

    /// <summary>
    /// Maps the value of this result to a new type using the provided function
    /// </summary>
    /// <typeparam name="TNew">The new result type</typeparam>
    /// <param name="mapper">Function to map the value</param>
    /// <returns>A new ProcessingResult with the mapped value</returns>
    public ProcessingResult<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        if (!IsSuccess)
        {
            return ProcessingResult<TNew>.Failure(ErrorMessage!, Exception);
        }

        try
        {
            var newValue = mapper(Value!);
            var newResult = ProcessingResult<TNew>.Success(newValue);
            newResult.Warnings.AddRange(Warnings);
            foreach (var kvp in Metadata)
            {
                newResult.Metadata[kvp.Key] = kvp.Value;
            }
            return newResult;
        }
        catch (Exception ex)
        {
            return ProcessingResult<TNew>.Failure(ex);
        }
    }

    /// <summary>
    /// Executes an action if the result is successful
    /// </summary>
    /// <param name="action">Action to execute with the value</param>
    /// <returns>This ProcessingResult for chaining</returns>
    public ProcessingResult<T> OnSuccess(Action<T> action)
    {
        if (IsSuccess && Value != null)
        {
            try
            {
                action(Value);
            }
            catch (Exception ex)
            {
                return Failure(ex);
            }
        }
        return this;
    }

    /// <summary>
    /// Executes an action if the result is a failure
    /// </summary>
    /// <param name="action">Action to execute with the error message</param>
    /// <returns>This ProcessingResult for chaining</returns>
    public ProcessingResult<T> OnFailure(Action<string> action)
    {
        if (!IsSuccess)
        {
            action(ErrorMessage ?? "Unknown error");
        }
        return this;
    }

    /// <summary>
    /// Returns the value if successful, otherwise returns the default value
    /// </summary>
    /// <param name="defaultValue">Default value to return if failed</param>
    /// <returns>The value or default</returns>
    public T GetValueOrDefault(T defaultValue = default!)
    {
        return IsSuccess ? Value! : defaultValue;
    }

    /// <summary>
    /// Returns the value if successful, otherwise throws an exception
    /// </summary>
    /// <returns>The value</returns>
    /// <exception cref="InvalidOperationException">Thrown if the result is not successful</exception>
    public T GetValueOrThrow()
    {
        if (!IsSuccess)
        {
            throw new InvalidOperationException(ErrorMessage ?? "Operation failed", Exception);
        }
        return Value!;
    }

    /// <summary>
    /// Implicit conversion from value to successful result
    /// </summary>
    /// <param name="value">The value to wrap</param>
    public static implicit operator ProcessingResult<T>(T value)
    {
        return Success(value);
    }

    /// <summary>
    /// Converts this result to a string representation
    /// </summary>
    /// <returns>String representation of the result</returns>
    public override string ToString()
    {
        if (IsSuccess)
        {
            return $"Success: {Value}";
        }
        else
        {
            return $"Failure: {ErrorMessage}";
        }
    }
}

/// <summary>
/// Non-generic ProcessingResult for operations that don't return a value
/// </summary>
public class ProcessingResult
{
    /// <summary>
    /// Indicates whether the operation was successful
    /// </summary>
    public bool IsSuccess { get; private set; }

    /// <summary>
    /// Error message if the operation failed
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Exception that caused the failure, if any
    /// </summary>
    public Exception? Exception { get; private set; }

    /// <summary>
    /// Additional context or warnings
    /// </summary>
    public List<string> Warnings { get; private set; } = new();

    /// <summary>
    /// Additional metadata about the operation
    /// </summary>
    public Dictionary<string, object> Metadata { get; private set; } = new();

    private ProcessingResult(bool isSuccess, string? errorMessage, Exception? exception)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Exception = exception;
    }

    /// <summary>
    /// Creates a successful result
    /// </summary>
    /// <returns>A successful ProcessingResult</returns>
    public static ProcessingResult Success()
    {
        return new ProcessingResult(true, null, null);
    }

    /// <summary>
    /// Creates a failed result with an error message
    /// </summary>
    /// <param name="errorMessage">The error message</param>
    /// <returns>A failed ProcessingResult</returns>
    public static ProcessingResult Failure(string errorMessage)
    {
        return new ProcessingResult(false, errorMessage, null);
    }

    /// <summary>
    /// Creates a failed result with an exception
    /// </summary>
    /// <param name="exception">The exception that caused the failure</param>
    /// <returns>A failed ProcessingResult</returns>
    public static ProcessingResult Failure(Exception exception)
    {
        return new ProcessingResult(false, exception.Message, exception);
    }

    /// <summary>
    /// Creates a failed result with both an error message and exception
    /// </summary>
    /// <param name="errorMessage">The error message</param>
    /// <param name="exception">The exception that caused the failure</param>
    /// <returns>A failed ProcessingResult</returns>
    public static ProcessingResult Failure(string errorMessage, Exception exception)
    {
        return new ProcessingResult(false, errorMessage, exception);
    }

    /// <summary>
    /// Adds a warning to the result
    /// </summary>
    /// <param name="warning">The warning message</param>
    /// <returns>This ProcessingResult for chaining</returns>
    public ProcessingResult WithWarning(string warning)
    {
        Warnings.Add(warning);
        return this;
    }

    /// <summary>
    /// Adds metadata to the result
    /// </summary>
    /// <param name="key">The metadata key</param>
    /// <param name="value">The metadata value</param>
    /// <returns>This ProcessingResult for chaining</returns>
    public ProcessingResult WithMetadata(string key, object value)
    {
        Metadata[key] = value;
        return this;
    }

    /// <summary>
    /// Executes an action if the result is successful
    /// </summary>
    /// <param name="action">Action to execute</param>
    /// <returns>This ProcessingResult for chaining</returns>
    public ProcessingResult OnSuccess(Action action)
    {
        if (IsSuccess)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                return Failure(ex);
            }
        }
        return this;
    }

    /// <summary>
    /// Executes an action if the result is a failure
    /// </summary>
    /// <param name="action">Action to execute with the error message</param>
    /// <returns>This ProcessingResult for chaining</returns>
    public ProcessingResult OnFailure(Action<string> action)
    {
        if (!IsSuccess)
        {
            action(ErrorMessage ?? "Unknown error");
        }
        return this;
    }

    /// <summary>
    /// Converts this result to a string representation
    /// </summary>
    /// <returns>String representation of the result</returns>
    public override string ToString()
    {
        if (IsSuccess)
        {
            return "Success";
        }
        else
        {
            return $"Failure: {ErrorMessage}";
        }
    }
}