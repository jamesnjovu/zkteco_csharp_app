# ZKTeco C# Middleware — API v1 Documentation

Base URL: `http://<host>:5000/api/v1`

All endpoints use `POST` with `Content-Type: application/json` unless noted otherwise.

Every request body accepts optional device targeting fields. If omitted, defaults from `config.json` are used:
- `ip` (string) — device IP address
- `port` (int, default 4370) — device TCP port
- `password` (int, default 0) — device comm password
- `timeout` (int, default 10) — connection timeout in seconds
- `machineNumber` (int, default 1) — device machine number

Every response follows a consistent envelope:
- Success: `{"ok": true, "data": { ... }}`
- Error: `{"ok": false, "error": "message"}` with appropriate HTTP status code

---

## 1. Connection

### 1.1 Test connection

```
POST /api/v1/connect
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "device": "10.121.0.206:4370",
    "connected": true
  }
}
```

**Error (502):**
```json
{
  "ok": false,
  "error": "Cannot connect to ZKTeco device at 10.121.0.206:4370 (SDK error=-201; check IP, port, and comm password)."
}
```

### 1.2 List configured devices

```
GET /api/v1/devices
```

**Request:** No body required.

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "defaultDevice": { "ip": "10.121.0.206", "port": 4370 },
    "devices": [
      { "name": "entrance", "ip": "10.121.0.206", "port": 4370 },
      { "name": "exit", "ip": "10.121.0.207", "port": 4370 }
    ]
  }
}
```

---

## 2. Users

### 2.1 List all users

```
POST /api/v1/users
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "device": "10.121.0.206:4370",
    "count": 3,
    "users": [
      { "enrollNumber": "1", "name": "Admin", "privilege": 3, "enabled": true },
      { "enrollNumber": "1001", "name": "John Doe", "privilege": 0, "enabled": true },
      { "enrollNumber": "1002", "name": "Jane Smith", "privilege": 0, "enabled": false }
    ]
  }
}
```

### 2.2 Get user by ID

```
POST /api/v1/users/get
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "enrollNumber": "1001"
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "user": {
      "enrollNumber": "1001",
      "name": "John Doe",
      "privilege": 0,
      "enabled": true
    }
  }
}
```

**Error (404):**
```json
{
  "ok": false,
  "error": "User 1001 not found"
}
```

### 2.3 Create user

```
POST /api/v1/users/create
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "enrollNumber": "1003",
  "name": "New User",
  "privilege": 0,
  "password": ""
}
```

Field notes:
- `enrollNumber` (string, required) — unique user ID on the device
- `name` (string, optional, default "") — display name
- `privilege` (int, optional, default 0) — 0=User, 1=Enroller, 2=Manager, 3=Admin
- `password` (string, optional, default "") — device-side password

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1003",
    "action": "created"
  }
}
```

### 2.4 Update user

```
POST /api/v1/users/update
```

Only the fields you provide are changed. The rest are preserved from the existing user record.

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "enrollNumber": "1003",
  "name": "Updated Name",
  "privilege": 1
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1003",
    "action": "updated"
  }
}
```

**Error (404):**
```json
{
  "ok": false,
  "error": "User 1003 not found"
}
```

### 2.5 Delete user

Deletes the user and ALL enrolled data (fingerprints, faces, password, card).

```
POST /api/v1/users/delete
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "enrollNumber": "1003"
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1003",
    "action": "deleted"
  }
}
```

### 2.6 Enable / Disable user

```
POST /api/v1/users/enable
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "enrollNumber": "1001",
  "enable": false
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1001",
    "enabled": false
  }
}
```

### 2.7 Get user validity dates

```
POST /api/v1/users/validity/get
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "enrollNumber": "1001"
}
```

**Response (200) — user has expiry:**
```json
{
  "ok": true,
  "data": {
    "validity": {
      "enrollNumber": "1001",
      "expires": true,
      "validCount": 0,
      "startDate": "2026-01-01",
      "endDate": "2026-12-31"
    }
  }
}
```

**Response (200) — no expiry set:**
```json
{
  "ok": true,
  "data": {
    "validity": {
      "enrollNumber": "1001",
      "expires": false,
      "validCount": 0,
      "startDate": "",
      "endDate": ""
    }
  }
}
```

### 2.8 Set user validity dates

```
POST /api/v1/users/validity/set
```

**Request — set expiry:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "enrollNumber": "1001",
  "expires": true,
  "startDate": "2026-01-01",
  "endDate": "2026-12-31"
}
```

