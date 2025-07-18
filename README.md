# CS2 Gameflow Control Plugin

A Counter-Strike 2 plugin that provides game flow control features including player ready system, match pause/unpause, and damage tracking.

## Features

### Player Ready System
- Players are automatically marked as ready when they spawn
- Manual ready/not ready commands
- Ready status tracking and broadcasting

### Match Control
- `!pause` - Pause the current match
- `!unpause` - Unpause the current match
- `!ready` - Mark yourself as ready
- `!notready` - Mark yourself as not ready
- `!status` - Check current ready status

### Damage Tracking
- Tracks damage dealt and taken per round
- Displays round summary to all players
- Only shows summary if player dealt or took damage

### Automatic Features
- Warmup timer pause enabled
- Ready status broadcast every 2 minutes
- Console logging for debugging

## Installation

1. **Prerequisites**
   - Counter-Strike 2 server with CounterStrikeSharp installed
   - .NET 7.0 SDK

2. **Build the Plugin**
   ```bash
   dotnet build --configuration Release
   ```

3. **Install**
   - Copy the built `GameFlowControl.dll` to your CS2 server's `addons/counterstrikesharp/plugins/` directory
   - Restart your CS2 server

## Commands

| Command | Description |
|---------|-------------|
| `!pause` | Pause the current match |
| `!unpause` | Unpause the current match |
| `!ready` | Mark yourself as ready |
| `!notready` | Mark yourself as not ready |
| `!status` | Check current ready status |

## Configuration

The plugin automatically:
- Enables warmup timer pause (`mp_warmup_pausetimer 1`)
- Broadcasts ready status every 2 minutes
- Tracks damage statistics per round

## Console Output

The plugin provides detailed console logging for debugging:
- Player connections/disconnections
- Ready status changes
- Match pause/unpause actions
- Plugin loading confirmation

## Requirements

- CounterStrikeSharp API v1.0.175+
- .NET 7.0 Runtime
- CS2 Server with CounterStrikeSharp addon

## Version

Current Version: 1.0
Author: Naranbat 