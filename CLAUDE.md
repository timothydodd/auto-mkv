# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

AutoMk is a .NET 10.0 console application that provides automated MakeMKV disc ripping with intelligent media identification and file organization. It continuously monitors CD/DVD/Blu-ray drives, automatically rips content using MakeMKV, identifies movies and TV series via OMDB API, and organizes files according to Plex naming conventions.

## Build and Run Commands

```bash
# Build the project
$HOME/.dotnet/dotnet build

# Run the application
$HOME/.dotnet/dotnet run

# Restore NuGet packages
$HOME/.dotnet/dotnet restore

# Build for specific configuration
$HOME/.dotnet/dotnet build -c Release
$HOME/.dotnet/dotnet build -c wsl

# Publish for deployment
$HOME/.dotnet/dotnet publish -c Release -r win-x64

# Run tests (when available)
$HOME/.dotnet/dotnet test

# Build and run the MakeMKV emulator (for testing)
cd MakeMkvEmulator
$HOME/.dotnet/dotnet build
cp examples/quick-test-example.json bin/Debug/net9.0/emulator-config.json
$HOME/.dotnet/dotnet run -- -r --progress=-same info
```

## MakeMKV Emulator

The project includes a MakeMKV emulator (`MakeMkvEmulator/`) for testing without physical discs or a MakeMKV license.

**Purpose:**
- Enables development and testing without physical optical drives
- Allows configurable disc sequences with custom tracks and properties
- Simulates realistic ripping progress and timing
- Perfect for CI/CD pipelines and rapid development

**Key Features:**
- Mimics `makemkvcon64` command-line interface exactly
- Configurable via JSON (disc name, track sizes, durations, rip times)
- Stateful - remembers current disc position across runs
- Auto-advances through disc sequence after ripping completes
- Creates dummy output files matching configured specifications

**Configuration:**
- Main config: `emulator-config.json` (defines disc sequence)
- State file: `emulator-state.json` (tracks current disc position)
- Three example configs provided:
  - `tv-series-example.json`: Multi-disc TV series (Breaking Bad S1)
  - `movie-example.json`: Movies with bonus features
  - `quick-test-example.json`: Fast testing (3-5s rip times)

**Usage:**
1. Build: `cd MakeMkvEmulator && dotnet build`
2. Copy config: `cp examples/quick-test-example.json bin/Debug/net9.0/emulator-config.json`
3. Point AutoMk to emulator: Set `RipSettings.MakeMKVPath` to emulator binary
4. Run AutoMk normally - it will use the emulated discs

**Output Format:**
Matches real MakeMKV output exactly:
- `DRV:` lines for drive information
- `TINFO:` lines for title properties
- `PRGV:`/`PRGC:` lines for progress updates
- `MSG:` lines for status messages

See `MakeMkvEmulator/README.md` for detailed documentation.

## Architecture Overview

### Core Service Pattern
The application uses .NET Generic Host with dependency injection and interface-based architecture. All services implement proper interfaces for testability and loose coupling:

**Main Services:**
- **MakeMkAuto**: Main background service orchestrating the entire workflow
- **DriveWatcher**: Hardware monitoring (Windows/WSL variants)

**MakeMKV Services:**
- **IMakeMkvService**: Main interface for MakeMKV operations (implemented by MakeMkvService)
- **MakeMkvProcessManager**: Handles process creation and execution
- **MakeMkvOutputParser**: Parses MakeMKV command output
- **MakeMkvProgressReporter**: Progress tracking and reporting

**Media Processing Services:**
- **IMediaIdentificationService**: OMDB API integration for content identification
- **IMediaNamingService**: Plex-compatible naming generation
- **IMediaStateManager**: Cross-disc state persistence for TV series
- **IMediaMoverService**: Post-processing file organization
- **IOmdbClient**: OMDB API client interface
- **IFileTransferClient**: HTTP-based remote file transfer
- **IFileDiscoveryService**: File discovery and verification for ripped media
- **IPatternLearningService**: Machine learning for user selection patterns
- **IMediaSelectionService**: Interactive media search and selection UI
- **ISeriesConfigurationService**: TV series configuration management UI
- **IBatchRenameService**: Centralized file renaming operations

**Console & Progress Services:**
- **IConsolePromptService**: Comprehensive console UI framework
- **IConsoleOutputService**: Structured console output with styling
- **IProgressManager**: Progress tracking and display management

