
# AutoMk

A .NET 9.0 console application that provides automated MakeMKV disc ripping with intelligent media identification and file organization.

## Features

- 🎬 **Automated Disc Ripping**: Continuously monitors CD/DVD/Blu-ray drives for inserted media
- 🔍 **Intelligent Media Identification**: Automatically identifies movies and TV series via OMDB API
- 📁 **Plex-Compatible Organization**: Organizes ripped files according to Plex naming conventions
- 📺 **Multi-Disc TV Series Support**: Handles multi-disc TV series with episode numbering continuity
- 🖥️ **Cross-Platform**: Supports both Windows and WSL environments
- 📊 **Progress Tracking**: Real-time progress reporting during ripping operations
- 🔄 **File Size Filtering**: Configurable size filtering to exclude unwanted content

## Prerequisites

- .NET 9.0 SDK
- MakeMKV (licensed version for Blu-ray support)
- OMDB API key (free tier available at [omdbapi.com](http://www.omdbapi.com/))
- CD/DVD/Blu-ray drive

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

The application will:
1. Monitor your optical drives for inserted discs
2. Automatically scan and rip eligible content
3. Identify the media using OMDB API
4. Organize files into appropriate directories
5. Eject the disc when complete

### Output Structure

- **Movies**: `Movies/{Title} ({Year})/{Title} ({Year}).mkv`
- **TV Series**: `TV Shows/{Series}/Season {##}/{Series} - S##E## - {Episode Title}.mkv`

## Architecture

AutoMk follows clean architecture principles with dependency injection:

- **Services**: Core business logic organized into focused, single-responsibility services
- **Interfaces**: All services implement interfaces for testability and loose coupling
- **Models**: Strongly-typed configuration and data models
- **Cross-Platform**: Platform-specific implementations for Windows and WSL

### Key Services

- `MakeMkvService`: Orchestrates MakeMKV operations
- `MediaIdentificationService`: OMDB API integration
- `MediaNamingService`: Generates Plex-compatible names
- `MediaStateManager`: Maintains state across disc sessions
- `DriveWatcher`: Monitors optical drives for media

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
