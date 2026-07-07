# docs/09-Deployment.md

# Deployment and Distribution

Version: 1.0

---

# Overview

This document defines the deployment strategy for OpenAudioLink.

Goals:

- Easy installation
- Windows 7 compatibility
- Zero manual dependency installation
- Automatic firewall configuration
- Reliable upgrades
- Simple uninstallation

---

# Deployment Targets

Version 1 supports:

| Platform | Target |
|----------|--------|
| Windows 7 SP1 x64 | ✔ |
| Windows 8.1 | ✔ |
| Windows 10 | ✔ |
| Windows 11 | ✔ |
| Android 8.0+ | ✔ |

Future versions may add:

- Linux
- macOS

---

# Distribution Components

The project consists of two applications.

```
Android Sender

+

Windows Receiver
```

They are distributed independently.

---

# Windows Packaging

Recommended installer:

```
Inno Setup
```

Reason:

- Windows 7 compatible
- Mature ecosystem
- Small installer size
- Scriptable
- Easy upgrades
- No runtime requirement

---

# Why Not MSIX

MSIX is not recommended because:

- No Windows 7 support
- Limited customization
- Additional platform requirements

Version 1 prioritizes maximum compatibility.

---

# Why Not ClickOnce

Reasons:

- Legacy deployment model
- Limited customization
- Weak installer experience
- Poor control over firewall configuration

---

# Why Not NSIS

NSIS is a valid alternative, but Inno Setup offers:

- Better documentation
- Cleaner scripting
- More maintainable installer definitions
- Strong Unicode support

---

# Runtime Strategy

Recommended deployment:

```
Self-contained
```

Advantages:

- No .NET runtime installation
- Predictable behavior
- Easier support
- Offline installation

Trade-off:

Larger installer size.

---

# Self-contained Build

Publish example:

```bash
dotnet publish

-c Release

-r win-x64

--self-contained true
```

The installer includes all required runtime components.

---

# Installation Layout

Recommended location:

```
C:\Program Files\OpenAudioLink\
```

Contents:

```
OpenAudioLink.exe

OpenAudioLink.dll

runtime/

assets/

licenses/
```

Configuration files remain under `%APPDATA%`.

---

# User Data

Application data:

```
%APPDATA%\OpenAudioLink\
```

Contains:

```
config.json

receivers.json

logs/

cache/
```

User data must survive upgrades.

---

# Desktop Shortcuts

Installer options:

```
☑ Desktop Shortcut

☑ Start Menu Shortcut
```

Defaults:

Desktop:

Disabled

Start Menu:

Enabled

---

# File Associations

Version 1 does not register:

- File extensions
- URL protocols
- Shell extensions

The application is launched normally.

---

# Start With Windows

The installer should not enable auto-start.

Instead:

Application setting:

```
Start with Windows
```

creates or removes the startup entry.

---

# Startup Mechanism

Preferred:

```
HKCU

Software

Microsoft

Windows

CurrentVersion

Run
```

Reason:

- No administrator privilege required
- User-specific
- Compatible with Windows 7+

---

# Single Instance

Application startup:

```text
Launch

↓

Check Mutex

↓

Already Running?

↓

Yes

↓

Activate Existing Window

↓

Exit
```

Only one receiver instance should run per user session.

---

# Windows Firewall

During installation:

Create inbound rules for:

```
UDP 5353

UDP 39887

TCP 39888
```

Rules should be named:

```
OpenAudioLink Receiver
```

---

# Firewall Failure

If firewall rule creation fails:

```
Continue Installation

↓

Show Warning

↓

Provide Manual Instructions
```

Firewall failure must not abort installation.

---

# Administrator Privileges

Required only for:

- Installing into Program Files
- Firewall rule creation

Normal application usage should never require elevation.

---

# Upgrade Strategy

The upgrade process must preserve user configuration and require minimal user interaction.

Objectives:

- Preserve settings
- Preserve receiver cache
- Preserve logs
- Replace application binaries safely
- Roll back on failure

---

# Upgrade Workflow

```text
Launch Installer

↓

Detect Existing Installation

↓

Stop Running Instance

↓

Backup Application Files

↓

Install New Version

↓

Preserve User Data

↓

Launch Application
```

