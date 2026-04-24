# CLAUDE.md — GymSync ZKT Bridge

.NET 8 ASP.NET Core middleware that exposes the ZKTeco `zkemkeeper` COM SDK as a JSON HTTP API. Runs as a **Windows service per branch** alongside the ZKT device — it is the production path Reception uses to reach the device.

> **Don't duplicate this file's content elsewhere.** Full API + setup lives in `docs/`:
> - [`docs/architecture.md`](docs/architecture.md) — overview, project layout, design decisions
> - [`docs/api-reference.md`](docs/api-reference.md) — complete `/api/v1/*` reference (~40 endpoints)
> - [`docs/sdk-setup.md`](docs/sdk-setup.md) — SDK files, registration, troubleshooting
> - [`docs/deployment.md`](docs/deployment.md) — Windows service install / update / uninstall
> - [`docs/elixir-integration.md`](docs/elixir-integration.md) — Reception-side HTTP client examples

When in doubt, edit those — this file is for cross-cutting conventions and gotchas, not API minutiae.

## Role in GymSync

Per branch:

```
┌─────────────────────────┐    HTTP/JSON    ┌────────────────────┐    COM (STA)    ┌─────────────┐
│ gym_sync_reception      │ ──────────────> │ Bridge (this app)  │ ──────────────> │ ZKT device  │
│ (App.BridgeApi client)  │ <────────────── │ Windows service    │ <────────────── │ (TCP 4370)  │
└─────────────────────────┘                 └────────────────────┘                 └─────────────┘
```

