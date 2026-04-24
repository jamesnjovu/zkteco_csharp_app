# Elixir Integration Guide

How to call the C# middleware from an Elixir/Phoenix application.

---

## Connection

The middleware runs as a Windows service at `http://<middleware-ip>:5000`. All Elixir HTTP calls go to this base URL.

```elixir
# config/config.exs (in gym_sync_reception — its OTP app name is :app)
config :app, :zkt_middleware,
  base_url: "http://10.121.0.200:5000/api/v1",
  timeout: 30_000
```

---

## HTTP client example

Using `Req` (recommended) or `HTTPoison`:

```elixir
defmodule GymSync.Zkt.Client do
  @base_url Application.compile_env!(:app, [:zkt_middleware, :base_url])
  @timeout Application.compile_env!(:app, [:zkt_middleware, :timeout])

  def call(endpoint, body \\ %{}) do
    url = "#{@base_url}#{endpoint}"

    case Req.post(url, json: body, receive_timeout: @timeout) do
      {:ok, %{status: status, body: %{"ok" => true, "data" => data}}} ->
        {:ok, data}

      {:ok, %{body: %{"ok" => false, "error" => error}}} ->
        {:error, error}

      {:ok, %{status: status, body: body}} ->
        {:error, "HTTP #{status}: #{inspect(body)}"}

      {:error, reason} ->
        {:error, "Connection failed: #{inspect(reason)}"}
    end
  end
end
```

---

## Common operations

### List configured devices

```elixir
{:ok, data} = GymSync.Zkt.Client.call("/devices")
# data = %{
#   "defaultDevice" => %{"ip" => "10.121.0.206", "port" => 4370},
#   "devices" => [
#     %{"name" => "entrance", "ip" => "10.121.0.206", "port" => 4370},
#     %{"name" => "exit", "ip" => "10.121.0.207", "port" => 4370}
#   ]
# }
```

### Create user on a device

```elixir
{:ok, _} = GymSync.Zkt.Client.call("/users/create", %{
  ip: "10.121.0.206",
  port: 4370,
  enrollNumber: "1001",
  name: "John Doe",
  privilege: 0
})
```

### Get all templates from a user

```elixir
{:ok, data} = GymSync.Zkt.Client.call("/templates/all", %{
  ip: "10.121.0.206",
  enrollNumber: "1001"
})

# data = %{
#   "enrollNumber" => "1001",
#   "fingerprints" => [%{"index" => 0, "template" => "base64...", "bytes" => 1246, "flag" => 1}],
#   "faces" => [%{"index" => 50, "template" => "base64...", "bytes" => 18832}],
#   "totalFingerprints" => 1,
#   "totalFaces" => 1
# }
```

### Sync user to all devices (most important call)

```elixir
{:ok, data} = GymSync.Zkt.Client.call("/sync/user/all", %{
  sourceIp: "10.121.0.206",
  enrollNumber: "1001"
})

# data = %{
#   "enrollNumber" => "1001",
#   "source" => "10.121.0.206:4370",
#   "sourceTemplates" => %{"fingerprints" => 2, "faces" => 1},
#   "targets" => [
#     %{
#       "device" => "10.121.0.207:4370",
#       "name" => "exit",
#       "success" => true,
#       "userCreated" => true,
#       "uploadedFingers" => 2,
#       "uploadedFaces" => 1,
#       "errors" => []
#     }
#   ]
# }
```

### Sync user to specific devices

```elixir
{:ok, data} = GymSync.Zkt.Client.call("/sync/user", %{
  sourceIp: "10.121.0.206",
  enrollNumber: "1001",
  targets: [
    %{ip: "10.121.0.207", port: 4370},
    %{ip: "10.121.0.208", port: 4370}
  ]
})
```

### Poll new attendance logs

```elixir
defmodule GymSync.Zkt.AttendancePoller do
  # Call this on a timer (e.g., every 30 seconds) for each device

  def poll_device(device_ip, port \\ 4370) do
    case GymSync.Zkt.Client.call("/attendance/new", %{ip: device_ip, port: port}) do
      {:ok, %{"logs" => logs}} when logs != [] ->
        Enum.each(logs, &process_log/1)
        {:ok, length(logs)}

      {:ok, _} ->
        {:ok, 0}

      {:error, reason} ->
        {:error, reason}
    end
  end

  defp process_log(log) do
    # log = %{
    #   "userId" => "1001",
    #   "timestamp" => "2026-04-18 08:30:15",
    #   "verifyMethod" => 1,     # 0=Password, 1=Fingerprint, 2=Card, 3=Face
    #   "inOutState" => 0,       # 0=Check-In, 1=Check-Out
    #   "workCode" => 0
    # }
    # Save to your database here
  end
end
```

### Delete user from all devices

```elixir
def delete_from_all_devices(enroll_number) do
  {:ok, %{"devices" => devices}} = GymSync.Zkt.Client.call("/devices")

  Enum.map(devices, fn device ->
    result = GymSync.Zkt.Client.call("/users/delete", %{
      ip: device["ip"],
      port: device["port"],
      enrollNumber: enroll_number
    })
    {device["name"], result}
  end)
end
```

---

## Typical registration workflow

```elixir
def register_member(enroll_number, name, enrollment_device_ip) do
  # 1. Create user on enrollment device
  {:ok, _} = Client.call("/users/create", %{
    ip: enrollment_device_ip,
    enrollNumber: enroll_number,
    name: name
  })

  # 2. Start fingerprint enrollment (user must be at the device)
  {:ok, _} = Client.call("/enroll/finger", %{
    ip: enrollment_device_ip,
    enrollNumber: enroll_number,
    fingerIndex: 0
  })

  # 3. Wait for enrollment to complete on device...
  #    (poll or wait for user confirmation in the UI)

  # 4. Sync to all other devices
  {:ok, sync_result} = Client.call("/sync/user/all", %{
    sourceIp: enrollment_device_ip,
    enrollNumber: enroll_number
  })

  {:ok, sync_result}
end
```

---

## Error handling

All errors return `{:error, message}` from the client module. Common errors:

| Error message | Meaning |
|---|---|
| `"Cannot connect to ZKTeco device at ..."` | Device offline or wrong IP |
| `"User X not found"` | User doesn't exist on that device |
| `"Connection failed: ..."` | Middleware itself is unreachable |
| `"enrollNumber required"` | Missing required field |

---

## Full API reference

See [api-reference.md](api-reference.md) for complete endpoint documentation with request/response examples.
