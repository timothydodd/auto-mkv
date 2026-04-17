# AutoMk

Automated MakeMKV disc ripping with OMDB-based media identification and Plex-compatible file organization. .NET 10.0 console app for Windows and WSL.

## What it does

1. Watches optical drives for inserted discs
2. Rips eligible titles with MakeMKV
3. Identifies the content via OMDB (movie or multi-disc TV series)
4. Renames and files output into a Plex-friendly structure
5. Ejects the disc and waits for the next one

## Prerequisites

- .NET 10.0 SDK
- OMDB API key — [omdbapi.com](http://www.omdbapi.com/)
- MakeMKV (licensed for Blu-ray) — *ripping modes only*
- CD/DVD/Blu-ray drive — *ripping modes only*

Discover and Name mode needs only .NET and an OMDB key.

## Setup

```bash
git clone https://github.com/yourusername/auto-mk.git
cd auto-mk
dotnet restore
dotnet build
dotnet run
```

Edit `appsettings.json`:

```json
{
  "Rip": {
    "MakeMkvPath": "path/to/makemkvcon64.exe",
    "TempPath": "path/to/temp",
    "OutputPath": "path/to/output",
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

For development, store the API key in user secrets:

```bash
dotnet user-secrets set "Omdb:ApiKey" "your-api-key"
```

## Modes

**Automatic** — Hands-off. Rips, identifies, organizes, ejects. TV series profiles are saved and reused across discs.

**Manual** — Confirm identification, pick tracks, map episodes yourself. No state reuse.

**TV Series Profile Configuration** — Pre-configure a series (episode size range, track sorting, double-episode handling, starting S/E, auto-increment). Sorting strategies:
- `ByTrackOrder` — MakeMKV's track numbering
- `ByMplsFileName` — source MPLS filename order
- `UserConfirmed` — confirm each episode (enables pattern learning)

**Discover and Name** — Point at a directory of existing MKVs. For each file: identify via OMDB and rename, move as-is to `_unidentified`, or skip. No disc or MakeMKV needed.

## Output layout

```
Movies/{Title} ({Year})/{Title} ({Year}).mkv
TV Shows/{Series}/Season {##}/{Series} - S##E## - {Episode Title}.mkv
_unidentified/{original-filename}.mkv
```

## Testing without a disc

`MakeMkvEmulator/` is a drop-in replacement for `makemkvcon64` that plays back a configurable disc sequence. Point `Rip.MakeMkvPath` at the emulator binary and run AutoMk normally.

```bash
cd MakeMkvEmulator
dotnet build
cp examples/quick-test-example.json bin/Debug/net9.0/emulator-config.json
```

Example configs: `tv-series-example.json`, `movie-example.json`, `quick-test-example.json`. See [MakeMkvEmulator/README.md](MakeMkvEmulator/README.md) for details.

## HandbrakeScheduler integration

AutoMk can POST completed rips to [HandbrakeScheduler](https://github.com/timothydodd/handbrake-scheduler) for transcoding, preserving folder structure and media metadata.

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

Flow: *Disc → AutoMk rip → OMDB identify → HTTP upload → HandBrake transcode*.

## Build targets

```bash
dotnet build -c Release
dotnet build -c wsl
dotnet publish -c Release -r win-x64
dotnet publish -c Release -r linux-x64
```

## License

MIT — see LICENSE.

## Acknowledgments

[MakeMKV](https://www.makemkv.com/) · [OMDB](http://www.omdbapi.com/) · [Plex](https://www.plex.tv/) · [HandbrakeScheduler](https://github.com/timothydodd/handbrake-scheduler)