- Reception is the **default** caller. It can also fall back to direct ZKP-over-TCP via its own `App.Devices.*` GenServer if the Bridge is offline (toggled in Reception's Settings).
- Bridge holds the **only** persistent client connection to the device — connect-per-request, serialised behind a `SemaphoreSlim`. Devices accept one TCP client at a time, so don't run a second consumer of the device alongside the Bridge.
- The Bridge can fan out to multiple devices via `config.json`'s `devices[]` array (see `/api/v1/sync/user/all`).

## Platform & runtime

| | |
|---|---|
| OS | Windows (x86 or x64) — non-negotiable; `zkemkeeper` is COM |
| Runtime | .NET SDK 8.0+ (release builds are self-contained — no .NET install needed on target) |
| Service | Installed as `GymSyncZkt` at `C:\GymSync\` by `install/install.ps1` |
| Default port | `5000` (firewall opened by installer; bind to `0.0.0.0` in `web.host` for LAN access) |

Service control (elevated PowerShell): `sc.exe start|stop|query GymSyncZkt`. Test UI at `http://localhost:5000`.

## Two API layers

Both share the same `DeviceClient` + STA executor + semaphore. **Don't add new endpoints to `/api/*`** — that namespace is frozen for the legacy browser test UI. New work goes in `/api/v1/*`.

| Prefix | Purpose | Response shape |
|---|---|---|
| `/api/*` | Legacy browser test UI (`wwwroot/`) | Loose — varies per endpoint |
| `/api/v1/*` | Elixir / programmatic clients | Envelope: `{"ok": true, "data": {...}}` or `{"ok": false, "error": "..."}` |

`/api/v1/*` lives in `src/GymSync.Zkt.WebUI/ApiV1.cs`. Test UI routes live in `Program.cs`.

## Endpoint surface (summary)

Full reference: [`docs/api-reference.md`](docs/api-reference.md). Quick map:

| Area | Routes | Purpose |
|---|---|---|
| Connection | `connect`, `devices`, `status` | Probe device(s); status reports `online: false` rather than HTTP-erroring |
| Users | `users`, `users/get`, `users/create`, `users/update`, `users/delete`, `users/enable`, `users/validity/get`, `users/validity/set` | Full user CRUD on device + enable/disable + validity windows |
| Enrollment | `enroll/finger`, `enroll/face` | Trigger physical enrollment (user must be at device) |
| Templates | `templates/all`, `templates/upload`, `templates/finger/{get,upload,delete}`, `templates/face/{get,upload,delete}` | Bulk + single-slot template I/O (base64) |
| Sync | `sync/user`, `sync/user/all` | Read source → upsert target users → upload templates in one call |
| Attendance | `attendance/{all,new,range,admin,clear,clear-admin,delete-range,delete-before}` | `new` returns logs since last read — Reception polls this on a timer |
| Device | `device/info`, `device/time`, `device/time/sync`, `device/restart`, `device/voice`, `device/door/{lock,unlock}` | Hardware control |

Every request body accepts optional `ip` / `port` / `password` / `timeout` / `machineNumber` overrides; defaults come from `config.json`'s `device` block.

## STA threading

`zkemkeeper.CZKEM` must be created and called from a Single-Threaded Apartment thread. ASP.NET Core's thread pool is MTA. **All COM calls must go through `src/GymSync.Zkt.Core/StaExecutor.cs`** — a dedicated STA background thread with a work queue. Never instantiate `CZKEM` directly from a controller; the wrapper handles marshalling and disposes the COM object on the same thread.

## Connect-per-request

`DeviceClient` connects, performs the operation, disconnects. No persistent sessions. Combined with the `SemaphoreSlim`, this means:

- Concurrent API requests are serialised, not parallel — devices reject concurrent clients anyway.
- A second consumer (Reception's direct TCP, the test UI, another HTTP client) racing against the Bridge will cause connect failures or kick someone off. Pick one.
- Restarting the service costs ~nothing since there's no session to rebuild.

## Real-time scan events: poll, don't push

There is **no** SSE / WebSocket / webhook push for attendance scans. `POST /api/v1/attendance/new` returns logs since the last read; Reception polls on a timer (~30s — see [`docs/elixir-integration.md`](docs/elixir-integration.md)). For gym attendance latency, this is fine. If push is ever needed, hook `OnAttTransactionEx` from `zkemkeeper` and proxy out via SSE — but keep the polling endpoint as the primary API.

## Configuration

`config.json` (full reference in [`docs/api-reference.md`](docs/api-reference.md) §config):

```json
{
  "device":  { "ip": "10.121.0.206", "port": 4370, "password": 0, "timeout": 10, "machineNumber": 1 },
  "devices": [
    { "name": "entrance", "ip": "10.121.0.206", "port": 4370 },
    { "name": "exit",     "ip": "10.121.0.207", "port": 4370 }
  ],
  "storage": { "path": "storage/templates" },
  "web":     { "host": "127.0.0.1", "port": 5000 }
}
```

- `device` — fallback target when a request omits `ip`/`port`
- `devices[]` — fan-out list for `/sync/user/all` and what `/devices` returns
- `web.host = "0.0.0.0"` — required for LAN access from Reception

Per-device IPs and the LAN bind are usually edited at `C:\GymSync\config.json` after install — restart the service to pick up changes.

## Project layout

```
src/
  GymSync.Zkt.Core/        # No web deps — usable from CLI / scripts
    Config.cs              # config.json + env var loader
    DeviceClient.cs        # All zkemkeeper SDK operations
    StaExecutor.cs         # STA thread + work queue (see "STA threading")
    TemplateStore.cs       # Local manifest + .bin storage
    Models.cs              # DTOs
  GymSync.Zkt.WebUI/
    Program.cs             # Legacy /api/* test UI routes
    ApiV1.cs               # /api/v1/* (Elixir-facing) routes
    wwwroot/{index.html, app.css, app.js}
sdk/                       # Pre-generated Interop.zkemkeeper.dll + native helper DLLs (gitignored)
install/                   # install.ps1 / uninstall.ps1
tests/ConnTest/            # Smoke test for SDK + connect path
build-release.ps1          # Bundles everything into dist/GymSyncZkt/ for client install
storage/templates/         # Downloaded template cache (gitignored)
docs/                      # See top of this file
```

## SDK gotchas

Full table in [`docs/sdk-setup.md`](docs/sdk-setup.md). Most common:

| Symptom | Cause / fix |
|---|---|
| `Connect_Net returns false, error -201` | Helper DLLs (`tcpcomm.dll`, `comms.dll`, etc.) missing from app dir — install puts them in `C:\GymSync\app\`, don't move them |
| `COM ProgID not found` | `zkemkeeper.dll` not registered — installer runs `regsvr32`; if installing manually, do this elevated |
| `STA thread error` | Direct COM call from MTA — route through `StaExecutor` |
| `Device busy / hangs` | Another client connected (Reception's direct-TCP path? another Bridge?) — close the other consumer |
| Service crashes on startup | Usually missing `Interop.zkemkeeper.dll` next to the exe; the assembly resolver has a fallback but the file should be at `C:\GymSync\app\` |

This project uses the **pre-generated** `Interop.zkemkeeper.dll` (strong-typed `new CZKEM()`), not late-bound `Type.GetTypeFromProgID`. ProgIDs vary by SDK version (`zkemkeeper.CZKEM` vs `zkemkeeper.ZKEM`); the interop assembly works regardless.

## Storage layout (template cache)

```
storage/templates/<device_ip>/<enroll_number>/
├── manifest.json
├── finger_0.bin … finger_9.bin
└── face_50.bin  … face_54.bin
```

Byte-compatible with the experimental `../ZKT Test/zkteco_*_app/` siblings — a template downloaded by any of them can be uploaded by another. `GET /api/v1/devices` is *not* about local storage — that's `GET /api/storage` (legacy).

## Relationship to the rest of GymSync

- **Reception** is the production HTTP caller. See `gym_sync_reception/CLAUDE.md` § "Talking to the Bridge" for the `App.BridgeApi` client + the bridge/tcp toggle.
- **Core** has no direct relationship — biometric templates flow Reception → Bridge → device locally, then Reception → Core via the existing biometric sync push (`/api/v1/biometrics/upsert` on Core).
- **`../ZKT Test/`** harnesses are reference implementations in other languages (Python / Node / PHP / C#). Treat them as reference only; the Bridge is the canonical implementation.
