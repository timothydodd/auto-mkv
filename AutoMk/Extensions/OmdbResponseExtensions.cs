using System;
using AutoMk.Models;

namespace AutoMk.Extensions;

/// <summary>
/// Extension methods for OMDB response validation
/// </summary>
public static class OmdbResponseExtensions
{
    /// <summary>
    /// Checks if an OMDB response is valid (has Response = "True")
    /// </summary>
    /// <param name="response">The OMDB response to validate</param>
    /// <returns>True if the response is valid, false otherwise</returns>
    public static bool IsValidOmdbResponse<T>(this T? response) where T : OmdbBaseResponse
    {
        return response?.Response?.Equals("True", StringComparison.OrdinalIgnoreCase) == true;
    }
    
    /// <summary>
    /// Checks if an OMDB season response is valid (has Response = "True")
    /// </summary>
    /// <param name="response">The OMDB season response to validate</param>
    /// <returns>True if the response is valid, false otherwise</returns>
    public static bool IsValidOmdbResponse(this OmdbSeasonResponse? response)
    {
        return response?.Response?.Equals("True", StringComparison.OrdinalIgnoreCase) == true;
    }
    
    /// <summary>
    /// Checks if a media identity is valid (has Response = "True")
    /// </summary>
    /// <param name="response">The media identity to validate</param>
    /// <returns>True if the response is valid, false otherwise</returns>
    public static bool IsValidOmdbResponse(this MediaIdentity? response)
    {
        return response?.Response?.Equals("True", StringComparison.OrdinalIgnoreCase) == true;
    }
    
    /// <summary>
    /// Checks if a confirmation info is valid (has Response = "True")
    /// </summary>
    /// <param name="response">The confirmation info to validate</param>
    /// <returns>True if the response is valid, false otherwise</returns>
    public static bool IsValidOmdbResponse(this ConfirmationInfo? response)
    {
        return response?.Response?.Equals("True", StringComparison.OrdinalIgnoreCase) == true;
    }
}