**Request — clear expiry:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "enrollNumber": "1001",
  "expires": false
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1001",
    "expires": true,
    "startDate": "2026-01-01",
    "endDate": "2026-12-31"
  }
}
```

---

## 3. Enrollment

These endpoints start biometric enrollment on the physical device. The user must be present at the device to complete enrollment (place finger on sensor or look at camera).

### 3.1 Start fingerprint enrollment

```
POST /api/v1/enroll/finger
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "enrollNumber": "1001",
  "fingerIndex": 0
}
```

Field notes:
- `fingerIndex` (int, 0-9) — 0=right thumb, 1=right index, ..., 5=left thumb, ..., 9=left little

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1001",
    "fingerIndex": 0,
    "action": "enrollment_started"
  }
}
```

### 3.2 Start face enrollment

```
POST /api/v1/enroll/face
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "enrollNumber": "1001"
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1001",
    "action": "enrollment_started"
  }
}
```

---

## 4. Templates

Templates are biometric data (fingerprint/face) stored as base64 strings. Templates downloaded from one device can be uploaded to another device to sync biometric access.

### 4.1 Get ALL templates for a user

Returns all fingerprint (slots 0-9) and face (slots 50-54) templates in a single call. This is the primary endpoint for downloading templates before syncing to other devices.

```
POST /api/v1/templates/all
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "enrollNumber": "1001"
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1001",
    "device": "10.121.0.206:4370",
    "totalFingerprints": 2,
    "totalFaces": 1,
    "fingerprints": [
      {
        "index": 0,
        "template": "TUzUzMQAA... (base64)",
        "bytes": 1246,
        "flag": 1
      },
      {
        "index": 1,
        "template": "TUzUzMQBB... (base64)",
        "bytes": 1198,
        "flag": 1
      }
    ],
    "faces": [
      {
        "index": 50,
        "template": "RkFDRQ... (base64)",
        "bytes": 18832
      }
    ]
  }
}
```

### 4.2 Upload ALL templates for a user

Uploads multiple fingerprint and face templates in a single call. The format matches the output of `GET /api/v1/templates/all` — you can pass the response directly.

```
POST /api/v1/templates/upload
```

**Request:**
```json
{
  "ip": "10.121.0.207",
  "port": 4370,
  "enrollNumber": "1001",
  "fingerprints": [
    { "index": 0, "template": "TUzUzMQAA... (base64)", "flag": 1 },
    { "index": 1, "template": "TUzUzMQBB... (base64)", "flag": 1 }
  ],
  "faces": [
    { "index": 50, "template": "RkFDRQ... (base64)" }
  ]
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1001",
    "device": "10.121.0.207:4370",
    "uploadedFingers": 2,
    "uploadedFaces": 1,
    "errors": []
  }
}
```

**Response (200) — partial failure:**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1001",
    "device": "10.121.0.207:4370",
    "uploadedFingers": 2,
    "uploadedFaces": 0,
    "errors": ["face[50]: SDK error -5"]
  }
}
```

### 4.3 Get single fingerprint template

```
POST /api/v1/templates/finger/get
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "enrollNumber": "1001",
  "fingerIndex": 0
}
```

**Response (200) — found:**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1001",
    "fingerIndex": 0,
    "found": true,
    "template": "TUzUzMQAA... (base64)",
    "bytes": 1246
  }
}
```

