# ZKTeco SDK Setup

The ZKTeco C# library is a COM component that is **Windows-only**. This document covers how to obtain, register, and troubleshoot the SDK.

---

## Required files

The SDK consists of:

| File | Purpose |
|---|---|
| `Interop.zkemkeeper.dll` | .NET COM interop assembly (compile-time + runtime) |
| `zkemkeeper.dll` | The actual COM server (must be registered with `regsvr32`) |
| `tcpcomm.dll` | TCP transport — required for `Connect_Net` |
| `comms.dll` | Communication layer |
| `zkemsdk.dll` | Core SDK logic |
| `commpro.dll`, `plcomms.dll`, etc. | Additional protocol/transport DLLs |

All native DLLs (`tcpcomm.dll`, `comms.dll`, etc.) must be in the same directory as the running application, otherwise `Connect_Net` silently fails with error -201.

---

## Where to get the SDK

The SDK is bundled with ZKTeco device software packages:

1. **ZKFinger SDK** — comes with fingerprint devices
2. **ZKAccess** — comes with access control devices
3. **ZKTime** — comes with attendance devices

Look for a folder containing `zkemkeeper.dll` + the helper DLLs. Both x86 and x64 versions exist.

If you have the `ZktBridge` project, the SDK is already at `ZktBridge/sdk/`.

---

## Registration

The COM DLL must be registered once on each machine that runs the middleware.

### Register (elevated PowerShell)

```powershell
# For 64-bit apps (default):
regsvr32 "C:\path\to\sdk\x64\zkemkeeper.dll"

# For 32-bit apps:
C:\Windows\SysWOW64\regsvr32.exe "C:\path\to\sdk\x86\zkemkeeper.dll"
```

### Verify registration

```powershell
[Type]::GetTypeFromProgID('zkemkeeper.ZKEM')
```

Non-null output = success. Some SDK versions register as `zkemkeeper.CZKEM` instead of `zkemkeeper.ZKEM`.

### Unregister

```powershell
regsvr32 /u "C:\path\to\sdk\x64\zkemkeeper.dll"
```

---

## Troubleshooting

| Error | Cause | Fix |
|---|---|---|
| `COM ProgID not found` | `zkemkeeper.dll` not registered | Run `regsvr32 zkemkeeper.dll` as admin |
| `Connect_Net returns false, error -201` | Helper DLLs (`tcpcomm.dll`, etc.) not in app directory | Copy all SDK DLLs to the app's output/publish directory |
| `FileNotFoundException: Interop.zkemkeeper` | Interop DLL not found at runtime | Ensure `Interop.zkemkeeper.dll` is in the output directory; the app has a fallback assembly resolver |
| `STA thread error` | COM called from MTA thread | All calls must go through `StaExecutor` (already handled in `DeviceClient`) |
| `Connect_Net returns false, error 0` | Wrong IP, port, or device is offline | Check device network settings: Menu > Communication > Ethernet |
| `Device busy / hangs` | Another client connected | ZKTeco devices accept one TCP client at a time; close other connections |

---

## SDK version notes

Different SDK versions expose slightly different COM ProgIDs and method signatures:

| ProgID | SDK Version | Notes |
|---|---|---|
| `zkemkeeper.CZKEM` | Newer (ZKAccess 3.5+) | More methods, face support |
| `zkemkeeper.ZKEM` | Older | Fewer methods, same core API |

This project uses `Interop.zkemkeeper.dll` with direct `new CZKEM()` instantiation, which works regardless of which ProgID is registered.