### Project Structure
```
AutoMk/
├── MakeMkAuto.cs             # Main background service
├── DriveWatcher.cs           # Windows drive monitoring
├── DriveWatcher_wsl.cs       # WSL drive monitoring
├── PostMKVJob.cs             # Post-rip job processing
├── Program.cs                # Application entry point
├── Models/                   # Data classes and DTOs
│   ├── ConfigurationModels.cs
│   ├── ConfirmationInfo.cs
│   ├── ConsolePromptModels.cs
│   ├── EpisodeDetails.cs
│   ├── MakeMkvModels.cs
│   ├── MediaDetails.cs
│   ├── MediaIdentity.cs
│   ├── MediaModels.cs          # SeriesProfile, SeriesState, TrackSortingStrategy
│   ├── MovieInfo.cs
│   ├── OmdbModels.cs
│   ├── PendingRenameModels.cs
│   ├── ProcessingResult.cs     # Consistent error handling wrapper
│   ├── ProgressModels.cs
│   ├── SearchResult.cs
│   └── SeriesInfo.cs
├── Interfaces/               # Service interfaces
│   ├── IBatchRenameService.cs
│   ├── IChapterConfigurable.cs
│   ├── IConsoleOutputService.cs
│   ├── IConsolePromptService.cs
│   ├── IDiscoverAndNameService.cs
│   ├── IEnhancedOmdbService.cs
│   ├── IFileDiscoveryService.cs
│   ├── IFileTransferClient.cs
│   ├── IMakeMkvService.cs
│   ├── IMediaIdentificationService.cs
│   ├── IMediaMoverService.cs
│   ├── IMediaNamingService.cs
│   ├── IMediaSelectionService.cs
│   ├── IMediaStateManager.cs
│   ├── IOmdbClient.cs
│   ├── IPatternLearningService.cs
│   ├── IProgressManager.cs
│   ├── ISeasonInfoCacheService.cs
│   ├── ISeriesConfigurationService.cs
│   ├── ISeriesProfileService.cs
│   └── ISizeConfigurable.cs
├── Services/                 # Service implementations
│   ├── BatchRenameService.cs
│   ├── ConsoleInteractionService.cs
│   ├── ConsoleOutputService.cs
│   ├── ConsolePromptService.cs
│   ├── DiscoverAndNameService.cs
│   ├── EnhancedOmdbService.cs
│   ├── FileDiscoveryService.cs
│   ├── FileTransferClient.cs
│   ├── MakeMkvOutputParser.cs
│   ├── MakeMkvProcessManager.cs
│   ├── MakeMkvProgressReporter.cs
│   ├── MakeMkvService.cs
│   ├── ManualModeService.cs
│   ├── MediaIdentificationService.cs
│   ├── MediaMoverService.cs
│   ├── MediaNamingService.cs
│   ├── MediaSelectionService.cs
│   ├── MediaStateManager.cs
│   ├── OmdbClient.cs
│   ├── PatternLearningService.cs
│   ├── ProgressManager.cs
│   ├── SeasonInfoCacheService.cs
│   ├── SeriesConfigurationService.cs
│   └── SeriesProfileService.cs
├── Utilities/                # Helper classes
│   ├── DiscNameUtility.cs      # Disc name parsing
│   ├── FileSystemHelper.cs     # File system operations
│   ├── ModelConverter.cs       # Model transformations
│   └── ValidationHelper.cs     # Input validation
├── Extensions/               # Extension methods
│   └── OmdbResponseExtensions.cs
└── [Runtime directories: Cache/, Profiles/, Logs/, state/]
```

### Configuration Structure
All settings are defined in strongly-typed models located in `Models/ConfigurationModels.cs`:
- **RipSettings**: MakeMKV configuration, output paths, size filtering (MinSizeGB/MaxSizeGB), chapter filtering (MinChapters/MaxChapters), MinTrackLength (in seconds), mode selection, media processing options
- **OmdbSettings**: OMDB API credentials and endpoints
- **FileTransferSettings**: Remote file transfer configuration (URL, timeouts, concurrent transfers)
- **PostRipSettings**: Post-processing cleanup options including DeleteFilesSmallerThan setting
- **ModeSelectionSetting**: Enum for startup mode (Ask, AlwaysManual, AlwaysAutomatic)
- **Logging**: File and console logging configuration with rotation and size limits

