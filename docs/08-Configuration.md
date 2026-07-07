# docs/08-Configuration.md

# OpenAudioLink Configuration System

Version: 1.0

---

# Overview

The configuration system centralizes all user-adjustable settings for both the Windows Receiver and the Android Sender.

Design goals:

- Human-readable
- Easy to back up
- Easy to migrate
- Backward compatible
- Hot-reload where possible
- Safe defaults

---

# Design Principles

The configuration system follows these principles:

1. Configuration is stored outside the executable.
2. Default values require no user intervention.
3. Invalid configuration never crashes the application.
4. New versions automatically fill missing fields.
5. Unknown fields are ignored to preserve forward compatibility.

---

# Configuration Scope

Configuration is divided into four categories:

```text
Application

↓

Network

↓

Audio

↓

User Interface
```

Each category is stored independently in memory, but persisted together.

---

# Windows Configuration Location

Recommended directory:

```text
%APPDATA%\OpenAudioLink\
```

Example:

```text
C:\Users\<User>\AppData\Roaming\OpenAudioLink\
```

Contents:

```text
config.json

receivers.json

trusted.json

logs/

cache/
```

Executable files must never modify their own installation directory.

---

# Android Configuration Location

Recommended storage:

```
Jetpack DataStore
```

Reason:

- Atomic updates
- Coroutine support
- Future-proof replacement for SharedPreferences

---

# Configuration File

Primary configuration file:

```
config.json
```

Encoding:

```
UTF-8
```

Formatting:

```
Pretty Printed
```

Indentation:

```
4 Spaces
```

---

# Configuration Version

Every configuration file contains:

```json
{
    "configVersion": 1
}
```

Purpose:

- Schema migration
- Compatibility checks
- Automatic upgrades

---

# Top-Level Structure

Recommended layout:

```json
{
    "configVersion": 1,

    "application": {},

    "network": {},

    "audio": {},

    "ui": {},

    "logging": {}
}
```

---

# Application Section

Controls global application behavior.

Example:

```json
{
    "application": {

        "startWithWindows": false,

        "minimizeToTray": true,

        "checkForUpdates": true,

        "language": "system"
    }
}
```

---

# Startup Options

Supported:

| Option | Default |
|--------|---------|
| startWithWindows | false |
| minimizeToTray | true |
| startHidden | false |
| singleInstance | true |

---

# Language

Supported values:

```text
system

en-US

zh-CN

ja-JP
```

Unknown languages fall back to:

```
system
```

---

# Network Section

Network-related configuration.

Example:

```json
{
    "network": {

        "tcpPort": 39888,

        "udpPort": 39887,

        "enableMdns": true,

        "enableBroadcast": true,

        "enableUnicast": true
    }
}
```

---

# Network Parameters

| Parameter | Default |
|-----------|---------:|
| tcpPort | 39888 |
| udpPort | 39887 |
| enableMdns | true |
| enableBroadcast | true |
| enableUnicast | true |
| allowVpnDiscovery | false |

---

# Discovery Timeout

Recommended:

```json
{
    "network": {

        "discoveryTimeoutMs": 3000,

        "offlineTimeoutMs": 30000
    }
}
```

---

# Audio Section

Controls playback behavior.

Example:

```json
{
    "audio": {

        "codec": "aac",

        "sampleRate": 48000,

        "channels": 2,

        "bufferMs": 100,

        "latencyMode": "balanced"
    }
}
```

---

# Audio Parameters

| Parameter | Default |
|-----------|---------:|
| codec | aac |
| sampleRate | 48000 |
| channels | 2 |
| bitDepth | 16 |
| bufferMs | 100 |

---

# Latency Modes

Supported values:

```text
low

balanced

stable
```

Mapping:

| Mode | Buffer |
|------|--------:|
| low | 50 ms |
| balanced | 100 ms |
| stable | 200 ms |

---

# Renderer Selection

Supported values:

```text
auto

wasapi

waveout
```

Default:

```
auto
```

