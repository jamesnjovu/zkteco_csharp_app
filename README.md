# GymSync.Zkt — ZKTeco C# Middleware

HTTP/JSON middleware between the GymSync Elixir reception app and ZKTeco biometric devices. Runs as a Windows service on the client PC.

```
                                                          ┌──────────────┐
                                                     ┌──> │ Device 1 (IN)│
┌──────────────┐    HTTP/JSON    ┌────────────────┐  │    └──────────────┘
│  Elixir App  │ ──────────────> │ C# Middleware  │──┤
│  (Reception) │ <────────────── │ (this app)     │  │    ┌───────────────┐
└──────────────┘   /api/v1/*     └────────────────┘  └──> │ Device 2 (OUT)│
                                     COM / TCP            └───────────────┘
```

## What it does

- Manages users, fingerprints, and faces across multiple ZKTeco devices
- Syncs biometric templates from an enrollment device to all other devices in one API call
- Polls attendance logs from all devices
- Controls doors, device time, voice prompts, restart
- Browser-based test UI for manual testing
- Installs as a Windows service with auto-start on boot

## Quick start (development)

### Prerequisites

- Windows (ZKTeco SDK is COM/Windows-only)
- .NET SDK 8.0+
- ZKTeco SDK — `zkemkeeper.dll` registered and helper DLLs in `sdk/x64/` (see [SDK Setup](docs/sdk-setup.md))

### Run

```bash
# 1. Copy and edit config
cp config.example.json config.json

# 2. Build and run
dotnet restore
dotnet run --project src/GymSync.Zkt.WebUI

# 3. Open http://localhost:5000
```

## Production deployment

Build on your dev PC, copy one folder to the client, double-click to install:

```powershell
# On your dev PC
.\build-release.ps1
```

This creates `dist\GymSyncZkt\` — a self-contained package (no .NET runtime install needed on the client).

```
# On the client PC
1. Copy dist\GymSyncZkt\ to the client
2. Edit config.json with the client's device IPs
3. Right-click INSTALL.bat -> Run as Administrator
```

The installer registers the SDK, installs the Windows service, opens the firewall, and starts it. Auto-starts on every boot.

See [Deployment Guide](docs/deployment.md) for full details.

## API

Two API layers serve different consumers:

| Layer | Base path | Consumer | Purpose |
|---|---|---|---|
| Test UI | `/api/*` | Browser | Manual testing via the web UI |
| V1 API | `/api/v1/*` | Elixir app | Programmatic access, multi-device workflows |

### Key endpoints

| Endpoint | Description |
|---|---|
| `POST /api/v1/connect` | Test connection to a device |
| `GET  /api/v1/devices` | List all configured devices |
| `POST /api/v1/users` | List users on a device |
| `POST /api/v1/users/create` | Create a user |
| `POST /api/v1/templates/all` | Get all fingerprint + face templates for a user |
| `POST /api/v1/templates/upload` | Upload all templates to a device |
| `POST /api/v1/sync/user` | Sync user + templates from source to specific targets |
| `POST /api/v1/sync/user/all` | Sync user + templates to ALL configured devices |
| `POST /api/v1/attendance/new` | Get new attendance logs since last poll |

Every request targets a specific device via `ip` and `port` in the body:

```json
POST /api/v1/sync/user/all
{
  "sourceIp": "10.121.0.206",
  "enrollNumber": "1001"
}
```

See [API Reference](docs/api-reference.md) for all 30+ endpoints with request/response examples.

## Configuration

```json
{
  "device": {
    "ip": "10.121.0.206",
    "port": 4370,
    "password": 0,
    "timeout": 10,
    "machineNumber": 1
  },
  "devices": [
    { "name": "entrance", "ip": "10.121.0.206", "port": 4370 },
    { "name": "exit", "ip": "10.121.0.207", "port": 4370 }
  ],
  "storage": {
    "path": "storage/templates"
  },
  "web": {
    "host": "0.0.0.0",
    "port": 5000
  }
}
```

- `device` — default device when `ip`/`port` are omitted from API requests
- `devices` — all devices on the network, used by `/api/v1/sync/user/all` and `/api/v1/devices`
- `web.host` — `0.0.0.0` to accept connections from other machines, `127.0.0.1` for localhost only

## Project layout

```
zkteco_csharp_app/
├── GymSync.Zkt.sln
├── config.example.json            # Template config
├── build-release.ps1              # Builds installer package
│
├── src/
│   ├── GymSync.Zkt.Core/          # Device logic (no web dependency)
│   │   ├── DeviceClient.cs        # All ZKTeco SDK operations
│   │   ├── StaExecutor.cs         # STA thread for COM calls
│   │   ├── Models.cs              # DTOs
│   │   ├── Config.cs              # Config loading
│   │   └── TemplateStore.cs       # Local template file storage
│   │
│   └── GymSync.Zkt.WebUI/         # ASP.NET Core web layer
│       ├── Program.cs             # Test UI API routes (/api/*)
│       ├── ApiV1.cs               # Elixir API routes (/api/v1/*)
│       └── wwwroot/               # Browser test UI
│
├── sdk/                           # ZKTeco SDK binaries (gitignored)
├── install/                       # install.ps1, uninstall.ps1
├── storage/templates/             # Downloaded templates (gitignored)
│
└── docs/
    ├── api-reference.md           # Full API documentation
    ├── architecture.md            # System design & data flows
    ├── deployment.md              # Build & install as Windows service
    ├── elixir-integration.md      # Elixir client examples
    └── sdk-setup.md               # ZKTeco SDK setup & troubleshooting
```

## Documentation

| Document | Description |
|---|---|
| [API Reference](docs/api-reference.md) | All endpoints with request/response examples |
| [Architecture](docs/architecture.md) | System diagram, design decisions, data flows |
| [Deployment](docs/deployment.md) | Build, install on client PC, run as Windows service |
| [Elixir Integration](docs/elixir-integration.md) | HTTP client module, code examples, typical workflows |
| [SDK Setup](docs/sdk-setup.md) | ZKTeco SDK installation, registration, troubleshooting |