User secrets are used in DEBUG mode for sensitive configuration like API keys (User Secrets ID: `03359cd6-bc97-47ea-95e3-ed36c00c2be0`). All configuration classes include proper validation and null-checking.

### TV Series Intelligence
The application has sophisticated logic for handling multi-disc TV series:
- Parses disc names to extract series/season/disc information
- Maintains episode numbering state across multiple discs
- Handles various disc naming patterns (e.g., `Series_S8_D1_BD`)
- Stores persistent state in `MediaStateManager` for session continuity

### Pattern Learning System
The application includes machine learning capabilities for TV series processing with the `UserConfirmed` track sorting strategy:

**Core Components:**
- **PatternLearningService**: Records and analyzes user episode confirmation choices
- **TrackSelectionPattern**: Data model for storing user selection history
- **EpisodeTrackPattern**: Learned patterns with confidence scores
- **TrackToEpisodeMapping**: Individual track-to-episode associations

**How It Works:**
1. **Recording Phase**: When users confirm or modify episode suggestions, the system records:
   - Track position and episode number selected
   - Whether the user accepted or changed the suggestion
   - Confidence scores based on user behavior
2. **Learning Phase**: After processing each disc, patterns are analyzed and stored:
   - Track position patterns within the same series and season
   - Confidence scores updated based on user acceptance/rejection
   - Only applies to series using `TrackSortingStrategy.UserConfirmed`
3. **Suggestion Phase**: For subsequent discs in the same series/season:
   - Suggests episode numbers based on learned patterns
   - Only provides suggestions above minimum confidence threshold (0.7)
   - Falls back to standard numbering if confidence is low

**Pattern Matching Strategy:**
- Patterns are series-specific and season-specific only
- Does not rely on disc name patterns (since UserConfirmed is about user choices, not disc structure)
- Confidence increases when users accept suggestions, decreases when they modify them
- Requires minimum 2 samples to establish reliable patterns

### Cross-Platform Considerations
- **DriveWatcher.cs**: Windows-specific implementation using WinMM API
- **DriveWatcher_wsl.cs**: Alternative for WSL environments
- Conditional compilation with `#if !WSL` directives
- Native library `libcdrom.so` for Linux CD/DVD access

## Key Workflows

### Main Processing Pipeline
1. Drive monitoring detects inserted disc
2. **Early media type prediction** based on disc name patterns
3. MakeMKV scans disc for titles
4. **Pre-identification** with user confirmation (if needed)
5. **Consolidated TV series configuration** (for new series)
6. **Pattern learning suggestions** (for UserConfirmed sorting strategy)
7. Size-based filtering with series-specific or global settings
8. **Pre-rip confirmation summary** showing all settings
9. Ripping to temporary location
10. **File discovery** and verification of ripped content
11. OMDB API lookup for episode details (if not pre-identified)
12. Plex-compatible file/folder naming
13. File organization or remote transfer
14. **Pattern learning updates** based on user selections
15. Drive ejection when complete

### User Interaction Workflows

#### Startup Mode Selection
Users choose between three modes at startup:
- **Automatic Mode**: Minimal interaction, uses saved profiles
- **Manual Mode**: Full control over track selection and naming
- **Discover and Name Mode**: Organize existing MKV files without ripping

#### Mode 1: Automatic Mode (Minimal Prompts)
**Purpose:** Hands-off continuous disc monitoring and processing

**Process Flow:**
1. **Disc Detection** → MakeMKV scans disc for titles
2. **Pre-Identify Media** → OMDB API lookup based on disc name, optional confirmation
3. **TV Series Configuration** (new series only):
   - If existing profile/state exists → use saved settings
   - If new series → prompt for complete profile (episode size range, track sorting, double episode handling, auto-increment)
4. **Track Selection** (automatic):
   - Movies: Largest track only
   - TV Series: Filter by episode size range
5. **Pre-Rip Confirmation**:
   - Movies: Skipped (auto-proceed)
   - TV Series: Shows summary, can proceed/modify/skip (can enable "Don't ask again")
6. **Rip** → Process & Rename → File Transfer (if enabled) → Eject Disc

#### Mode 2: Manual Mode (More Prompts)
**Purpose:** Full user control over every decision