The application selects the best available renderer automatically.

---

# Output Device

Default:

```json
{
    "audio": {

        "outputDevice": "default"
    }
}
```

Specific device:

```json
{
    "audio": {

        "outputDevice":
        "{DEVICE-GUID}"
    }
}
```

---

# Configuration Validation

Every configuration value must be validated before use.

Example:

```
bufferMs
```

Allowed range:

```
50 - 500
```

Values outside the range are replaced with defaults.

---

# User Interface Configuration

The UI configuration controls application appearance and interaction behavior.

Example:

```json
{
    "ui": {

        "theme": "system",

        "showNotifications": true,

        "minimizeToTray": true,

        "closeToTray": true,

        "showDebugPanel": false
    }
}
```

---

# Theme

Supported values:

```text
system

light

dark
```

Default:

```
system
```

---

# Tray Behavior

Recommended defaults:

| Option | Default |
|---------|---------|
| minimizeToTray | true |
| closeToTray | true |
| showTrayIcon | true |

---

# Notification Settings

Supported:

```json
{
    "ui": {

        "showConnectionNotification": true,

        "showDeviceNotification": true,

        "showErrorNotification": true
    }
}
```

---

# Window State

Persist between launches.

Example:

```json
{
    "ui": {

        "window": {

            "width": 980,

            "height": 720,

            "maximized": false
        }
    }
}
```

---

# Logging Configuration

Logging should be configurable without recompiling.

Example:

```json
{
    "logging": {

        "level": "Information",

        "writeFile": true,

        "writeConsole": false,

        "keepDays": 30
    }
}
```

---

# Log Levels

Supported values:

```text
Trace

Debug

Information

Warning

Error

Critical
```

Default:

```
Information
```

---

# Log File Location

Recommended:

```text
%APPDATA%\OpenAudioLink\logs\
```

File naming:

```text
2026-07-07.log

2026-07-08.log
```

Daily rolling logs simplify troubleshooting.

---

# Log Retention

Default:

```
30 days
```

Expired log files should be removed automatically during application startup.

---

# Receiver Cache

Previously discovered receivers are stored separately.

File:

```
receivers.json
```

Example:

```json
[
    {
        "id":
        "d91b9c54-a6c4-4f35-bc3d-9fd8a7b0a012",

        "name":
        "Office-PC",

        "lastAddress":
        "192.168.1.20",

        "lastSeen":
        "2026-07-07T08:20:00Z"
    }
]
```

---

# Receiver Cache Policy

Recommendations:

| Setting | Value |
|----------|------:|
| Maximum Entries | 100 |
| Expiration | 24 Hours |
| Save Interval | Immediate |

---

# Trusted Receiver Database

Future versions may maintain a list of trusted receivers.

File:

```
trusted.json
```

Example:

```json
[
    {
        "id":
        "d91b9c54-a6c4-4f35-bc3d-9fd8a7b0a012",

        "paired": true,

        "trusted": true
    }
]
```

Version 1 may leave this file empty.

---

# Android DataStore Mapping

Suggested structure:

```
Preferences

↓

Application

↓

Network

↓

Audio

↓

UI
```

Each logical group should be represented by a dedicated repository class.

---

# Android Repository Design

Recommended classes:

```text
ConfigurationRepository

↓

ApplicationSettings

↓

NetworkSettings

↓

AudioSettings

↓

UiSettings
```

The ViewModel communicates only with `ConfigurationRepository`.

---

# Windows Configuration Manager

Main class:

```
ConfigurationManager
```

Responsibilities:

- Load configuration
- Validate values
- Save changes
- Watch file changes
- Apply runtime updates

---

# ConfigurationManager Interface

```csharp
public interface IConfigurationManager
{
    AppConfiguration Load();

    void Save(AppConfiguration config);

    void Reload();

    event EventHandler<ConfigurationChangedEventArgs>
        ConfigurationChanged;
}
```

---

# Hot Reload

Some settings may be applied immediately.

Examples:

- Logging level
- UI theme
- Discovery timeout

Other settings require restart.

Examples:

- TCP listening port
- Audio renderer
- Sample rate

---

# Restart Required

When a restart is required:

```text
Configuration Changed

↓

Mark Restart Required

↓

Notify User

↓

Restart Later
```

The application should never restart itself automatically.

---

# Configuration Migration

Older configuration versions should migrate automatically.

Example:

```text
Version 1

↓

Version 2

↓

Add Missing Fields

↓

Save Updated File
```

Migration must preserve all valid user settings.

---

# AppConfiguration Model

The configuration system is centered around a single immutable configuration object.

Recommended class:

```csharp
public sealed class AppConfiguration
{
    public int ConfigVersion { get; init; }

    public ApplicationConfiguration Application { get; init; }

    public NetworkConfiguration Network { get; init; }

    public AudioConfiguration Audio { get; init; }

    public UiConfiguration Ui { get; init; }

    public LoggingConfiguration Logging { get; init; }
}
```

Each sub-configuration should be independently validated.

---

# ApplicationConfiguration

```csharp
public sealed class ApplicationConfiguration
{
    public bool StartWithWindows { get; init; }

    public bool StartHidden { get; init; }

    public bool MinimizeToTray { get; init; }

    public bool SingleInstance { get; init; }

    public bool CheckForUpdates { get; init; }

    public string Language { get; init; }
}
```

---

# NetworkConfiguration

```csharp
public sealed class NetworkConfiguration
{
    public int TcpPort { get; init; }

    public int UdpPort { get; init; }

    public bool EnableMdns { get; init; }

    public bool EnableBroadcast { get; init; }

    public bool EnableUnicast { get; init; }

    public bool AllowVpnDiscovery { get; init; }

    public int DiscoveryTimeoutMs { get; init; }

    public int OfflineTimeoutMs { get; init; }
}
```

---

# AudioConfiguration

```csharp
public sealed class AudioConfiguration
{
    public string Codec { get; init; }

    public int SampleRate { get; init; }

    public int Channels { get; init; }

    public int BitDepth { get; init; }

    public int BufferMs { get; init; }

    public string LatencyMode { get; init; }

    public string Renderer { get; init; }

    public string OutputDevice { get; init; }
}
```

---

# UiConfiguration

```csharp
public sealed class UiConfiguration
{
    public string Theme { get; init; }

    public bool ShowNotifications { get; init; }

    public bool ShowTrayIcon { get; init; }

    public bool CloseToTray { get; init; }

    public bool ShowDebugPanel { get; init; }

    public WindowConfiguration Window { get; init; }
}
```

---

# WindowConfiguration

```csharp
public sealed class WindowConfiguration
{
    public int Width { get; init; }

    public int Height { get; init; }

    public bool Maximized { get; init; }
}
```

---

# LoggingConfiguration

```csharp
public sealed class LoggingConfiguration
{
    public string Level { get; init; }

    public bool WriteFile { get; init; }

    public bool WriteConsole { get; init; }

    public int KeepDays { get; init; }
}
```

---

# JSON Schema Guidelines

The configuration file should follow these rules:

- UTF-8 encoding
- UTF-8 without BOM preferred
- Indented formatting
- Stable property ordering

Property ordering:

```text
ConfigVersion

Application

Network

Audio

Ui

Logging
```

Stable ordering minimizes unnecessary version control diffs.

---

# Default Configuration Factory

The application should never construct configuration objects manually.

Use:

```csharp
ConfigurationFactory.CreateDefault()
```

Responsibilities:

- Populate all default values
- Guarantee valid configuration
- Avoid null properties

---

# Default Values

Example:

```csharp
BufferMs = 100

SampleRate = 48000

Channels = 2

Renderer = "auto"

Theme = "system"
```

These defaults should exist in one location only.

---

# Configuration Validator

Before applying a configuration:

```text
Load JSON

↓

Deserialize

↓

Validate

↓

Normalize

↓

Apply
```

