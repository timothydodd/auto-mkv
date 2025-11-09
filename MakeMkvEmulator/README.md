# MakeMKV Emulator

A testing tool that emulates MakeMKV's command-line interface for development and testing of AutoMk without requiring physical discs or a MakeMKV license.

## Features

- Emulates MakeMKV's command-line interface (`makemkvcon64`)
- Configurable disc sequences with custom tracks
- Simulates ripping progress and duration
- Creates dummy output files
- Automatically advances through configured discs
- Stateful - remembers which disc is currently "inserted"

## Building

```bash
$HOME/.dotnet/dotnet build
```

The output binary is named `makemkvcon64` to match the real MakeMKV executable.

## Configuration

The emulator reads its configuration from `emulator-config.json` in the working directory. This file defines the sequence of discs to emulate and their properties.

### Configuration Structure

```json
{
  "discs": [
    {
      "name": "DISC_NAME",
      "titles": [
        {
          "id": "0",
          "name": "title_t00.mkv",
          "duration": "00:45:30",
          "sizeGB": 4.5,
          "chapterCount": 6,
          "sourceFileName": "00800.mpls",
          "ripDurationSeconds": 20
        }
      ]
    }
  ],
  "drive": {
    "id": "0",
    "driveName": "EMULATOR_DRIVE",
    "driveLetter": "E:"
  }
}
```

### Configuration Fields

**Disc Properties:**
- `name`: Disc label (shown as CDName in MakeMKV output)
- `titles`: Array of tracks/titles on the disc

**Title Properties:**
- `id`: Title/track ID (usually sequential: "0", "1", "2"...)
- `name`: Output filename for the ripped track
- `duration`: Track duration in HH:MM:SS format
- `sizeGB`: Track size in gigabytes
- `chapterCount`: Number of chapters in the track
- `sourceFileName`: (Optional) Source MPLS filename from the disc
- `ripDurationSeconds`: How long the simulated rip should take

**Drive Properties:**
- `id`: Drive ID (usually "0")
- `driveName`: Drive model/name
- `driveLetter`: Drive letter (Windows style, e.g., "E:")

## Example Configurations

The `examples/` directory contains several pre-configured scenarios:

### 1. TV Series Example (`tv-series-example.json`)
Simulates a multi-disc TV series (Breaking Bad Season 1):
- 3 discs with multiple episodes each
- Disc 1: 4 episodes
- Disc 2: 4 episodes
- Disc 3: 1 episode
- Realistic episode sizes (~4 GB each)

### 2. Movie Example (`movie-example.json`)
Simulates movie discs with bonus features:
- 2 movie discs (The Matrix, Inception)
- Main feature (~28-32 GB)
- Bonus features (smaller tracks)
- Longer rip times for larger files

### 3. Quick Test Example (`quick-test-example.json`)
Fast testing with short rip times:
- 2 TV series discs + 1 movie
- 3-5 second rip times for rapid testing
- Smaller file sizes

## Usage

### 1. Copy your chosen configuration

```bash
cp examples/quick-test-example.json emulator-config.json
```

Or create your own `emulator-config.json` file.

### 2. Configure AutoMk to use the emulator

Update your AutoMk configuration (appsettings.json or user secrets) to point to the emulator binary:

```json
{
  "RipSettings": {
    "MakeMKVPath": "/path/to/MakeMkvEmulator/bin/Debug/net9.0/makemkvcon64"
  }
}
```

On Windows:
```json
{
  "RipSettings": {
    "MakeMKVPath": "C:\\path\\to\\MakeMkvEmulator\\bin\\Debug\\net9.0\\makemkvcon64.exe"
  }
}
```

### 3. Run AutoMk

The emulator will automatically provide the configured discs in sequence. When AutoMk finishes processing a disc, the emulator advances to the next disc in the configuration.

### 4. Reset disc sequence

To reset back to the first disc, delete the state file:

```bash
rm emulator-state.json
```

## How It Works

1. **Disc Sequence**: The emulator maintains a list of discs and tracks which disc is currently "inserted"
2. **State Persistence**: Current disc index is saved to `emulator-state.json`
3. **Auto-Advance**: After the last title on a disc is ripped, the emulator automatically advances to the next disc
4. **Cycle Behavior**: After the last disc is processed, it wraps back to the first disc

## Command-Line Interface

The emulator mimics MakeMKV's command-line interface:

```bash
# List available drives
makemkvcon64 -r --progress=-same info

# Get disc information
makemkvcon64 -r --progress=-same info disc:0

# Rip a title
makemkvcon64 -r --progress=-same mkv disc:0 0 "/output/path"
```

## Output Format

The emulator generates output identical to MakeMKV:

**Drive Information:**
```
DRV:0,0,1,12,"BD-RE ASUS BC-12D2HT","Breaking_Bad_S1_D1_BD","E:"
```

**Title Information:**
```
TINFO:0,8,0,"6"           # Chapter count
TINFO:0,9,0,"00:48:15"    # Duration
TINFO:0,10,0,"4.2 GB"     # Size
TINFO:0,27,0,"title_t00.mkv"  # Name
```

**Progress Updates:**
```
PRGV:0,45,100,0,0         # Progress: 45% complete
PRGC:0,"45%","18.5 fps, avg 18.5 fps, 12s remaining"
```

## Testing Scenarios

### Test Multi-Disc TV Series Processing
Use `tv-series-example.json` to test:
- Episode numbering across multiple discs
- Series profile management
- Auto-increment episode tracking

### Test Movie Identification
Use `movie-example.json` to test:
- Movie vs TV detection
- OMDB lookup and naming
- Bonus feature filtering

### Rapid Development Testing
Use `quick-test-example.json` to test:
- Quick iteration during development
- Feature testing without long waits
- Edge case handling

## Troubleshooting

### "Configuration file not found"
Ensure `emulator-config.json` exists in the working directory where AutoMk runs the emulator.

### Disc not advancing
Check that AutoMk is ripping all titles. The emulator only advances after the last title on a disc is ripped.

### Reset to first disc
Delete `emulator-state.json` to reset the disc sequence.

## Development Notes

- The emulator creates dummy output files (capped at 1MB regardless of configured size)
- Files are created with the correct filename in the specified output directory
- Progress simulation uses a simple linear progression
- All output matches MakeMKV's actual output format for compatibility

## Creating Custom Configurations

1. Copy an existing example as a starting point
2. Modify disc names to match your test scenario
3. Adjust title counts, sizes, and durations
4. Set `ripDurationSeconds` based on your patience (3-5s for quick tests, 15-30s for realistic)
5. Ensure title IDs are sequential starting from "0"
6. Save as `emulator-config.json`

## Integration with AutoMk

The emulator is designed to be a drop-in replacement for MakeMKV:
- Same command-line interface
- Same output format
- Same exit codes (0 = success, 1 = failure)

Simply point AutoMk's `MakeMKVPath` setting to the emulator binary and AutoMk will work with it seamlessly.
