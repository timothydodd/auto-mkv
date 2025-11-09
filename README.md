
# AutoMk

A .NET 9.0 console application that provides automated MakeMKV disc ripping with intelligent media identification and file organization.

## Features

- 🎬 **Automated Disc Ripping**: Continuously monitors CD/DVD/Blu-ray drives for inserted media
- 🔍 **Intelligent Media Identification**: Automatically identifies movies and TV series via OMDB API
- 📁 **Plex-Compatible Organization**: Organizes ripped files according to Plex naming conventions
- 📺 **Multi-Disc TV Series Support**: Handles multi-disc TV series with episode numbering continuity
- 🗂️ **Discover and Name Mode**: Organize existing MKV files without ripping - perfect for cleaning up your media library
- 🖥️ **Cross-Platform**: Supports both Windows and WSL environments
- 📊 **Progress Tracking**: Real-time progress reporting during ripping operations
- 🔄 **File Size Filtering**: Configurable size filtering to exclude unwanted content
- ⚙️ **Multiple Operating Modes**: Automatic, Manual, TV Series Profile Configuration, and Discover & Name

## Prerequisites

- .NET 9.0 SDK
- OMDB API key (free tier available at [omdbapi.com](http://www.omdbapi.com/))
- MakeMKV (licensed version for Blu-ray support) - *Required for disc ripping modes only*
- CD/DVD/Blu-ray drive - *Required for disc ripping modes only*

**Note:** Discover and Name mode only requires .NET 9.0 and an OMDB API key - no MakeMKV or optical drive needed.

## Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/yourusername/auto-mk.git
   cd auto-mk
   ```

2. Restore dependencies:
   ```bash
   dotnet restore
   ```

3. Build the application:
   ```bash
   dotnet build
   ```

## Configuration

### Application Settings

Configure the application by editing `appsettings.json`:

```json
{
  "Rip": {
    "MakeMkvPath": "path/to/makemkvcon64.exe",
    "TempPath": "path/to/temp/directory",
    "OutputPath": "path/to/output/directory",
    "MinSizeGB": 3.0,
    "MaxSizeGB": 12.0,
    "MinTrackLength": "00:20:00"
  },
  "Omdb": {
    "ApiKey": "your-omdb-api-key",
    "ApiUrl": "http://www.omdbapi.com/"
  }
}
```

### User Secrets (Development)

For development, sensitive configuration like API keys can be stored in user secrets:

```bash
dotnet user-secrets set "Omdb:ApiKey" "your-api-key"
```

## Usage

Run the application:

```bash
dotnet run
```

### Operating Modes

AutoMk offers four operating modes to suit different workflows:

#### 1. Automatic Mode (Default)
The application will:
1. Monitor your optical drives for inserted discs
2. Automatically scan and rip eligible content
3. Identify the media using OMDB API
4. Organize files into appropriate directories
5. Eject the disc when complete

Features:
- Uses OMDB API for automatic identification
- Tracks TV series episode numbers across multiple discs
- Filters tracks by size settings
- Minimal user interaction required

#### 2. Manual Mode
Provides full control over the ripping process:
- User confirms all media identification
- User selects which tracks to rip
- User manually maps episodes for TV series
- State file is bypassed for fresh processing every time

#### 3. Configure TV Series Profiles
Pre-configure settings for TV series before ripping:
- Set episode size ranges (min/max GB)
- Choose track sorting strategy (MakeMKV order or MPLS filename)
- Configure double episode handling (auto-detect, always single, always double)
- Set starting season/episode numbers
- Enable auto-increment for multi-disc series

#### 4. Discover and Name Mode
Organize existing MKV files without ripping - perfect for cleaning up your media library:

**Workflow:**
1. Specify a directory containing MKV files
2. For each file found, choose to:
   - **Identify and name**: Search OMDB, identify as movie or TV series, rename with proper naming, and organize into Plex structure
   - **Move without naming**: Transfer to file server with original name (moved to "_unidentified" folder)
   - **Skip**: Leave the file as-is

**Features:**
- No disc or MakeMKV required
- Uses the same OMDB identification as ripping mode
- Generates Plex-compatible names and folder structure
- Supports both movies and TV series
- Integrates with file transfer to remote servers
- Overwrite protection for existing files
- Progress summary at completion

**Example Use Cases:**
- Organizing files ripped manually with MakeMKV
- Cleaning up a directory of renamed MKV files
- Batch processing media from other sources
- Preparing files for Plex import

### Output Structure

- **Movies**: `Movies/{Title} ({Year})/{Title} ({Year}).mkv`
- **TV Series**: `TV Shows/{Series}/Season {##}/{Series} - S##E## - {Episode Title}.mkv`
- **Unidentified Files**: `_unidentified/{original-filename}.mkv`

## Architecture

AutoMk follows clean architecture principles with dependency injection:

- **Services**: Core business logic organized into focused, single-responsibility services
- **Interfaces**: All services implement interfaces for testability and loose coupling
- **Models**: Strongly-typed configuration and data models
- **Cross-Platform**: Platform-specific implementations for Windows and WSL

### Key Services

- `MakeMkvService`: Orchestrates MakeMKV operations
- `MediaIdentificationService`: OMDB API integration and media processing
- `MediaNamingService`: Generates Plex-compatible names
- `MediaStateManager`: Maintains state across disc sessions
- `DriveWatcher`: Monitors optical drives for media
- `DiscoverAndNameService`: Organizes existing MKV files without ripping
- `MediaSelectionService`: Interactive media search and selection
- `SeriesConfigurationService`: TV series profile management
- `PatternLearningService`: Machine learning for user selection patterns

## Development

### Building

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release

# WSL build
dotnet build -c wsl
```

### Publishing

```bash
# Windows x64
dotnet publish -c Release -r win-x64

# Linux x64 (for WSL)
dotnet publish -c Release -r linux-x64
```

### Testing with MakeMKV Emulator

AutoMk includes a MakeMKV emulator for testing without requiring physical discs or a MakeMKV license. The emulator mimics MakeMKV's command-line interface and allows you to configure sequences of virtual discs with custom tracks.

**Features:**
- Emulates MakeMKV's command-line interface completely
- Configurable disc sequences with custom tracks, sizes, and durations
- Simulates realistic ripping progress and timing
- Automatically advances through multiple discs
- Perfect for development, testing, and CI/CD pipelines

**Quick Start:**

1. Build the emulator:
   ```bash
   cd MakeMkvEmulator
   dotnet build
   ```

2. Copy an example configuration:
   ```bash
   cp examples/quick-test-example.json emulator-config.json
   ```

3. Configure AutoMk to use the emulator:
   ```json
   {
     "Rip": {
       "MakeMkvPath": "/path/to/MakeMkvEmulator/bin/Debug/net9.0/makemkvcon64"
     }
   }
   ```

4. Run AutoMk - it will automatically use the emulated discs!

**Example configurations included:**
- `tv-series-example.json`: Multi-disc TV series (Breaking Bad S1)
- `movie-example.json`: Movies with bonus features (The Matrix, Inception)
- `quick-test-example.json`: Fast testing with 3-5 second rip times

See [MakeMkvEmulator/README.md](MakeMkvEmulator/README.md) for detailed documentation and configuration options.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Integration with HandbrakeScheduler

AutoMk can integrate with [HandbrakeScheduler](https://github.com/timothydodd/handbrake-scheduler) to create a complete media processing pipeline:

1. **AutoMk** rips discs using MakeMKV and identifies content via OMDB
2. **HandbrakeScheduler** receives the ripped files and transcodes them using HandBrake

### How It Works

AutoMk includes a `FileTransferClient` service that can automatically send ripped MKV files to HandbrakeScheduler's REST API after successful ripping. The integration preserves media metadata (movie/TV show information) throughout the pipeline.

### Enabling Integration

1. Configure AutoMk's `appsettings.json`:
   ```json
   {
     "Rip": {
       "EnableFileTransfer": true,
       "FileTransferSettings": {
         "Enabled": true,
         "TargetServiceUrl": "http://localhost:5000",
         "DeleteAfterTransfer": false
       }
     }
   }
   ```

2. Ensure HandbrakeScheduler is running and accessible at the configured URL

3. AutoMk will automatically:
   - Send completed MKV files to HandbrakeScheduler
   - Include relative file paths to maintain folder structure
   - Move processed files to a `_moved` subfolder (or delete if configured)

### Data Flow

```
[Disc Inserted] → [AutoMk Rips with MakeMKV] → [OMDB Identification] → 
[HTTP Upload to HandbrakeScheduler] → [HandBrake Transcoding] → [Final Output]
```

The integration maintains Plex-compatible folder structures throughout the process, ensuring your media library stays organized from disc to final transcoded file.

## Acknowledgments

- [MakeMKV](https://www.makemkv.com/) for the excellent disc ripping functionality
- [OMDB API](http://www.omdbapi.com/) for media metadata
- [Plex](https://www.plex.tv/) for the naming convention standards
- [HandbrakeScheduler](https://github.com/timothydodd/handbrake-scheduler) for automated transcoding integration
