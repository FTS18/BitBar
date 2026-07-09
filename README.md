# Uplink

A minimal, native network speed monitor that lives in your Windows 11 taskbar. No bloat, no electron, no background services beyond itself.

## What it does

- Shows live upload and download speeds directly on the taskbar
- Hover to get a flyout graph of the last 60 seconds of traffic, top process consuming bandwidth, and current speeds
- Right-click for settings and controls
- Fades out automatically when the network is idle
- Detects your system theme and renders accordingly

## Installation

Download Uplink.exe from [Releases](https://github.com/FTS18/Uplink/releases) and run install.ps1 (right-click, Run with PowerShell).

The installer will:
- Build and copy the binary to C:\Program Files\Uplink
- Create a Start Menu shortcut
- Register it to auto-start on boot via the registry

No admin rights are required after initial install.

## Settings

Right-click the taskbar widget and select **Settings** to configure:

- **Network Adapter** - Auto-detect or pin to a specific interface (Wi-Fi, Ethernet, VPN)
- **Display Units** - MB/s (bytes) or Mbps (bits)
- **Refresh Rate** - 100ms to 5000ms

Settings are stored in HKCU\Software\Uplink.

## Building from source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

`
cd windows
dotnet build
dotnet run
`

To produce a single-file release binary:

`
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true
`

## Requirements

- Windows 10 22H2 or later (Windows 11 recommended)
- .NET 8 Runtime (x64)
- A taskbar

## License

MIT