Validation failures should not prevent startup.

---

# Validation Rules

Examples:

| Field | Rule |
|--------|------|
| TcpPort | 1–65535 |
| UdpPort | 1–65535 |
| BufferMs | 50–500 |
| SampleRate | 44100 or 48000 |
| Channels | 1 or 2 |
| BitDepth | 16 |

Invalid values are replaced with defaults.

---

# Validation Result

Suggested model:

```csharp
public sealed class ValidationResult
{
    public bool Success { get; init; }

    public IReadOnlyList<string> Warnings { get; init; }

    public IReadOnlyList<string> Errors { get; init; }
}
```

Warnings should be logged but should not stop the application.

---

# Backup Strategy

Before saving:

```text
config.json

↓

config.json.bak

↓

Write New File
```

If writing fails:

```text
Restore Backup
```

This prevents data loss caused by interrupted writes.

---

# Atomic Save

Recommended process:

```text
Write

↓

config.tmp

↓

Flush

↓

Rename

↓

config.json
```

Avoid writing directly to the active configuration file.

---

# Import Configuration

Supported source:

```
config.json
```

Workflow:

```text
Select File

↓

Validate

↓

Preview

↓

Import

↓

Restart If Required
```

---

# Export Configuration

Export should include:

- config.json
- receivers.json
- trusted.json (future)

Logs should not be exported by default.

---

# Configuration Hot Reload

The configuration system should support runtime updates whenever possible.

Not every setting requires an application restart.

The goal is to apply changes safely while minimizing service interruption.

---

# Reload Workflow

Recommended workflow:

```text
Configuration File Changed

↓

Read File

↓

Deserialize

↓

Validate

↓

Compare

↓

Generate Change Set

↓

Apply Changes

↓

Notify Modules
```

---

# File Monitoring

Windows implementation:

```
FileSystemWatcher
```

Monitor:

```
config.json
```

Events:

- Changed
- Created
- Renamed

Ignore:

- Deleted

Deletion should be handled during the next configuration access.

---

# Debounce

Some editors generate multiple write events.

Recommended debounce:

```
500 ms
```

Workflow:

```text
Changed

↓

Restart Timer

↓

No Further Changes

↓

Reload Configuration
```

This avoids unnecessary repeated reloads.

---

# ConfigurationChanged Event

Suggested event:

```csharp
public sealed class ConfigurationChangedEventArgs
{
    public AppConfiguration Previous { get; }

    public AppConfiguration Current { get; }

    public IReadOnlyList<string> ChangedKeys { get; }
}
```

---

# Change Detection

Configuration comparison should occur by section.

Example:

```text
Application

Network

Audio

UI

Logging
```

Only modified sections should notify subscribers.

---

# Runtime Reload Matrix

| Configuration | Hot Reload | Restart Required |
|--------------|:----------:|:----------------:|
| Logging Level | ✔ | |
| Theme | ✔ | |
| Notifications | ✔ | |
| Discovery Timeout | ✔ | |
| mDNS Enable | ✔ | |
| Broadcast Enable | ✔ | |
| TCP Port | | ✔ |
| UDP Port | | ✔ |
| Renderer | | ✔ |
| Sample Rate | | ✔ |

---

# Applying UI Changes

Example:

```text
Theme Changed

↓

Update Resource Dictionary

↓

Refresh UI
```

No application restart required.

---

# Applying Logging Changes

Example:

```text
Logging Level

↓

Reconfigure Logger

↓

Continue Logging
```

Log files remain open unless the output destination changes.

---

# Applying Discovery Changes

When discovery settings change:

```text
Disable mDNS

↓

Stop mDNS Service

↓

Apply Settings

↓

Restart Discovery
```

Streaming sessions should remain unaffected.

---

# Audio Configuration Changes

Changes to:

- Renderer
- Sample Rate
- Output Device

cannot be safely applied while streaming.

Workflow:

```text
Configuration Changed

↓

Mark Restart Required

↓

Notify User
```

---

# Restart Notification

