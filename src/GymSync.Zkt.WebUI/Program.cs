using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using GymSync.Zkt.Core;

[assembly: SupportedOSPlatform("windows")]

// COM interop DLL is not in deps.json — resolve it from the output directory.
System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (ctx, name) =>
{
    if (name.Name == "Interop.zkemkeeper")
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Interop.zkemkeeper.dll");
        if (File.Exists(path)) return ctx.LoadFromAssemblyPath(path);
    }
    return null;
};

// --- Resolve project root (two levels up from bin/Debug/net8.0-windows) --------------
string ProjectRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "config.example.json")))
        dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

var root = ProjectRoot();
var cfg = ConfigLoader.Load(root);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://{cfg.Web.Host}:{cfg.Web.Port}");
builder.Services.AddSingleton(cfg);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Device access must be serialized — the device only talks to one client at a time.
var deviceLock = new SemaphoreSlim(1, 1);

var jsonOpts = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false,
};

// ------------- /api/config -----------------
app.MapGet("/api/config", () =>
    Results.Json(new
    {
        ok = true,
        device = cfg.Device,
        storagePath = cfg.Storage.Path,
    }, jsonOpts));

// ------------- /api/connect ----------------
app.MapPost("/api/connect", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);

    return await WithDeviceAsync(p, deviceLock, client =>
        Results.Json(new { ok = true, message = $"Connected to {p.Ip}:{p.Port}" }, jsonOpts));
});

// ------------- /api/users ------------------
app.MapPost("/api/users", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        var users = client.ListUsers();
        return Results.Json(new
        {
            ok = true,
            device = $"{p.Ip}:{p.Port}",
            count = users.Count,
            users = users.Select(u => new
            {
                enrollNumber = u.EnrollNumber,
                name = u.Name,
                privilege = u.Privilege,
                enabled = u.Enabled,
            })
        }, jsonOpts);
    });
});

// ------------- /api/user -------------------
app.MapPost("/api/user", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var enroll = GetStr(body, "enrollNumber");
    if (string.IsNullOrWhiteSpace(enroll)) return Err("Supply enrollNumber", 400);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        var user = client.GetUser(enroll);
        if (user is null) return Err($"User {enroll} not found on device", 404);
        return Results.Json(new
        {
            ok = true,
            user = new
            {
                enrollNumber = user.EnrollNumber,
                name = user.Name,
                privilege = user.Privilege,
                enabled = user.Enabled,
            }
        }, jsonOpts);
    });
});

// ------------- /api/user/create ------------
app.MapPost("/api/user/create", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var enroll = GetStr(body, "enrollNumber");
    var name = GetStr(body, "name") ?? "";
    var privilege = GetInt(body, "privilege") ?? 0;
    var password = GetStr(body, "password") ?? "";

    if (string.IsNullOrWhiteSpace(enroll)) return Err("Supply enrollNumber", 400);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.CreateUser(enroll, name, privilege, password);
        return Results.Json(new { ok = true, message = $"User {enroll} created" }, jsonOpts);
    });
});

// ------------- /api/user/update ------------
app.MapPost("/api/user/update", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var enroll = GetStr(body, "enrollNumber");
    if (string.IsNullOrWhiteSpace(enroll)) return Err("Supply enrollNumber", 400);

    var name = GetStr(body, "name");
    var privilege = GetInt(body, "privilege");

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        var existing = client.GetUser(enroll);
        if (existing is null) return Err($"User {enroll} not found on device", 404);

        client.CreateUser(
            enroll,
            name ?? existing.Name,
            privilege ?? existing.Privilege,
            "",
            existing.Enabled);

        return Results.Json(new { ok = true, message = $"User {enroll} updated" }, jsonOpts);
    });
});

// ------------- /api/user/enable ------------
app.MapPost("/api/user/enable", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var enroll = GetStr(body, "enrollNumber");
    var enable = body.TryGetValue("enable", out var v) && v.ValueKind != JsonValueKind.False;
    if (string.IsNullOrWhiteSpace(enroll)) return Err("Supply enrollNumber", 400);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        var existing = client.GetUser(enroll);
        if (existing is null) return Err($"User {enroll} not found on device", 404);

        client.CreateUser(enroll, existing.Name, existing.Privilege, "", enable);
        return Results.Json(new { ok = true, message = $"User {enroll} {(enable ? "enabled" : "disabled")}" }, jsonOpts);
    });
});

