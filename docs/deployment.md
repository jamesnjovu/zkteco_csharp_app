# Deploying GymSync.Zkt to a Client PC

Build on your dev PC, copy one folder to the client, double-click to install.

---

## On your dev PC: Build

```powershell
cd C:\Users\v1p3r\Documents\Softwares\zkteco_csharp_app
.\build-release.ps1
```

This creates `dist\GymSyncZkt\` with everything bundled:

```
dist\GymSyncZkt\
├── INSTALL.bat          ← double-click on client PC
├── UNINSTALL.bat        ← removes service
├── install.ps1          ← PowerShell installer
├── uninstall.ps1        ← PowerShell uninstaller
├── config.json          ← edit before or after install
├── app\                 ← self-contained app (no .NET install needed)
│   ├── GymSync.Zkt.WebUI.exe
│   ├── Interop.zkemkeeper.dll
│   ├── tcpcomm.dll, comms.dll, ...
│   └── wwwroot\
└── sdk\                 ← ZKTeco COM DLLs
    ├── x64\
    └── x86\
```

---

## On the client PC: Install

### Step 1 — Copy

Copy the `dist\GymSyncZkt\` folder to the client PC (USB, network share, whatever).

### Step 2 — Edit config

Open `config.json` and set the device IPs for this client's network:

```json
{
  "device": {
    "ip": "192.168.1.201",
    "port": 4370
  },
  "devices": [
    { "name": "entrance", "ip": "192.168.1.201", "port": 4370 },
    { "name": "exit", "ip": "192.168.1.202", "port": 4370 }
  ],
  "web": {
    "host": "0.0.0.0",
    "port": 5000
  }
}
```

### Step 3 — Install

Right-click **`INSTALL.bat`** and select **Run as Administrator**.

The installer will:
1. Copy app files to `C:\GymSync\`
2. Register the ZKTeco COM DLL
3. Create `config.json` (if not already there)
4. Install a Windows service called `GymSyncZkt`
5. Open firewall port 5000
6. Start the service

That's it. The service starts automatically on boot.

---

## After install

| Task | Command (elevated PowerShell) |
|---|---|
| Check status | `sc.exe query GymSyncZkt` |
| Stop | `sc.exe stop GymSyncZkt` |
| Start | `sc.exe start GymSyncZkt` |
| Restart | `sc.exe stop GymSyncZkt; Start-Sleep 2; sc.exe start GymSyncZkt` |
| Edit config | Edit `C:\GymSync\config.json`, then restart |
| View test UI | Open `http://localhost:5000` in browser |
| Uninstall | Right-click `UNINSTALL.bat` -> Run as Administrator |

## Updating

1. Build a new release on your dev PC: `.\build-release.ps1`
2. Copy the new `dist\GymSyncZkt\` to the client
3. Right-click `INSTALL.bat` -> Run as Administrator

The installer stops the old service, replaces the files, and restarts. `config.json` on the client is preserved if it already exists.

---

## Custom install location

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1 -InstallDir "D:\MyApp" -Port 8080
```