Example UI message:

```
Some changes will take effect after restarting OpenAudioLink.
```

The application should not force an immediate restart.

---

# Immutable Configuration

`AppConfiguration` should be immutable.

Reasons:

- Thread safety
- Predictable behavior
- Easier debugging

Modules should replace the entire configuration object instead of mutating individual properties.

---

# Configuration Snapshot

Every subsystem keeps its own snapshot.

Example:

```text
ConfigurationManager

↓

Current Configuration

↓

AudioManager Snapshot

↓

DiscoveryManager Snapshot

↓

UiManager Snapshot
```

Snapshots are replaced atomically when configuration changes.

---

# Thread Safety

Access pattern:

```text
Read

↓

Immutable Object

↓

No Lock Required
```

Only the configuration manager performs writes.

---

# Android DataStore

Android uses Kotlin `Flow`.

Example:

```text
DataStore

↓

Flow<AppConfiguration>

↓

Repository

↓

ViewModel

↓

Compose UI
```

Every collector receives updated configuration automatically.

---

# Repository Pattern

Android architecture:

```text
ConfigurationRepository

↓

SettingsViewModel

↓

Composable Screen
```

The UI must never access DataStore directly.

---

# Configuration Persistence

Save operation:

```text
Memory

↓

Serialize

↓

Temporary File

↓

Replace Existing File
```

The in-memory configuration remains the authoritative copy.

---

# Configuration Errors

If configuration loading fails:

```text
Read File

↓

Deserialize Failed

↓

Restore Backup

↓

Load Defaults

↓

Notify User
```

The application must always start with a valid configuration.

---

# Diagnostics

Expose:

- Current configuration version
- Last reload time
- Last save time
- Validation warnings
- Restart required flag

These values assist troubleshooting.

---

# Unit Testing

Recommended tests:

- Default configuration creation
- Serialization
- Deserialization
- Validation
- Migration
- Atomic save
- Hot reload
- Change detection

---

# Configuration Subsystem Architecture

Recommended namespace:

```
OpenAudioLink.Configuration
```

---

# Directory Structure

```
Configuration/

├── ConfigurationManager.cs
├── ConfigurationFactory.cs
├── ConfigurationValidator.cs
├── ConfigurationMigrator.cs
├── ConfigurationSerializer.cs
├── ConfigurationWatcher.cs
├── ConfigurationComparer.cs
├── ConfigurationBackup.cs
│
├── Models/
│   ├── AppConfiguration.cs
│   ├── ApplicationConfiguration.cs
│   ├── NetworkConfiguration.cs
│   ├── AudioConfiguration.cs
│   ├── UiConfiguration.cs
│   ├── WindowConfiguration.cs
│   └── LoggingConfiguration.cs
│
└── Events/
    └── ConfigurationChangedEventArgs.cs
```

---

# Module Responsibilities

## ConfigurationManager

Responsible for:

- Loading configuration
- Saving configuration
- Providing current configuration
- Publishing configuration changes

---

## ConfigurationFactory

Creates:

- Default configuration
- Missing sections
- Initial configuration during first launch

---

## ConfigurationValidator

Responsible for:

- Range checking
- Enum validation
- Null handling
- Automatic normalization

---

## ConfigurationMigrator

Responsibilities:

- Detect configuration version
- Upgrade schema
- Preserve user settings
- Save upgraded file

---

## ConfigurationSerializer

Responsibilities:

- JSON serialization
- JSON deserialization
- UTF-8 encoding
- Stable formatting

---

## ConfigurationWatcher

Responsible for:

- File monitoring
- Debouncing
- Triggering reload

---

## ConfigurationComparer

Produces:

```text
Previous

↓

Current

↓

Changed Keys
```

Used to minimize unnecessary updates.

---

# Startup Sequence

```text
Application Start

↓

Locate Config Folder

↓

config.json Exists?

      |

      +------ No

      |

      ▼

Create Default

↓

Save

↓

Load

      |

      +------ Yes

      |

      ▼

Deserialize

↓

Validate

↓

Migrate

↓

Publish Configuration
```