// ------------- /api/user/delete ------------
app.MapPost("/api/user/delete", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var enroll = GetStr(body, "enrollNumber");
    if (string.IsNullOrWhiteSpace(enroll)) return Err("Supply enrollNumber", 400);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.DeleteUser(enroll);
        return Results.Json(new { ok = true, message = $"User {enroll} deleted" }, jsonOpts);
    });
});

// ------------- /api/enroll/finger ----------
app.MapPost("/api/enroll/finger", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var enroll = GetStr(body, "enrollNumber");
    var finger = GetInt(body, "fingerIndex") ?? 0;
    if (string.IsNullOrWhiteSpace(enroll)) return Err("Supply enrollNumber", 400);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.StartEnrollFingerprint(enroll, finger);
        return Results.Json(new { ok = true, message = $"Fingerprint enrollment started for {enroll} finger={finger}. Follow device prompts." }, jsonOpts);
    });
});

// ------------- /api/enroll/face ------------
app.MapPost("/api/enroll/face", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var enroll = GetStr(body, "enrollNumber");
    if (string.IsNullOrWhiteSpace(enroll)) return Err("Supply enrollNumber", 400);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.StartEnrollFace(enroll);
        return Results.Json(new { ok = true, message = $"Face enrollment started for {enroll}. Follow device prompts." }, jsonOpts);
    });
});

// ------------- /api/template/finger --------
app.MapPost("/api/template/finger", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var enroll = GetStr(body, "enrollNumber");
    var finger = GetInt(body, "fingerIndex") ?? 0;
    if (string.IsNullOrWhiteSpace(enroll)) return Err("Supply enrollNumber", 400);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        var tpl = client.GetFingerTemplate(enroll, finger);
        if (tpl is null)
            return Results.Json(new { ok = true, found = false, enrollNumber = enroll, fingerIndex = finger, template = (string?)null }, jsonOpts);

        var b64 = Convert.ToBase64String(tpl);
        return Results.Json(new { ok = true, found = true, enrollNumber = enroll, fingerIndex = finger, bytes = tpl.Length, template = b64 }, jsonOpts);
    });
});

// ------------- /api/template/face ----------
app.MapPost("/api/template/face", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var enroll = GetStr(body, "enrollNumber");
    var faceIndex = GetInt(body, "faceIndex") ?? 50;
    if (string.IsNullOrWhiteSpace(enroll)) return Err("Supply enrollNumber", 400);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        var tpl = client.GetFaceTemplate(enroll, faceIndex);
        if (tpl is null)
            return Results.Json(new { ok = true, found = false, enrollNumber = enroll, faceIndex, template = (string?)null }, jsonOpts);

        var b64 = Convert.ToBase64String(tpl);
        return Results.Json(new { ok = true, found = true, enrollNumber = enroll, faceIndex, bytes = tpl.Length, template = b64 }, jsonOpts);
    });
});

// ------------- /api/template/finger/upload -
app.MapPost("/api/template/finger/upload", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var enroll = GetStr(body, "enrollNumber");
    var finger = GetInt(body, "fingerIndex") ?? 0;
    var template = GetStr(body, "template");
    if (string.IsNullOrWhiteSpace(enroll)) return Err("Supply enrollNumber", 400);
    if (string.IsNullOrWhiteSpace(template)) return Err("Supply template (base64)", 400);

    byte[] tpl;
    try { tpl = Convert.FromBase64String(template); }
    catch { return Err("Invalid base64 template", 400); }

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.SetFingerTemplate(enroll, finger, tpl);
        return Results.Json(new { ok = true, message = $"Fingerprint template uploaded for {enroll} finger={finger}" }, jsonOpts);
    });
});

// ------------- /api/template/face/upload ---
app.MapPost("/api/template/face/upload", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var enroll = GetStr(body, "enrollNumber");
    var faceIndex = GetInt(body, "faceIndex") ?? 50;
    var template = GetStr(body, "template");
    if (string.IsNullOrWhiteSpace(enroll)) return Err("Supply enrollNumber", 400);
    if (string.IsNullOrWhiteSpace(template)) return Err("Supply template (base64)", 400);

    byte[] tpl;
    try { tpl = Convert.FromBase64String(template); }
    catch { return Err("Invalid base64 template", 400); }

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.SetFaceTemplate(enroll, tpl, faceIndex);
        return Results.Json(new { ok = true, message = $"Face template uploaded for {enroll} face={faceIndex}" }, jsonOpts);
    });
});

// ------------- /api/attendance/all ----------
app.MapPost("/api/attendance/all", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        var logs = client.ReadAllAttLogs();
        return Results.Json(new { ok = true, count = logs.Count, logs }, jsonOpts);
    });
});