**Process Flow:**
1. **Disc Detection** → MakeMKV scans disc for titles
2. **Identify Media with Confirmation** → Shows OMDB results, user confirms or searches manually
3. **Media Type Selection** → If unclear, user chooses Movie or TV Series
4. **Track Selection** (user picks) → Displays all tracks in table, user multi-selects
5. **Content Mapping**:
   - **TV Series**: Episode Mapping → For each track: enter season (1-50) and episode (1-999)
   - **Multi-Movie Disc**: If multiple tracks selected in movie mode, each track is identified as a separate movie via OMDB search
6. **Rip Selected Tracks** → Process & Rename → Move to organized folders → Eject Disc

**Multi-Movie Disc Support:**
In Manual Mode, when the user selects multiple tracks and chooses movie mode:
- Each track is treated as a different movie
- User is prompted to identify each track via OMDB search
- Track name is used as default search term (cleaned)
- Each movie is processed and named individually
- Organized into separate movie folders following Plex naming conventions

#### Mode 3: Discover and Name Mode (File Organization)
**Purpose:** Organize existing MKV files without ripping

**Process Flow:**
1. **Enter Directory Path** → Search recursively for `*.mkv` files
2. **Display File Count**
3. **For Each File:**
   - Show: filename, size (GB), full path
   - "Does this file need to be identified?"
     - **YES**: Interactive OMDB search → generate Plex name → move to organized folder
     - **NO**: Option to move to file server with original name or skip
4. **Summary Table** → Total files, processed count, skipped count
5. **Return to Mode Selection** → Run Again / Mode Selection / Quit

#### TV Series Profile Configuration
When a new TV series is detected in Automatic Mode, all settings are configured upfront:
1. **Episode Size Range**: Custom min/max GB for filtering
2. **Track Sorting Strategy**: Three options available:
   - **ByTrackOrder**: Use MakeMKV's track numbering (Title #0, #1, #2...)
   - **ByMplsFileName**: Sort by source MPLS file names (00042.mpls, 00043.mpls...)
   - **UserConfirmed**: Sort by track order but confirm each episode with user (enables pattern learning)
3. **Double Episode Handling**: Auto-detect, always single, or always double
4. **Starting Position**: Season and episode numbers if unclear
5. **Auto-increment Mode**: For multi-disc series handling

These profiles are saved and reused for future discs from the same series.

#### Pre-Rip Confirmation
Before ripping begins in Automatic Mode, users see a summary of:
- Media title and type
- Number of tracks to rip
- Size filtering settings
- TV series-specific settings (sorting, episode handling)
- List of selected tracks

Users can proceed, modify settings, or skip the disc.

### File Organization Patterns
- **Movies**: `Movies/{Title} ({Year})/{Title} ({Year}).mkv`
- **TV Series**: `TV Shows/{Series}/Season {##}/{Series} - S##E## - {Episode Title}.mkv`

### Media Type Prediction
The system analyzes disc names via `DiscNameUtility` to predict media type:
- **TV Series Patterns**: S##D##, Season_#_Disc_#, Series names
- **Movie Patterns**: Year (19xx/20xx), quality indicators (1080p, BluRay)

This helps streamline the identification process by suggesting the likely media type.

## External Dependencies

### Required
- **MakeMKV**: Must be installed and licensed for disc ripping functionality
- **OMDB API Key**: Required for movie/TV series metadata (set in OmdbSettings.ApiKey)

### Runtime Environment
- Windows OS (for drive monitoring) or WSL
- Network access for OMDB API calls and optional file transfers
- CD/DVD/Blu-ray drive for disc detection

## Development Notes

### Clean Architecture Benefits
The refactored codebase now follows clean architecture principles:
- **Separation of Concerns**: Each service has a single responsibility
- **Dependency Inversion**: All services depend on interfaces, not concrete implementations
- **Testability**: Interfaces allow easy mocking for unit tests
- **Maintainability**: Smaller, focused classes are easier to understand and modify

### Key Improvements Made
- **MakeMkvCli.cs (635 lines) broken down into:**
  - `MakeMkvService.cs` - Main service implementing IMakeMkvService
  - `MakeMkvProcessManager.cs` - Process creation and execution
  - `MakeMkvOutputParser.cs` - Output parsing logic
  - `MakeMkvProgressReporter.cs` - Progress tracking
- **Models extracted to separate folder** with proper namespacing
- **All services now implement interfaces** for better IoC and testing
- **Nullable reference warnings eliminated** with proper null-checking
- **Configuration models centralized** with validation