**Response (200) — not enrolled:**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1001",
    "fingerIndex": 0,
    "found": false,
    "template": null
  }
}
```

### 4.4 Get single face template

```
POST /api/v1/templates/face/get
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "enrollNumber": "1001",
  "faceIndex": 50
}
```

**Response (200) — found:**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1001",
    "faceIndex": 50,
    "found": true,
    "template": "RkFDRQ... (base64)",
    "bytes": 18832
  }
}
```

**Response (200) — not enrolled:**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1001",
    "faceIndex": 50,
    "found": false,
    "template": null
  }
}
```

### 4.5 Upload single fingerprint template

```
POST /api/v1/templates/finger/upload
```

**Request:**
```json
{
  "ip": "10.121.0.207",
  "port": 4370,
  "enrollNumber": "1001",
  "fingerIndex": 0,
  "template": "TUzUzMQAA... (base64)"
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1001",
    "fingerIndex": 0,
    "action": "uploaded"
  }
}
```

### 4.6 Upload single face template

```
POST /api/v1/templates/face/upload
```

**Request:**
```json
{
  "ip": "10.121.0.207",
  "port": 4370,
  "enrollNumber": "1001",
  "faceIndex": 50,
  "template": "RkFDRQ... (base64)"
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1001",
    "faceIndex": 50,
    "action": "uploaded"
  }
}
```

### 4.7 Delete fingerprint template

```
POST /api/v1/templates/finger/delete
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "enrollNumber": "1001",
  "fingerIndex": 0
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1001",
    "fingerIndex": 0,
    "action": "deleted"
  }
}
```

### 4.8 Delete face template

```
POST /api/v1/templates/face/delete
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "enrollNumber": "1001",
  "faceIndex": 50
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1001",
    "faceIndex": 50,
    "action": "deleted"
  }
}
```

---

## 5. Sync (Multi-Device Workflows)

These are the key middleware endpoints. They handle the full workflow of copying a user and their biometric templates from a source device to one or more target devices in a single call.

### 5.1 Sync user to specific target devices

This endpoint:
1. Connects to the source device and reads the user record + all templates
2. For each target device: creates the user if they don't exist, then uploads all templates
3. Returns per-device results

```
POST /api/v1/sync/user
```

**Request:**
```json
{
  "sourceIp": "10.121.0.206",
  "sourcePort": 4370,
  "enrollNumber": "1001",
  "targets": [
    { "ip": "10.121.0.207", "port": 4370 },
    { "ip": "10.121.0.208", "port": 4370 }
  ]
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1001",
    "source": "10.121.0.206:4370",
    "sourceTemplates": {
      "fingerprints": 2,
      "faces": 1
    },
    "targets": [
      {
        "device": "10.121.0.207:4370",
        "success": true,
        "userCreated": true,
        "uploadedFingers": 2,
        "uploadedFaces": 1,
        "errors": []
      },
      {
        "device": "10.121.0.208:4370",
        "success": false,
        "userCreated": false,
        "uploadedFingers": 0,
        "uploadedFaces": 0,
        "errors": ["Cannot connect to ZKTeco device at 10.121.0.208:4370 (SDK error=-201; check IP, port, and comm password)."]
      }
    ]
  }
}
```

### 5.2 Sync user to ALL configured devices

Same as 5.1, but automatically targets all devices in the `config.json` `"devices"` array (excluding the source device).

```
POST /api/v1/sync/user/all
```

**Request:**
```json
{
  "sourceIp": "10.121.0.206",
  "sourcePort": 4370,
  "enrollNumber": "1001"
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "enrollNumber": "1001",
    "source": "10.121.0.206:4370",
    "sourceTemplates": {
      "fingerprints": 2,
      "faces": 1
    },
    "targets": [
      {
        "device": "10.121.0.207:4370",
        "name": "exit",
        "success": true,
        "userCreated": false,
        "uploadedFingers": 2,
        "uploadedFaces": 1,
        "errors": []
      }
    ]
  }
}
```

**Error (404) — user not on source:**
```json
{
  "ok": false,
  "error": "User 1001 not found on source 10.121.0.206:4370"
}
```

**Error (400) — no devices configured:**
```json
{
  "ok": false,
  "error": "No target devices configured in config.json 'devices' list"
}
```

---

## 6. Attendance Logs

### 6.1 Get all attendance logs

```
POST /api/v1/attendance/all
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "device": "10.121.0.206:4370",
    "count": 3,
    "logs": [
      {
        "userId": "1001",
        "timestamp": "2026-04-17 08:30:15",
        "verifyMethod": 1,
        "inOutState": 0,
        "workCode": 0
      },
      {
        "userId": "1001",
        "timestamp": "2026-04-17 17:05:42",
        "verifyMethod": 1,
        "inOutState": 1,
        "workCode": 0
      },
      {
        "userId": "1002",
        "timestamp": "2026-04-17 09:12:00",
        "verifyMethod": 3,
        "inOutState": 0,
        "workCode": 0
      }
    ]
  }
}
```

Field reference:
- `verifyMethod`: 0=Password, 1=Fingerprint, 2=Card, 3=Face, 4=Multi
- `inOutState`: 0=Check-In, 1=Check-Out, 2=Break-Out, 3=Break-In, 4=OT-In, 5=OT-Out

### 6.2 Get new attendance logs

Returns only logs recorded since the last read.

```
POST /api/v1/attendance/new
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "device": "10.121.0.206:4370",
    "count": 1,
    "logs": [
      {
        "userId": "1001",
        "timestamp": "2026-04-18 08:15:30",
        "verifyMethod": 1,
        "inOutState": 0,
        "workCode": 0
      }
    ]
  }
}
```

### 6.3 Get attendance logs by date range

```
POST /api/v1/attendance/range
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "startDate": "2026-04-17 00:00:00",
  "endDate": "2026-04-17 23:59:59"
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "device": "10.121.0.206:4370",
    "count": 2,
    "startDate": "2026-04-17 00:00:00",
    "endDate": "2026-04-17 23:59:59",
    "logs": [
      {
        "userId": "1001",
        "timestamp": "2026-04-17 08:30:15",
        "verifyMethod": 1,
        "inOutState": 0,
        "workCode": 0
      },
      {
        "userId": "1001",
        "timestamp": "2026-04-17 17:05:42",
        "verifyMethod": 1,
        "inOutState": 1,
        "workCode": 0
      }
    ]
  }
}
```

### 6.4 Get admin / operation logs

```
POST /api/v1/attendance/admin
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "device": "10.121.0.206:4370",
    "count": 1,
    "logs": [
      {
        "admin": "1",
        "target": "1001",
        "manipulation": 46,
        "timestamp": "2026-04-17 10:00:00"
      }
    ]
  }
}
```

### 6.5 Clear all attendance logs

```
POST /api/v1/attendance/clear
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "device": "10.121.0.206:4370",
    "action": "attendance_logs_cleared"
  }
}
```

### 6.6 Clear admin logs

```
POST /api/v1/attendance/clear-admin
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "device": "10.121.0.206:4370",
    "action": "admin_logs_cleared"
  }
}
```

### 6.7 Delete attendance logs by date range

```
POST /api/v1/attendance/delete-range
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "startDate": "2026-01-01 00:00:00",
  "endDate": "2026-03-31 23:59:59"
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "device": "10.121.0.206:4370",
    "action": "deleted",
    "startDate": "2026-01-01 00:00:00",
    "endDate": "2026-03-31 23:59:59"
  }
}
```

### 6.8 Delete attendance logs before date

```
POST /api/v1/attendance/delete-before
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "before": "2026-04-01 00:00:00"
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "device": "10.121.0.206:4370",
    "action": "deleted",
    "before": "2026-04-01 00:00:00"
  }
}
```

---

## 7. Device Management

### 7.1 Get device info

```
POST /api/v1/device/info
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "device": "10.121.0.206:4370",
    "info": {
      "serial": "ABCD1234567890",
      "firmware": "Ver 6.60 Oct 29 2019",
      "platform": "ZMM220_TFT",
      "vendor": "ZKTeco",
      "product": "iFace800",
      "sdkVersion": "6.3.1.24",
      "mac": "00:17:61:XX:XX:XX",
      "userCount": 50,
      "userCapacity": 10000,
      "fingerprintCount": 120,
      "fingerprintCapacity": 3000,
      "faceCount": 45,
      "faceCapacity": 1200,
      "attLogCount": 5000,
      "attLogCapacity": 100000
    }
  }
}
```

### 7.2 Get device time

```
POST /api/v1/device/time
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "device": "10.121.0.206:4370",
    "time": "2026-04-18 10:30:45"
  }
}
```

### 7.3 Sync device time to host

Sets the device clock to the current time of the machine running this middleware.

```
POST /api/v1/device/time/sync
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "device": "10.121.0.206:4370",
    "action": "time_synced"
  }
}
```

### 7.4 Restart device

```
POST /api/v1/device/restart
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "device": "10.121.0.206:4370",
    "action": "restarting"
  }
}
```

### 7.5 Play voice

```
POST /api/v1/device/voice
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "index": 0
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "device": "10.121.0.206:4370",
    "action": "voice_played",
    "index": 0
  }
}
```

### 7.6 Lock door

```
POST /api/v1/device/door/lock
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370
}
```

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "device": "10.121.0.206:4370",
    "action": "locked"
  }
}
```