// ------------- /api/attendance/new ----------
app.MapPost("/api/attendance/new", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        var logs = client.ReadNewAttLogs();
        return Results.Json(new { ok = true, count = logs.Count, logs }, jsonOpts);
    });
});

// ------------- /api/attendance/range --------
app.MapPost("/api/attendance/range", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var startDate = GetStr(body, "startDate");
    var endDate = GetStr(body, "endDate");
    if (string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(endDate))
        return Err("Supply startDate and endDate (yyyy-MM-dd HH:mm:ss)", 400);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        var logs = client.ReadAttLogsByDateRange(startDate, endDate);
        return Results.Json(new { ok = true, count = logs.Count, startDate, endDate, logs }, jsonOpts);
    });
});

// ------------- /api/attendance/admin --------
app.MapPost("/api/attendance/admin", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        var logs = client.ReadAdminLogs();
        return Results.Json(new { ok = true, count = logs.Count, logs }, jsonOpts);
    });
});

// ------------- /api/attendance/clear --------
app.MapPost("/api/attendance/clear", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.ClearAttLogs();
        return Results.Json(new { ok = true, message = "Attendance logs cleared" }, jsonOpts);
    });
});

// ------------- /api/attendance/clear-admin --
app.MapPost("/api/attendance/clear-admin", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.ClearAdminLogs();
        return Results.Json(new { ok = true, message = "Admin logs cleared" }, jsonOpts);
    });
});

// ------------- /api/attendance/delete-range -
app.MapPost("/api/attendance/delete-range", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var startDate = GetStr(body, "startDate");
    var endDate = GetStr(body, "endDate");
    if (string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(endDate))
        return Err("Supply startDate and endDate (yyyy-MM-dd HH:mm:ss)", 400);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.DeleteAttLogsByDateRange(startDate, endDate);
        return Results.Json(new { ok = true, message = $"Attendance logs deleted from {startDate} to {endDate}" }, jsonOpts);
    });
});

// ------------- /api/attendance/delete-before
app.MapPost("/api/attendance/delete-before", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var before = GetStr(body, "before");
    if (string.IsNullOrWhiteSpace(before))
        return Err("Supply before (yyyy-MM-dd HH:mm:ss)", 400);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.DeleteAttLogsBefore(before);
        return Results.Json(new { ok = true, message = $"Attendance logs before {before} deleted" }, jsonOpts);
    });
});

// ------------- /api/template/finger/delete -
app.MapPost("/api/template/finger/delete", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var enroll = GetStr(body, "enrollNumber");
    var finger = GetInt(body, "fingerIndex") ?? 0;
    if (string.IsNullOrWhiteSpace(enroll)) return Err("Supply enrollNumber", 400);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.DeleteFingerTemplate(enroll, finger);
        return Results.Json(new { ok = true, message = $"Fingerprint template deleted for {enroll} finger={finger}" }, jsonOpts);
    });
});

// ------------- /api/template/face/delete ---
app.MapPost("/api/template/face/delete", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var enroll = GetStr(body, "enrollNumber");
    var faceIndex = GetInt(body, "faceIndex") ?? 50;
    if (string.IsNullOrWhiteSpace(enroll)) return Err("Supply enrollNumber", 400);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.DeleteFaceTemplate(enroll, faceIndex);
        return Results.Json(new { ok = true, message = $"Face template deleted for {enroll} face={faceIndex}" }, jsonOpts);
    });
});

// ------------- /api/user/validity ----------
app.MapPost("/api/user/validity", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var enroll = GetStr(body, "enrollNumber");
    if (string.IsNullOrWhiteSpace(enroll)) return Err("Supply enrollNumber", 400);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        var v = client.GetUserValidDate(enroll);
        return Results.Json(new { ok = true, validity = v }, jsonOpts);
    });
});

// ------------- /api/user/validity/set ------
app.MapPost("/api/user/validity/set", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var enroll = GetStr(body, "enrollNumber");
    var startDate = GetStr(body, "startDate") ?? "";
    var endDate = GetStr(body, "endDate") ?? "";
    var expires = body.TryGetValue("expires", out var ev) && ev.ValueKind != JsonValueKind.False;
    if (string.IsNullOrWhiteSpace(enroll)) return Err("Supply enrollNumber", 400);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.SetUserValidDate(enroll, expires, startDate, endDate);
        return Results.Json(new { ok = true, message = $"Validity set for {enroll}: {(expires ? $"{startDate} to {endDate}" : "no expiry")}" }, jsonOpts);
    });
});

// ------------- /api/device/restart ---------
app.MapPost("/api/device/restart", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.RestartDevice();
        return Results.Json(new { ok = true, message = "Device restarting..." }, jsonOpts);
    });
});