### Enhanced User Experience Services
- **DiscNameUtility**: Analyzes disc names to extract series/season/disc info and predict media type
- **SeriesProfileService**: Manages saved TV series configurations
- **ConsoleInteractionService**: Unified console interaction with series profile prompts, pre-rip confirmation summaries, and error handling
- **BatchRenameService**: Centralized file renaming with batch operations and rollback support
- **ProgressManager**: Real-time progress tracking with visual feedback

### Console UI Framework
The application includes a comprehensive console UI framework for consistent user interaction:

**Core Components:**
- **ConsolePromptService**: Full-featured prompt service with multiple input types
- **ConsolePromptModels**: Strongly-typed prompt configuration classes
- **PromptResult<T>**: Consistent result wrapper for all user interactions

**Prompt Types Supported:**
- **Single/Multi-Select**: Choice-based prompts with numbered options
- **Text Input**: String input with validation, defaults, and password masking
- **Confirmation**: Yes/No prompts with customizable text and defaults
- **Number Input**: Integer input with min/max validation
- **Display Methods**: Headers, messages, errors, warnings, and screen management

**Features:**
- **Consistent Error Handling**: All prompts return `PromptResult<T>` with success/failure states
- **Cancellation Support**: Built-in cancel functionality for all prompt types
- **Validation**: Pattern-based validation for text inputs, range validation for numbers
- **Customization**: Configurable prompt text, display options, and formatting
- **Type Safety**: Generic prompt methods ensure type-safe results

### ProcessingResult Pattern
The application uses a consistent result pattern for error handling across all operations:

**Features:**
- **Success/Failure States**: Clear indication of operation outcomes
- **Value Wrapping**: Type-safe value storage with null handling
- **Error Information**: Detailed error messages and exception chaining
- **Warnings Collection**: Non-fatal warnings that don't break operations
- **Metadata Support**: Additional context information for operations
- **Fluent API**: Chainable methods for result manipulation and handling

**Usage Patterns:**
- **Map Operations**: Transform successful results to different types
- **Conditional Execution**: Execute actions only on success or failure
- **Default Values**: Provide fallback values for failed operations
- **Exception Handling**: Convert exceptions to structured error results
- **Implicit Conversion**: Automatic wrapping of values into successful results

### File Discovery Service
The application includes a robust file discovery system for locating ripped media files:

**Core Functionality:**
- **File Location**: Finds ripped files using multiple naming patterns from MakeMKV
- **Batch Discovery**: Processes multiple tracks simultaneously for efficiency
- **File Verification**: Validates file existence and accessibility
- **Size Calculation**: Provides file size information for filtering decisions
- **Cleanup Operations**: Removes temporary and incomplete files

**Discovery Strategies:**
- **Primary Pattern**: Matches MakeMKV's default naming conventions
- **Fallback Patterns**: Handles alternative naming when primary fails
- **Context-Aware**: Uses disc names and track information for better matching
- **Error Tolerance**: Continues processing even if some files aren't found

**Integration Points:**
- **Post-Rip Processing**: Locates files after MakeMKV operations complete
- **Media Organization**: Provides file paths for naming and moving operations
- **Size Filtering**: Enables size-based filtering before processing
- **Cleanup Workflows**: Maintains clean output directories

### Data Persistence
- **Media State**: JSON file tracking TV series progress and settings
- **Series Profiles**: Separate JSON storage for reusable series configurations
- **Manual Identifications**: Cached OMDB results for consistent naming
- **Season Cache**: Cached episode information to reduce OMDB API calls
- **Pattern Learning Data**: Stored within series state for user selection patterns

### OMDB API Optimization
- **Season-Level Caching**: Fetches entire seasons at once instead of individual episodes
- **30-Day Cache Expiration**: Balances freshness with performance
- **Intelligent Preloading**: Automatically preloads current and next season data
- **Fallback Support**: Falls back to individual episode lookup if season fetch fails
- **Memory + Disk Storage**: Fast memory access with persistent disk cache

### Error Handling Patterns
The codebase uses extensive null-checking due to nullable reference types being enabled. All configuration is validated at startup with meaningful error messages.

### Process Management
The application includes cleanup logic for existing `makemkvcon64` processes at startup to prevent conflicts.

### Logging
Comprehensive file and console logging is configured. Logs are written to the `Logs/` directory with daily rotation and size limits. All services use structured logging with proper log levels.

### Launch Profiles
The application includes two launch profiles for different environments:
- **AutoMk**: Standard Windows execution
- **WSL**: WSL2 distribution execution for Linux environments