---

# Runtime Lifecycle

```text
Application Running

↓

Configuration Changed

↓

Reload

↓

Compare

↓

Notify Modules
```

---

# Shutdown Sequence

```text
Flush Pending Changes

↓

Dispose File Watcher

↓

Release Resources

↓

Application Exit
```

---

# Error Recovery

If the active configuration is unusable:

```text
Load

↓

Validation Failed

↓

Load Backup

↓

Validation Failed

↓

Create Default

↓

Continue Startup
```

The application must never fail to start because of configuration corruption.

---

# Configuration Ownership

The configuration manager is the only component allowed to modify configuration files.

All other modules receive immutable snapshots.

Architecture:

```text
ConfigurationManager

        |

        +------ Audio

        |

        +------ Discovery

        |

        +------ UI

        |

        +------ Logging
```

---

# Persistence Rules

Configuration should be saved:

- When the user explicitly changes settings
- After successful migration
- After importing configuration

Avoid periodic automatic saves.

---

# Performance Targets

| Metric | Target |
|--------|--------:|
| Initial Load | < 100 ms |
| Validation | < 20 ms |
| Save | < 50 ms |
| Hot Reload | < 200 ms |
| Memory Usage | < 2 MB |

---

# Future Extensions

Reserved configuration sections:

```json
{
    "security": {},

    "pairing": {},

    "streaming": {},

    "diagnostics": {}
}
```

Adding new sections must not affect older application versions.

---

# Compatibility Policy

Versioning rules:

- New fields must have defaults.
- Existing fields must not change meaning.
- Deprecated fields remain readable for at least one major version.
- Unknown fields are ignored.

---

# Example Complete Configuration

```json
{
    "configVersion": 1,

    "application": {
        "startWithWindows": false,
        "startHidden": false,
        "minimizeToTray": true,
        "singleInstance": true,
        "checkForUpdates": true,
        "language": "system"
    },

    "network": {
        "tcpPort": 39888,
        "udpPort": 39887,
        "enableMdns": true,
        "enableBroadcast": true,
        "enableUnicast": true,
        "allowVpnDiscovery": false,
        "discoveryTimeoutMs": 3000,
        "offlineTimeoutMs": 30000
    },

    "audio": {
        "codec": "aac",
        "sampleRate": 48000,
        "channels": 2,
        "bitDepth": 16,
        "bufferMs": 100,
        "latencyMode": "balanced",
        "renderer": "auto",
        "outputDevice": "default"
    },

    "ui": {
        "theme": "system",
        "showNotifications": true,
        "showTrayIcon": true,
        "closeToTray": true,
        "showDebugPanel": false,
        "window": {
            "width": 980,
            "height": 720,
            "maximized": false
        }
    },

    "logging": {
        "level": "Information",
        "writeFile": true,
        "writeConsole": false,
        "keepDays": 30
    }
}
```

---

# Configuration Checklist

## Loading

- [ ] Create default configuration
- [ ] Deserialize JSON
- [ ] Validate values
- [ ] Migrate older versions

---

## Saving

- [ ] Atomic write
- [ ] Backup existing file
- [ ] Preserve formatting

---

## Runtime

- [ ] File watcher
- [ ] Hot reload
- [ ] Immutable snapshots
- [ ] Change notifications

---

## Recovery

- [ ] Restore backup
- [ ] Create defaults
- [ ] Continue startup

---

## Diagnostics

- [ ] Validation warnings
- [ ] Reload timestamp
- [ ] Restart required flag

---

# Configuration Design Principles

The configuration subsystem follows these principles:

1. Configuration is external to the application.
2. A single manager owns persistence.
3. Configuration objects are immutable.
4. Validation always precedes application.
5. Hot reload is supported where safe.
6. Corrupted configuration must never prevent startup.
7. Future versions remain backward compatible.

---

# End of Document

docs/08-Configuration.md complete.