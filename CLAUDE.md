# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Dalamud plugin project for Final Fantasy XIV called "DartsTracker" that tracks darts game scores and statistics within the game.

## Architecture

### Core Components
- **Plugin.cs**: Main plugin entry point implementing `IDalamudPlugin` interface
- **Configuration.cs**: Plugin configuration handling with serialization
- **Windows/**: UI components using ImGui
  - `MainWindow.cs`: Primary plugin interface
  - `ConfigWindow.cs`: Configuration/settings interface
- **DartsTracker.json**: Plugin metadata for Dalamud

### Framework Integration
- Uses Dalamud.NET.Sdk (version 13.1.0) for FFXIV integration
- Dependency injection via `[PluginService]` attributes for Dalamud services
- Window system using `Dalamud.Interface.Windowing`
- Command registration through `ICommandManager`

## Development Commands

### Building
```bash
# Build debug version
dotnet build

# Build release version  
dotnet build --configuration Release
```

### Project Structure
- Debug builds: `DartsTracker/bin/x64/Debug/`
- Release builds: `DartsTracker/bin/x64/Release/`
- Solution file: `DartsTracker.sln`

### Testing in Game
1. Build the plugin (debug or release)
2. Use `/xlsettings` in FFXIV to access Dalamud settings
3. Add the plugin DLL path to Dev Plugin Locations under Experimental
4. Use `/xlplugins` to enable the plugin in Dev Tools

## Code Style

The project follows strict C# formatting rules defined in `.editorconfig`:
- 4-space indentation
- UTF-8 encoding with LF line endings
- PascalCase for public members, camelCase for private fields
- Comprehensive ReSharper styling rules
- Braces on new lines for all constructs

## Prerequisites

- .NET 9.0 SDK
- XIVLauncher and Dalamud installed
- FFXIV with Dalamud running at least once
- Optional: `DALAMUD_HOME` environment variable for custom Dalamud paths

## Plugin Commands

- `/darts`: Opens the main DartsTracker interface
- Plugin provides configuration window accessible through Dalamud plugin settings

## Plugin Features

- Main window: "Darts Tracker" - Primary interface for tracking darts games
- Configuration window: "Darts Tracker Configuration" - Settings and preferences