### 7.7 Unlock door

```
POST /api/v1/device/door/unlock
```

**Request:**
```json
{
  "ip": "10.121.0.206",
  "port": 4370,
  "seconds": 5
}
```

Field notes:
- `seconds` (int, default 5) — how long the door stays unlocked

**Response (200):**
```json
{
  "ok": true,
  "data": {
    "device": "10.121.0.206:4370",
    "action": "unlocked",
    "seconds": 5
  }
}
```

---

## Typical Elixir Workflow

### Register new member on all devices

```
1. POST /api/v1/users/create          — create user on enrollment device
   body: { ip: "10.121.0.206", enrollNumber: "1001", name: "John Doe" }

2. POST /api/v1/enroll/finger         — start fingerprint enrollment (user places finger)
   body: { ip: "10.121.0.206", enrollNumber: "1001", fingerIndex: 0 }

3. POST /api/v1/enroll/face           — start face enrollment (user looks at camera)
   body: { ip: "10.121.0.206", enrollNumber: "1001" }

4. POST /api/v1/sync/user/all         — sync user + templates to all other devices
   body: { sourceIp: "10.121.0.206", enrollNumber: "1001" }
```

### Remove member from all devices

```
For each device in config:
  POST /api/v1/users/delete
  body: { ip: "<device_ip>", enrollNumber: "1001" }
```

### Poll attendance from all devices

```
For each device in config:
  POST /api/v1/attendance/new
  body: { ip: "<device_ip>" }
```

---

## Error Codes

| HTTP Status | Meaning |
|---|---|
| 200 | Success (`ok: true`) |
| 400 | Bad request — missing or invalid parameters |
| 404 | Resource not found (user not on device) |
| 500 | SDK or internal error |
| 502 | Cannot connect to device |

---

## config.json Reference

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
    "host": "127.0.0.1",
    "port": 5000
  }
}
```

- `device` — default device used when `ip`/`port` are omitted from requests
- `devices` — list of all devices, used by `/api/v1/sync/user/all` and `/api/v1/devices`
- `web.host` — set to `"0.0.0.0"` to accept connections from other machines on the network
