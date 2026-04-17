# Architecture

## Overview

```
┌─────────────────┐     HTTP/JSON      ┌──────────────────────┐     COM/TCP      ┌─────────────┐
│  Elixir App     │ ──────────────────> │  C# Middleware       │ ───────────────> │ ZKTeco       │
│  (Reception)    │   /api/v1/*         │  (this app)          │   zkemkeeper     │ Devices      │
│                 │ <────────────────── │                      │ <─────────────── │              │
└─────────────────┘                     └──────────────────────┘                  └─────────────┘
                                                                                  ┌─────────────┐
                                                                 ───────────────> │ Device 2     │
                                                                                  └─────────────┘
                                                                                  ┌─────────────┐
                                                                 ───────────────> │ Device N     │
                                                                                  └─────────────┘
```

## Why a C# middleware?

The ZKTeco SDK (`zkemkeeper.dll`) is a **Windows COM component**. It only works on Windows and must be called via COM interop. Elixir/Erlang cannot call COM objects directly, so this C# app acts as a proxy — exposing all device operations as simple HTTP/JSON endpoints.

## Project structure

```
zkteco_csharp_app/
├── GymSync.Zkt.sln
├── config.example.json          # Template config
├── config.json                  # Local config (gitignored)
├── build-release.ps1            # Build installer package
│
├── src/
│   ├── GymSync.Zkt.Core/        # Device logic (no web dependency)
│   │   ├── Config.cs            # Config loading + models
│   │   ├── DeviceClient.cs      # All ZKTeco SDK operations
│   │   ├── StaExecutor.cs       # STA thread for COM calls
│   │   ├── Models.cs            # DTOs
│   │   └── TemplateStore.cs     # Local file storage for templates
│   │
│   └── GymSync.Zkt.WebUI/       # ASP.NET Core web layer
│       ├── Program.cs           # Test UI API routes (/api/*)
│       ├── ApiV1.cs             # Elixir API routes (/api/v1/*)
│       └── wwwroot/             # Browser test UI
│           ├── index.html
│           ├── app.css
│           └── app.js
│
├── sdk/                         # ZKTeco SDK binaries (gitignored)
│   ├── Interop.zkemkeeper.dll   # .NET COM interop assembly
│   ├── x64/                     # 64-bit native DLLs
│   └── x86/                     # 32-bit native DLLs
│
├── install/                     # Deployment scripts
│   ├── install.ps1
│   └── uninstall.ps1
│
├── storage/templates/           # Downloaded templates (gitignored)
│
└── docs/                        # Documentation
```

## Key design decisions

### STA thread requirement

The `CZKEM` COM object must be created and called from a **Single-Threaded Apartment (STA)** thread. ASP.NET Core uses MTA thread pool threads. All COM calls are marshalled through `StaExecutor` — a dedicated STA background thread with a work queue.

### Connect-per-request

Each API request creates a new `DeviceClient`, connects, performs the operation, and disconnects. This is simple and avoids stale connection state. The ZKTeco device only accepts one TCP client at a time, so a `SemaphoreSlim` serializes all device access.

### Interop.zkemkeeper.dll

The project uses a pre-generated .NET COM interop assembly (`Interop.zkemkeeper.dll`) rather than late-bound `Type.GetTypeFromProgID`. This gives us strong typing and matches the approach used by the proven `ZktBridge` project. The native SDK helper DLLs (`tcpcomm.dll`, `comms.dll`, etc.) must be present in the output directory for TCP connections to work.

### Two API layers

- `/api/*` — Test UI endpoints (used by the browser-based test page)
- `/api/v1/*` — Elixir API endpoints (clean, consistent, designed for programmatic use)

Both share the same `DeviceClient` and `SemaphoreSlim`. The v1 layer wraps responses in `{ok, data}` / `{ok, error}` envelopes.

### Multi-device support

The `config.json` `"devices"` array lists all known devices. The sync endpoints (`/api/v1/sync/user`, `/api/v1/sync/user/all`) use this list to replicate users and templates across devices.

## Data flow: User enrollment

```
1. Elixir app calls POST /api/v1/users/create
   → Middleware connects to enrollment device, creates user record

2. Elixir app calls POST /api/v1/enroll/finger
   → Middleware tells device to start enrollment
   → User places finger on sensor (physical interaction)

3. Elixir app calls POST /api/v1/sync/user/all
   → Middleware reads user + all templates from enrollment device
   → For each target device:
     → Creates user if not exists
     → Uploads all fingerprint + face templates
   → Returns per-device success/failure
```

## Data flow: Attendance polling

```
1. Elixir app calls POST /api/v1/attendance/new for each device
   → Middleware reads only new logs since last poll
   → Returns array of {userId, timestamp, verifyMethod, inOutState}

2. Elixir app stores logs in its own database
```
