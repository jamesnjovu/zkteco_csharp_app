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

// ------------- /api/download ---------------
app.MapPost("/api/download", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var enrollArg = GetStr(body, "enrollNumber");
    var doAll = GetBool(body, "all");

    if (!doAll && string.IsNullOrWhiteSpace(enrollArg))
        return Err("Supply enrollNumber or set all=true", 400);

    var store = new TemplateStore(p.StoragePath);
    var summaries = new List<Manifest>();
    var totals = new { users = 0, fingers = 0, faces = 0 };
    int users = 0, fingers = 0, faces = 0;

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.Disable();

        var targets = doAll
            ? client.ListUsers().Select(u => (u.EnrollNumber, u.Name)).ToList()
            : new List<(string, string)> { (enrollArg!, "") };

        foreach (var (enroll, name) in targets)
        {
            if (string.IsNullOrWhiteSpace(enroll)) continue;

            var manifest = new Manifest
            {
                DeviceIp = p.Ip,
                DevicePort = p.Port,
                EnrollNumber = enroll,
                Name = name,
                DownloadedAt = DateTime.UtcNow.ToString("o"),
            };

            foreach (var fid in DeviceClient.FingerSlots)
            {
                var tpl = client.GetFingerTemplate(enroll, fid);
                if (tpl is null) continue;
                var path = store.WriteFingerprint(p.Ip, enroll, fid, tpl);
                manifest.Fingerprints.Add(new TemplateBlob(fid, Path.GetFileName(path), tpl.Length, TemplateStore.Sha256Hex(tpl)));
                fingers++;
            }

            foreach (var faceid in DeviceClient.FaceSlots)
            {
                var tpl = client.GetFaceTemplate(enroll, faceid);
                if (tpl is null) continue;
                var path = store.WriteFace(p.Ip, enroll, faceid, tpl);
                manifest.Faces.Add(new TemplateBlob(faceid, Path.GetFileName(path), tpl.Length, TemplateStore.Sha256Hex(tpl)));
                faces++;
            }

            if (manifest.Fingerprints.Count > 0 || manifest.Faces.Count > 0)
            {
                store.WriteManifest(p.Ip, enroll, manifest);
                users++;
                summaries.Add(manifest);
            }
        }

        return Results.Json(new
        {
            ok = true,
            totals = new { users, fingers, faces },
            manifests = summaries,
        }, jsonOpts);
    });
});

// ------------- /api/upload -----------------
app.MapPost("/api/upload", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var p = DeviceParams(body, cfg);
    var enroll = GetStr(body, "enrollNumber");
    if (string.IsNullOrWhiteSpace(enroll)) return Err("Supply enrollNumber", 400);

    var sourceIp = GetStr(body, "sourceIp") ?? p.Ip;
    var targetEnroll = GetStr(body, "targetEnrollNumber") ?? enroll;
    var skipFingers = GetBool(body, "skipFingers");
    var skipFaces = GetBool(body, "skipFaces");

    var store = new TemplateStore(p.StoragePath);
    Manifest manifest;
    try { manifest = store.ReadManifest(sourceIp, enroll); }
    catch (Exception e) { return Err(e.Message, 404); }

    return await WithDeviceAsync(p, deviceLock, client =>
    {
        client.Disable();

        var existing = client.ListUsers().FirstOrDefault(u => u.EnrollNumber == targetEnroll);
        if (existing is null)
            return Err($"Target enrollNumber {targetEnroll} not found on device. Create the user record on the device first.", 404);

        int uploadedFingers = 0, uploadedFaces = 0;

        if (!skipFingers)
        {
            foreach (var t in manifest.Fingerprints)
            {
                var tpl = store.ReadTemplate(sourceIp, enroll, t.File);
                client.SetFingerTemplate(targetEnroll, t.Slot, tpl);
                uploadedFingers++;
            }
        }

        if (!skipFaces)
        {
            foreach (var t in manifest.Faces)
            {
                var tpl = store.ReadTemplate(sourceIp, enroll, t.File);
                client.SetFaceTemplate(targetEnroll, tpl, t.Slot);
                uploadedFaces++;
            }
        }

        return Results.Json(new { ok = true, uploadedFingers, uploadedFaces }, jsonOpts);
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

static bool GetBool(Dictionary<string, JsonElement> m, string key) =>
    m.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.True;

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
        catch (Exception e) { return Err($"{e.GetType().Name}: {e.Message}\n{e.StackTrace}", 500); }
    }
    finally { gate.Release(); }
}

sealed record DeviceParams(string Ip, int Port, int Password, int Timeout, int MachineNumber, string StoragePath);
