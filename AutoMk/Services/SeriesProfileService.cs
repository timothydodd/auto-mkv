using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

public class SeriesProfileService : ISeriesProfileService
{
    private readonly ILogger<SeriesProfileService> _logger;
    private readonly string _profilesPath;
    private List<SeriesProfile> _profiles;

    public SeriesProfileService(ILogger<SeriesProfileService> logger)
    {
        _logger = ValidationHelper.ValidateNotNull(logger);
        _profilesPath = Path.Combine(Directory.GetCurrentDirectory(), "Profiles", "series_profiles.json");
        _profiles = new List<SeriesProfile>();
        LoadProfiles();
    }

    public async Task<SeriesProfile?> GetProfileAsync(string seriesTitle)
    {
        var profile = _profiles.FirstOrDefault(p => 
            p.SeriesTitle.Equals(seriesTitle, StringComparison.OrdinalIgnoreCase));

        return await Task.FromResult(profile);
    }

    public async Task<SeriesProfile> CreateOrUpdateProfileAsync(SeriesProfile profile)
    {
        // Validate that the profile has a non-empty series title
        if (string.IsNullOrWhiteSpace(profile?.SeriesTitle))
        {
            throw new ArgumentException("SeriesTitle cannot be null or empty when creating/updating a profile.", nameof(profile));
        }

        var existing = await GetProfileAsync(profile.SeriesTitle);
        
        if (existing != null)
        {
            // Update existing profile
            existing.MinEpisodeSizeGB = profile.MinEpisodeSizeGB;
            existing.MaxEpisodeSizeGB = profile.MaxEpisodeSizeGB;
            existing.TrackSortingStrategy = profile.TrackSortingStrategy;
            existing.DoubleEpisodeHandling = profile.DoubleEpisodeHandling;
            existing.DefaultStartingSeason = profile.DefaultStartingSeason;
            existing.DefaultStartingEpisode = profile.DefaultStartingEpisode;
            existing.UseAutoIncrement = profile.UseAutoIncrement;
            existing.LastModifiedDate = DateTime.Now;
            
            await SaveProfilesAsync();
            _logger.LogInformation($"Updated profile for series: {profile.SeriesTitle}");
            return existing;
        }
        else
        {
            // Create new profile
            profile.CreatedDate = DateTime.Now;
            profile.LastModifiedDate = DateTime.Now;
            _profiles.Add(profile);
            
            await SaveProfilesAsync();
            _logger.LogInformation($"Created new profile for series: {profile.SeriesTitle}");
            return profile;
        }
    }

    public async Task<List<SeriesProfile>> GetAllProfilesAsync()
    {
        return await Task.FromResult(_profiles.ToList());
    }

    public async Task DeleteProfileAsync(string seriesTitle)
    {
        var profile = await GetProfileAsync(seriesTitle);
        if (profile != null)
        {
            _profiles.Remove(profile);
            await SaveProfilesAsync();
            _logger.LogInformation($"Deleted profile for series: {seriesTitle}");
        }
    }

    private void LoadProfiles()
    {
        try
        {
            if (File.Exists(_profilesPath))
            {
                var json = File.ReadAllText(_profilesPath);
                var allProfiles = JsonSerializer.Deserialize<List<SeriesProfile>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<SeriesProfile>();
                
                // Filter out empty profiles and perform data integrity checks
                _profiles = allProfiles.Where(p => !string.IsNullOrWhiteSpace(p.SeriesTitle)).ToList();
                
                var removedCount = allProfiles.Count - _profiles.Count;
                if (removedCount > 0)
                {
                    _logger.LogWarning($"Removed {removedCount} empty/invalid profiles during load");
                    // Save the cleaned profiles back to disk
                    _ = Task.Run(SaveProfilesAsync);
                }
                
                _logger.LogInformation($"Loaded {_profiles.Count} valid series profiles");
            }
            else
            {
                _logger.LogInformation("No existing series profiles found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading series profiles");
            _profiles = new List<SeriesProfile>();
        }
    }

    private async Task SaveProfilesAsync()
    {
        try
        {
            // Ensure directory exists
            FileSystemHelper.EnsureFileDirectoryExists(_profilesPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(_profiles, options);
            await File.WriteAllTextAsync(_profilesPath, json);
            
            _logger.LogInformation($"Saved {_profiles.Count} series profiles");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving series profiles");
        }
    }

    public SeriesProfile CreateDefaultProfile(string seriesTitle)
    {
        // Validate that series title is not empty
        if (string.IsNullOrWhiteSpace(seriesTitle))
        {
            throw new ArgumentException("SeriesTitle cannot be null or empty when creating a default profile.", nameof(seriesTitle));
        }

        return new SeriesProfile
        {
            SeriesTitle = seriesTitle,
            MinEpisodeSizeGB = null, // Will use global defaults
            MaxEpisodeSizeGB = null,
            TrackSortingStrategy = TrackSortingStrategy.ByTrackOrder,
            DoubleEpisodeHandling = DoubleEpisodeHandling.AlwaysAsk,
            UseAutoIncrement = false
        };
    }
}