The installer should never overwrite files under `%APPDATA%`.

---

# Version Detection

The installer should detect:

- Installed version
- Installation path
- Architecture (x64)

If an older version is found, perform an in-place upgrade.

---

# Backup During Upgrade

Before replacing binaries:

```text
OpenAudioLink.exe

↓

OpenAudioLink.exe.bak
```

If installation fails:

```text
Restore Backup

↓

Rollback
```

---

# Configuration Migration

After application startup:

```text
Load config.json

↓

Compare Version

↓

Run Migration

↓

Save Updated Configuration
```

Migration is handled by the application, not the installer.

---

# Uninstall Behavior

Uninstall removes:

```
Program Files\OpenAudioLink\
```

By default, it preserves:

```
%APPDATA%\OpenAudioLink\
```

Users should be offered an option:

```
☐ Remove user configuration and logs
```

Unchecked by default.

---

# Portable Mode (Future)

Future versions may support:

```
OpenAudioLink.exe

config/

logs/

cache/
```

All files remain in the application directory.

No installation required.

Portable mode is intended for USB drives or temporary environments.

---

# Digital Signing

Release builds should be code signed.

Benefits:

- Improved Windows SmartScreen reputation
- Better user trust
- Reduced security warnings

Unsigned builds remain suitable for development.

---

# Release Channels

Recommended channels:

| Channel | Purpose |
|----------|---------|
| Stable | Public releases |
| Preview | Feature validation |
| Nightly | Development builds |

Configuration option:

```json
{
    "application": {
        "updateChannel": "stable"
    }
}
```

---

# Automatic Update Policy

Version 1:

```
Manual update notification only.
```

Workflow:

```text
Check Latest Version

↓

New Version?

↓

Notify User

↓

Open Release Page
```

The application does not download or install updates automatically.

---

# Update Check

Frequency:

```
Once every 24 hours
```

Skip checks when:

- Offline
- User disabled update checks

---

# Android Distribution

Primary distribution:

```
GitHub Releases
```

APK naming:

```
OpenAudioLink-Android-1.0.0.apk
```

Alternative distribution channels may be added later.

---

# APK Signing

Every release APK must be signed.

Suggested approach:

- Debug key for development
- Dedicated release key for production

The release signing key must never be committed to source control.

---

# Windows Release Package

Recommended release assets:

```
OpenAudioLink-Setup-1.0.0.exe

SHA256SUMS.txt

CHANGELOG.md

LICENSE
```

Optionally:

```
OpenAudioLink-Portable-1.0.0.zip
```

---

# Release Versioning

Use Semantic Versioning:

```
MAJOR.MINOR.PATCH
```

Examples:

```
1.0.0

1.1.0

1.1.1

2.0.0
```

---

# Changelog

Every release should include:

- New features
- Improvements
- Bug fixes
- Breaking changes
- Known issues

Recommended format:

```
## Added

## Changed

## Fixed

## Removed
```

---

# Compatibility Policy

Version compatibility:

| Sender | Receiver | Supported |
|---------|----------|-----------|
| 1.0.x | 1.0.x | ✔ |
| 1.1.x | 1.0.x | ✔ (if protocol unchanged) |
| 2.0.x | 1.x | Depends on protocol negotiation |

Protocol negotiation should determine compatibility rather than version numbers alone.

---

# Installer Logging

The installer should generate an installation log.

Suggested location:

```
%TEMP%\OpenAudioLink-Install.log
```

Useful for diagnosing installation failures.

---

# Continuous Integration and Delivery

This document defines the automated build and release pipeline for OpenAudioLink.

Objectives:

- Fully automated builds
- Repeatable releases
- Version consistency
- Artifact integrity
- Minimal manual steps

---

# Source Control

Recommended platform:

```
GitHub
```

Repository layout:

```
OpenAudioLink/

├── android/
├── windows/
├── docs/
├── scripts/
├── .github/
│   └── workflows/
└── README.md
```

---

# Branch Strategy

Recommended branches:

| Branch | Purpose |
|---------|---------|
| main | Stable development |
| release/* | Release preparation |
| feature/* | Feature development |
| hotfix/* | Urgent fixes |

All pull requests should target `main`.

---

# Version Source

The application version should be defined in a single location.

Example:

```
version.json
```

```json
{
    "version": "1.0.0"
}
```

Build scripts read this file to ensure all artifacts use the same version.

---

# Git Tags

Every public release must create a Git tag.

Example:

```
v1.0.0

v1.1.0

v1.1.1
```

Tags are immutable after publication.

---

# GitHub Actions

Workflow directory:

```
.github/workflows/
```

Recommended workflows:

```
build.yml

release.yml

android.yml

windows.yml

lint.yml
```

Each workflow has a single responsibility.

---

# Build Workflow

Trigger:

- Push to `main`
- Pull request
- Manual dispatch

Pipeline:

```text
Checkout

↓

Restore Dependencies

↓

Build

↓

Run Tests

↓

Package

↓

Upload Artifacts
```

---

# Android Build

Pipeline:

```text
Checkout

↓

Setup Java

↓

Setup Android SDK

↓

Restore Gradle Cache

↓

Build Release APK

↓

Sign APK

↓

Upload Artifact
```

Release artifact:

```
OpenAudioLink-Android-1.0.0.apk
```

---

# Windows Build

Pipeline:

```text
Checkout

↓

Setup .NET SDK

↓

Restore NuGet Packages

↓

Build

↓

Publish Self-contained

↓

Package Installer

↓

Upload Artifact
```

Release artifact:

```
OpenAudioLink-Setup-1.0.0.exe
```

Optional portable artifact:

```
OpenAudioLink-Portable-1.0.0.zip
```

---

# Dependency Caching

Cache:

- NuGet packages
- Gradle cache

Benefits:

- Faster builds
- Reduced network usage
- Lower CI costs

---

# Build Configuration

All release builds use:

```
Release
```

Debug builds are generated only for development workflows.

---

# Artifact Naming

Windows:

```
OpenAudioLink-Setup-<version>.exe
```

Portable:

```
OpenAudioLink-Portable-<version>.zip
```

Android:

```
OpenAudioLink-Android-<version>.apk
```

Checksums:

```
SHA256SUMS.txt
```

---

# Release Workflow

Trigger:

```
Git Tag
```

Pipeline:

```text
Tag Push

↓

Build Windows

↓

Build Android

↓

Generate Checksums

↓

Create GitHub Release

↓

Upload Artifacts

↓

Publish Release Notes
```

---

# Release Notes

Release notes should be generated from:

- CHANGELOG.md
- Git commits
- Pull request titles

Manual editing is recommended before publication.

---

# Integrity Verification

Generate SHA-256 hashes for all release artifacts.

Example:

```
OpenAudioLink-Setup-1.0.0.exe

↓

SHA-256

↓

SHA256SUMS.txt
```

Users can verify downloaded files before installation.

---

# Signing Keys

Sensitive assets:

- Android release keystore
- Windows code-signing certificate (future)

These must be stored as encrypted CI secrets and never committed to the repository.

---

# Secrets Management

Recommended GitHub Secrets:

```
ANDROID_KEYSTORE

ANDROID_KEY_ALIAS

ANDROID_KEY_PASSWORD

ANDROID_STORE_PASSWORD
```

Future:

```
WINDOWS_SIGN_CERT

WINDOWS_SIGN_PASSWORD
```

---

# Failure Policy

If any required stage fails:

```text
Build

↓

Failure

↓

Stop Pipeline

↓

Report Error
```

Artifacts from failed release workflows must not be published.

---

# First Run Experience

The first launch experience should require minimal user interaction while ensuring the application is ready for streaming.

Objectives:

- Verify runtime environment
- Detect audio devices
- Verify network configuration
- Start discovery service
- Minimize setup time

---

# First Launch Workflow

```text
Application Start

↓

First Launch?

↓

Yes

↓

Run Initialization Wizard

↓

Save Configuration

↓

Start Receiver
```

Subsequent launches skip the wizard unless configuration is reset.

---

# Initialization Wizard

Recommended steps:

1. Welcome
2. Receiver Name
3. Audio Output Selection
4. Network Check
5. Firewall Check
6. Finish

The wizard should be skippable.

---

# Receiver Name

Default value:

```
<ComputerName>
```

Example:

```
Office-PC
```

Users may edit the display name.

---

# Audio Device Detection

At startup:

```text
Enumerate Audio Devices

↓

Select Default Output

↓

Save Device Identifier
```

If the previously selected device is unavailable:

```
Fallback

↓

System Default Device
```

---

# Audio Device Test

The wizard should provide:

```
▶ Test Sound
```

This verifies:

- Device availability
- Volume
- Channel configuration

---

# Network Detection

Collect:

- Active network adapters
- IPv4 addresses
- IPv6 addresses
- Default gateway

Display a summary for troubleshooting.

---

# Firewall Verification

Verify:

```
UDP 5353

UDP 39887

TCP 39888
```

If required rules are missing:

- Attempt automatic creation
- Otherwise show manual instructions

---

# mDNS Verification

Startup sequence:

```text
Initialize mDNS

↓

Publish Service

↓

Success?
```

Failure should not terminate the application.

Instead:

```
Disable mDNS

↓

Enable Broadcast Discovery

↓

Continue Startup
```

---

# Discovery Verification

After discovery services start:

```text
Publish Receiver

↓

Wait

↓

Ready
```

A status indicator should reflect readiness.

---

# System Tray

On startup:

Initialize:

- Tray icon
- Context menu
- Notification handler

Suggested menu:

```
Open

Pause Streaming

Restart Discovery

Settings

View Logs

Exit
```

---

# Startup Notification

Optional notification:

```
OpenAudioLink is running in the background.
```

Shown only on the first launch.

---

# Receiver Status

Tray icon states:

| Status | Meaning |
|--------|---------|
| Gray | Initializing |
| Green | Ready |
| Blue | Streaming |
| Yellow | Busy |
| Red | Error |

Status changes should be reflected immediately.

---

# Crash Recovery

Unexpected termination:

```text
Crash

↓

Write Crash Report

↓

Save Logs

↓

Restart Normally
```

The application should not enter a restart loop.

---

# Crash Reports

Recommended location:

```
%APPDATA%\OpenAudioLink\crash\
```

Filename:

```
Crash-2026-07-07-142315.log
```

Include:

- Version
- Stack trace
- Configuration summary
- Active modules

Exclude sensitive user information.

---

# Recovery After Crash

On next startup:

```text
Crash Report Exists?

↓

Yes

↓

Offer to Open Log

↓

Continue Startup
```

---

# Missing Audio Device

If the configured output device no longer exists:

```text
Configured Device Missing

↓

Use Default Device

↓

Notify User
```

Streaming should continue whenever possible.

---

# Network Changes During Runtime

When the active network changes:

```text
Network Event

↓

Refresh Interfaces

↓

Restart Discovery

↓

Keep Existing Streams
```

Existing audio sessions should remain active if the TCP connection is still valid.

---

# Background Operation

When the main window closes:

```text
Close Window

↓

Hide to Tray

↓

Continue Receiver Service
```

Actual termination occurs only when the user selects **Exit**.

---

# Diagnostics Page

Recommended information:

- Application version
- Receiver UUID
- Receiver name
- Active network interfaces
- Listening ports
- Discovery status
- Connected sender
- Audio renderer
- Current output device

Provide a **Copy Diagnostics** button for troubleshooting.

---

# User Experience Guidelines

The receiver should:

- Start quickly
- Require no technical knowledge
- Recover automatically from common failures
- Minimize notifications
- Continue operating silently in the background

---

# Installer Specification

The Windows installer is responsible for deploying the application safely and consistently.

Primary objectives:

- Predictable installation
- Easy upgrades
- Clean uninstallation
- Minimal user interaction

---

# Installer Technology

Recommended:

```
Inno Setup 6.x
```

Unicode edition should be used for all releases.

---

# Installer Layout

Suggested project structure:

```
installer/

├── OpenAudioLink.iss
├── Files.iss
├── Registry.iss
├── Firewall.iss
├── Tasks.iss
├── Icons.iss
├── Languages.iss
└── Resources/
```

Separate include files improve maintainability.

---

# Installation Directories

Application:

```
{autopf}\OpenAudioLink\
```

User configuration:

```
%APPDATA%\OpenAudioLink\
```

Temporary files:

```
%LOCALAPPDATA%\Temp\
```

Logs:

```
%APPDATA%\OpenAudioLink\logs\
```

Crash reports:

```
%APPDATA%\OpenAudioLink\crash\
```

---

# Directory Permissions

Application directory:

```
Read Only
```

for normal users.

User-specific data is written only under `%APPDATA%`.

No write access is required inside `Program Files`.

---

# Registry Usage

The application should minimize registry usage.

Recommended keys:

```
HKCU

Software

OpenAudioLink
```

Store only:

- Window position (optional)
- Startup preference
- Installation path (optional)

Configuration remains file-based.

---

# Startup Registration

When enabled:

```
HKCU

Software

Microsoft

Windows

CurrentVersion

Run
```

Value:

```
OpenAudioLink
```

Points to:

```
OpenAudioLink.exe
```

---

# Firewall Rules

Installer should attempt to create:

```
Inbound

UDP 5353

UDP 39887

TCP 39888
```

Rules should be removed during uninstall if they were created by the installer.

---

# Installer Tasks

Optional tasks:

```
☑ Create Desktop Shortcut

☑ Launch After Installation
```

Do not enable automatic startup by default.

---

# Installer Languages

Recommended:

- English
- Simplified Chinese
- Japanese

The installer language should default to the operating system language when available.

---

# Uninstaller

Responsibilities:

- Remove binaries
- Remove shortcuts
- Remove firewall rules
- Preserve user data unless explicitly requested

---

# Uninstall Options

Prompt:

```
Remove user configuration?

☐ Yes
☑ No
```

If the user chooses **Yes**, remove:

```
config.json

receivers.json

trusted.json

logs/

cache/

crash/
```

---

# Portable Edition

Suggested layout:

```
OpenAudioLink/

├── OpenAudioLink.exe
├── config/
├── logs/
├── cache/
├── crash/
└── runtime/
```

Portable mode should not:

- Modify the registry
- Create firewall rules automatically
- Register startup entries

---

# Enterprise Deployment

Future enterprise deployments may use:

- Group Policy
- Software Center
- Silent installation
- Configuration pre-seeding

---

# Silent Installation

Recommended installer switches:

```
/VERYSILENT

/SUPPRESSMSGBOXES

/NORESTART
```

This enables automated deployment.

---

# Installer Logging

Support:

```
/LOG
```

Example:

```
Setup.exe /LOG
```

The generated installation log assists in troubleshooting deployment issues.

---

# Installation Validation

After installation:

```text
Verify Files

↓

Verify Configuration Directory

↓

Verify Firewall Rules

↓

Launch Application (Optional)
```

---

# Installation Checklist

## Installer

- [ ] Correct version
- [ ] Code signing (future)
- [ ] Correct application icon
- [ ] Localized installer
- [ ] License included

---

## Runtime

- [ ] Application launches
- [ ] Configuration directory created
- [ ] Discovery service starts
- [ ] Audio engine initializes

---

## Networking

- [ ] TCP listener active
- [ ] UDP listener active
- [ ] mDNS advertisement visible
- [ ] Broadcast discovery responding

---

## User Experience

- [ ] Tray icon visible
- [ ] Settings accessible
- [ ] Logs created
- [ ] Graceful shutdown

---

# Deployment Principles

The deployment system follows these principles:

1. Installation is simple.
2. Upgrades preserve user data.
3. Uninstallation is clean.
4. Configuration is never stored in the installation directory.
5. Administrator privileges are required only when necessary.
6. Portable mode remains independent of the installer.
7. Future enterprise deployment is considered from the beginning.

---

# Deployment Architecture

Deployment consists of four independent phases:

```text
Build

↓

Package

↓

Install

↓

Run
```

Each phase should be independently testable and repeatable.

---

# Project Deliverables

Version 1 release package:

```
OpenAudioLink-Setup-1.0.0.exe

OpenAudioLink-Portable-1.0.0.zip

OpenAudioLink-Android-1.0.0.apk

SHA256SUMS.txt

CHANGELOG.md

LICENSE
```

Future releases should preserve the same naming convention.

---

# Installation Lifecycle

```text
Download Installer

↓

Verify Signature

↓

Run Installer

↓

Install Files

↓

Create Configuration Folder

↓

Configure Firewall

↓

Launch Receiver
```

---

# Runtime Lifecycle

```text
Receiver Start

↓

Load Configuration

↓

Initialize Audio

↓

Initialize Discovery

↓

Start TCP Listener

↓

Ready
```

---

# Upgrade Lifecycle

```text
New Version

↓

Backup Existing Files

↓

Replace Binaries

↓

Keep User Data

↓

Run Migration

↓

Ready
```

---

# Uninstall Lifecycle

```text
Run Uninstaller

↓

Stop Receiver

↓

Remove Application Files

↓

Remove Shortcuts

↓

Remove Firewall Rules

↓

Preserve User Data (Default)

↓

Finish
```

---

# Directory Layout

Installed edition:

```
C:\Program Files\OpenAudioLink\
│
├── OpenAudioLink.exe
├── OpenAudioLink.CLI.exe
├── OpenAudioLink.Diagnostic.exe
├── OpenAudioLink.dll
├── LICENSE
├── README.txt
├── runtime/
├── assets/
└── licenses/
```

User data:

```
%APPDATA%\OpenAudioLink\
│
├── config.json
├── receivers.json
├── trusted.json
├── logs/
├── cache/
├── crash/
└── exports/
```

The separation of binaries and user data simplifies upgrades and backup.

---

# Deployment Performance Targets

| Metric | Target |
|--------|--------:|
| Installer Size | < 120 MB |
| Portable Package | < 100 MB |
| Installation Time | < 30 s |
| Application Startup | < 2 s |
| First Discovery | < 3 s |
| Audio Ready | < 1 s |

These targets should be validated during release testing.

---

# Recovery Strategy

The deployment system should tolerate:

- Interrupted installation
- Interrupted upgrade
- Corrupted configuration
- Missing audio device
- Firewall configuration failure

Recovery policy:

```text
Detect

↓

Recover Automatically

↓

Notify User

↓

Continue
```

The application should remain usable whenever possible.

---

# Diagnostics Bundle

The diagnostic utility should be able to generate a support package.

Suggested contents:

```
diagnostics.zip

├── system.txt
├── configuration.json
├── discovery.txt
├── audio.txt
├── logs/
└── crash/
```

Sensitive information such as authentication tokens or personal data must be excluded.

---

# Release Checklist

## Build

- [ ] Build succeeds
- [ ] Version numbers match
- [ ] Dependencies restored
- [ ] Unit tests passed

---

## Package

- [ ] Installer generated
- [ ] Portable archive generated
- [ ] Android APK signed
- [ ] Checksums generated

---

## Validation

- [ ] Windows 7 installation
- [ ] Windows 10 installation
- [ ] Windows 11 installation
- [ ] Android connection test
- [ ] Discovery verification
- [ ] Audio playback verification

---

## Distribution

- [ ] Git tag created
- [ ] GitHub Release published
- [ ] Release notes completed
- [ ] SHA256SUMS uploaded

---

## Documentation

- [ ] README updated
- [ ] CHANGELOG updated
- [ ] Migration notes added
- [ ] Known issues reviewed

---

# Deployment Principles

The deployment subsystem follows these principles:

1. Installation should require minimal user interaction.
2. User data must survive upgrades.
3. Deployment must support Windows 7 and newer.
4. Configuration is external to application binaries.
5. Every release must be reproducible.
6. Deployment failures should be recoverable.
7. Diagnostics should be available independently of the main application.

---

# Future Enhancements

Planned improvements include:

- Automatic update downloads
- Delta patch updates
- Background update service
- Enterprise deployment packages
- Microsoft Store distribution
- Winget package
- Chocolatey package
- Scoop package

These enhancements should remain optional and must not compromise compatibility with the standalone installer.

---

# End of Document

docs/09-Deployment.md complete.