// ------------- /api/device/voice -----------
app.MapPost("/api/device/voice", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var index = GetInt(body, "index") ?? 0;

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.PlayVoice(index);
        return Results.Json(new { ok = true, message = $"Voice {index} played" }, jsonOpts);
    });
});

// ------------- /api/device/door/lock -------
app.MapPost("/api/device/door/lock", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.DoorLock();
        return Results.Json(new { ok = true, message = "Door locked" }, jsonOpts);
    });
});

// ------------- /api/device/door/unlock -----
app.MapPost("/api/device/door/unlock", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var seconds = GetInt(body, "seconds") ?? 5;

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.DoorUnlock(seconds);
        return Results.Json(new { ok = true, message = $"Door unlocked for {seconds}s" }, jsonOpts);
    });
});

// ------------- /api/device/time ------------
app.MapPost("/api/device/time", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        var t = client.GetDeviceTime();
        return Results.Json(new
        {
            ok = true,
            time = $"{t.Year:D4}-{t.Month:D2}-{t.Day:D2} {t.Hour:D2}:{t.Minute:D2}:{t.Second:D2}",
            raw = t,
        }, jsonOpts);
    });
});

// ------------- /api/device/time/set --------
app.MapPost("/api/device/time/set", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.SetDeviceTime(DateTime.Now);
        return Results.Json(new { ok = true, message = "Device time synced to host" }, jsonOpts);
    });
});

// ------------- /api/device/info ------------
app.MapPost("/api/device/info", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        var info = client.GetDeviceInfo();
        return Results.Json(new { ok = true, info }, jsonOpts);
    });
});

// ------------- /api/storage ----------------
app.MapGet("/api/storage", () =>
{
    var store = new TemplateStore(cfg.Storage.Path);
    var items = store.Enumerate().Select(e => new
    {
        deviceIp = e.DeviceIp,
        enrollNumber = e.EnrollNumber,
        hasManifest = e.Manifest is not null,
        name = e.Manifest?.Name ?? "",
        fingers = e.Manifest?.Fingerprints.Count ?? 0,
        faces = e.Manifest?.Faces.Count ?? 0,
        downloadedAt = e.Manifest?.DownloadedAt ?? "",
    }).ToList();

    return Results.Json(new { ok = true, items }, jsonOpts);
});

app.Run();

// ============================================================================
// Helpers
// ============================================================================

static async Task<Dictionary<string, JsonElement>> ReadBodyAsync(HttpRequest req)
{
    if (req.ContentLength is 0 or null) return new();
    using var doc = await JsonDocument.ParseAsync(req.Body);
    var root = doc.RootElement;
    var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
    if (root.ValueKind == JsonValueKind.Object)
        foreach (var prop in root.EnumerateObject()) map[prop.Name] = prop.Value.Clone();
    return map;
}

static string? GetStr(Dictionary<string, JsonElement> m, string key) =>
    m.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() :
    m.TryGetValue(key, out var n) && n.ValueKind == JsonValueKind.Number ? n.GetRawText() : null;

static int? GetInt(Dictionary<string, JsonElement> m, string key)
{
    if (!m.TryGetValue(key, out var v)) return null;
    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
    if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
    return null;
}

static DeviceParams DeviceParams(Dictionary<string, JsonElement> body, AppConfig cfg) => new(
    Ip: GetStr(body, "ip") ?? cfg.Device.Ip,
    Port: GetInt(body, "port") ?? cfg.Device.Port,
    Password: GetInt(body, "password") ?? cfg.Device.Password,
    Timeout: GetInt(body, "timeout") ?? cfg.Device.Timeout,
    MachineNumber: GetInt(body, "machineNumber") ?? cfg.Device.MachineNumber,
    StoragePath: cfg.Storage.Path
);

static IResult Err(string message, int code) =>
    Results.Json(new { ok = false, error = message }, statusCode: code);

static async Task<IResult> WithDeviceAsync(DeviceParams p, SemaphoreSlim gate, Func<DeviceClient, IResult> work)
{
    await gate.WaitAsync();
    try
    {
        using var client = new DeviceClient(p.Ip, p.Port, p.MachineNumber);
        try { client.Connect(p.Password, p.Timeout); }
        catch (Exception e) { return Err(e.Message, 500); }

        try { return work(client); }
        catch (Exception e) { return Err($"{e.GetType().Name}: {e.Message}", 500); }
    }
    finally { gate.Release(); }
}

sealed record DeviceParams(string Ip, int Port, int Password, int Timeout, int MachineNumber, string StoragePath);
