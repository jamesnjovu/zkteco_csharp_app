using System.Text.Json;
using GymSync.Zkt.Core;

namespace GymSync.Zkt.WebUI;

/// <summary>
/// /api/v1/* — Clean REST-style API for the Elixir app to consume.
/// Every request body includes "ip" and "port" to target a specific device.
/// </summary>
public static class ApiV1
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static void MapV1Routes(this WebApplication app, AppConfig cfg, SemaphoreSlim deviceLock)
    {
        var v1 = app.MapGroup("/api/v1");

        // ==================== Connection ====================

        v1.MapPost("/connect", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            return await With(p, deviceLock, client =>
                Ok(new { device = $"{p.Ip}:{p.Port}", connected = true }));
        });

        // Check connection status for one or all configured devices
        v1.MapPost("/status", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var ip = Str(b, "ip");

            // If ip provided, check single device
            if (ip is not null)
            {
                var port = Int(b, "port") ?? cfg.Device.Port;
                var password = Int(b, "password") ?? cfg.Device.Password;
                var timeout = Int(b, "timeout") ?? 5;
                var machine = Int(b, "machineNumber") ?? cfg.Device.MachineNumber;

                try
                {
                    using var client = new DeviceClient(ip, port, machine);
                    client.Connect(password, timeout);
                    var info = client.GetDeviceInfo();
                    return Ok(new
                    {
                        device = $"{ip}:{port}",
                        online = true,
                        serial = info.Serial,
                        product = info.Product,
                        firmware = info.Firmware,
                    });
                }
                catch (Exception e)
                {
                    return Ok(new
                    {
                        device = $"{ip}:{port}",
                        online = false,
                        serial = (string?)null,
                        product = (string?)null,
                        firmware = (string?)null,
                        error = e.Message,
                    });
                }
            }

            // No ip — check ALL configured devices
            var results = new List<object>();
            foreach (var d in cfg.Devices)
            {
                try
                {
                    using var client = new DeviceClient(d.Ip, d.Port, d.MachineNumber > 0 ? d.MachineNumber : cfg.Device.MachineNumber);
                    client.Connect(d.Password, d.Timeout > 0 ? d.Timeout : 5);
                    var info = client.GetDeviceInfo();
                    results.Add(new
                    {
                        name = d.Name,
                        device = $"{d.Ip}:{d.Port}",
                        online = true,
                        serial = info.Serial,
                        product = info.Product,
                        firmware = info.Firmware,
                    });
                }
                catch (Exception e)
                {
                    results.Add(new
                    {
                        name = d.Name,
                        device = $"{d.Ip}:{d.Port}",
                        online = false,
                        serial = (string?)null,
                        product = (string?)null,
                        firmware = (string?)null,
                        error = e.Message,
                    });
                }
            }

            return Ok(new { devices = results, total = results.Count, online = results.Count(r => ((dynamic)r).online) });
        });

        v1.MapGet("/devices", () =>
            Ok(new
            {
                defaultDevice = new { cfg.Device.Ip, cfg.Device.Port },
                devices = cfg.Devices.Select(d => new { d.Name, d.Ip, d.Port })
            }));

        // ==================== Users ====================

        v1.MapPost("/users", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            return await With(p, deviceLock, client =>
            {
                var users = client.ListUsers();
                return Ok(new
                {
                    device = $"{p.Ip}:{p.Port}",
                    count = users.Count,
                    users = users.Select(u => new
                    {
                        enrollNumber = u.EnrollNumber,
                        name = u.Name,
                        privilege = u.Privilege,
                        enabled = u.Enabled,
                    })
                });
            });
        });

        v1.MapPost("/users/get", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var enroll = Str(b, "enrollNumber");
            if (enroll is null) return Err("enrollNumber required", 400);

            return await With(p, deviceLock, client =>
            {
                var user = client.GetUser(enroll);
                if (user is null) return Err($"User {enroll} not found", 404);
                return Ok(new { user = new { user.EnrollNumber, user.Name, user.Privilege, user.Enabled } });
            });
        });

        v1.MapPost("/users/create", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var enroll = Str(b, "enrollNumber");
            if (enroll is null) return Err("enrollNumber required", 400);

            return await With(p, deviceLock, client =>
            {
                client.CreateUser(enroll, Str(b, "name") ?? "", Int(b, "privilege") ?? 0, Str(b, "password") ?? "");
                return Ok(new { enrollNumber = enroll, action = "created" });
            });
        });

        v1.MapPost("/users/update", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var enroll = Str(b, "enrollNumber");
            if (enroll is null) return Err("enrollNumber required", 400);

            return await With(p, deviceLock, client =>
            {
                var existing = client.GetUser(enroll);
                if (existing is null) return Err($"User {enroll} not found", 404);
                client.CreateUser(enroll, Str(b, "name") ?? existing.Name, Int(b, "privilege") ?? existing.Privilege, "", existing.Enabled);
                return Ok(new { enrollNumber = enroll, action = "updated" });
            });
        });

        v1.MapPost("/users/delete", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var enroll = Str(b, "enrollNumber");
            if (enroll is null) return Err("enrollNumber required", 400);

            return await With(p, deviceLock, client =>
            {
                client.DeleteUser(enroll);
                return Ok(new { enrollNumber = enroll, action = "deleted" });
            });
        });

        v1.MapPost("/users/enable", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var enroll = Str(b, "enrollNumber");
            var enable = b.TryGetValue("enable", out var ev) && ev.ValueKind != JsonValueKind.False;
            if (enroll is null) return Err("enrollNumber required", 400);

            return await With(p, deviceLock, client =>
            {
                var existing = client.GetUser(enroll);
                if (existing is null) return Err($"User {enroll} not found", 404);
                client.CreateUser(enroll, existing.Name, existing.Privilege, "", enable);
                return Ok(new { enrollNumber = enroll, enabled = enable });
            });
        });

        v1.MapPost("/users/validity/get", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var enroll = Str(b, "enrollNumber");
            if (enroll is null) return Err("enrollNumber required", 400);

            return await With(p, deviceLock, client =>
            {
                var v = client.GetUserValidDate(enroll);
                return Ok(new { validity = v });
            });
        });

        v1.MapPost("/users/validity/set", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var enroll = Str(b, "enrollNumber");
            if (enroll is null) return Err("enrollNumber required", 400);
            var expires = b.TryGetValue("expires", out var ev) && ev.ValueKind != JsonValueKind.False;

            return await With(p, deviceLock, client =>
            {
                client.SetUserValidDate(enroll, expires, Str(b, "startDate") ?? "", Str(b, "endDate") ?? "");
                return Ok(new { enrollNumber = enroll, expires, startDate = Str(b, "startDate"), endDate = Str(b, "endDate") });
            });
        });

        // ==================== Enrollment ====================

        v1.MapPost("/enroll/finger", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var enroll = Str(b, "enrollNumber");
            if (enroll is null) return Err("enrollNumber required", 400);

            return await With(p, deviceLock, client =>
            {
                client.StartEnrollFingerprint(enroll, Int(b, "fingerIndex") ?? 0);
                return Ok(new { enrollNumber = enroll, fingerIndex = Int(b, "fingerIndex") ?? 0, action = "enrollment_started" });
            });
        });

        v1.MapPost("/enroll/face", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var enroll = Str(b, "enrollNumber");
            if (enroll is null) return Err("enrollNumber required", 400);

            return await With(p, deviceLock, client =>
            {
                client.StartEnrollFace(enroll);
                return Ok(new { enrollNumber = enroll, action = "enrollment_started" });
            });
        });

        // ==================== Templates ====================

        // Get ALL templates for a user (fingerprints + faces) in one call
        v1.MapPost("/templates/all", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var enroll = Str(b, "enrollNumber");
            if (enroll is null) return Err("enrollNumber required", 400);

            return await With(p, deviceLock, client =>
            {
                var templates = client.GetAllTemplates(enroll);
                return Ok(new
                {
                    enrollNumber = enroll,
                    device = $"{p.Ip}:{p.Port}",
                    fingerprints = templates.Fingerprints,
                    faces = templates.Faces,
                    totalFingerprints = templates.Fingerprints.Count,
                    totalFaces = templates.Faces.Count,
                });
            });
        });

        // Upload ALL templates for a user in one call
        v1.MapPost("/templates/upload", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var enroll = Str(b, "enrollNumber");
            if (enroll is null) return Err("enrollNumber required", 400);

            // Parse fingerprints and faces arrays from body
            var templates = ParseTemplates(b, enroll);

            return await With(p, deviceLock, client =>
            {
                var result = client.UploadAllTemplates(enroll, templates);
                return Ok(new
                {
                    enrollNumber = enroll,
                    device = $"{p.Ip}:{p.Port}",
                    uploadedFingers = result.UploadedFingers,
                    uploadedFaces = result.UploadedFaces,
                    errors = result.Errors,
                });
            });
        });

        v1.MapPost("/templates/finger/get", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var enroll = Str(b, "enrollNumber");
            if (enroll is null) return Err("enrollNumber required", 400);
            var finger = Int(b, "fingerIndex") ?? 0;

            return await With(p, deviceLock, client =>
            {
                var tpl = client.GetFingerTemplate(enroll, finger);
                if (tpl is null) return Ok(new { enrollNumber = enroll, fingerIndex = finger, found = false, template = (string?)null });
                return Ok(new { enrollNumber = enroll, fingerIndex = finger, found = true, template = Convert.ToBase64String(tpl), bytes = tpl.Length });
            });
        });

        v1.MapPost("/templates/face/get", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var enroll = Str(b, "enrollNumber");
            if (enroll is null) return Err("enrollNumber required", 400);
            var faceIndex = Int(b, "faceIndex") ?? 50;

            return await With(p, deviceLock, client =>
            {
                var face = client.GetFaceTemplate(enroll, faceIndex);
                if (face is null) return Ok(new { enrollNumber = enroll, faceIndex, found = false, template = (string?)null });
                return Ok(new { enrollNumber = enroll, faceIndex, found = true, template = Convert.ToBase64String(face.Template), bytes = face.Size });
            });
        });

        v1.MapPost("/templates/finger/upload", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var enroll = Str(b, "enrollNumber");
            var template = Str(b, "template");
            if (enroll is null) return Err("enrollNumber required", 400);
            if (template is null) return Err("template (base64) required", 400);

            byte[] tpl;
            try { tpl = Convert.FromBase64String(template); }
            catch { return Err("Invalid base64 template", 400); }

            return await With(p, deviceLock, client =>
            {
                client.SetFingerTemplate(enroll, Int(b, "fingerIndex") ?? 0, tpl);
                return Ok(new { enrollNumber = enroll, fingerIndex = Int(b, "fingerIndex") ?? 0, action = "uploaded" });
            });
        });

        v1.MapPost("/templates/face/upload", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var enroll = Str(b, "enrollNumber");
            var template = Str(b, "template");
            if (enroll is null) return Err("enrollNumber required", 400);
            if (template is null) return Err("template (base64) required", 400);

            byte[] tpl;
            try { tpl = Convert.FromBase64String(template); }
            catch { return Err("Invalid base64 template", 400); }

            var size = Int(b, "bytes") ?? tpl.Length;

            return await With(p, deviceLock, client =>
            {
                client.SetFaceTemplate(enroll, tpl, size, Int(b, "faceIndex") ?? 50);
                return Ok(new { enrollNumber = enroll, faceIndex = Int(b, "faceIndex") ?? 50, action = "uploaded" });
            });
        });

        v1.MapPost("/templates/finger/delete", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var enroll = Str(b, "enrollNumber");
            if (enroll is null) return Err("enrollNumber required", 400);

            return await With(p, deviceLock, client =>
            {
                client.DeleteFingerTemplate(enroll, Int(b, "fingerIndex") ?? 0);
                return Ok(new { enrollNumber = enroll, fingerIndex = Int(b, "fingerIndex") ?? 0, action = "deleted" });
            });
        });

        v1.MapPost("/templates/face/delete", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var enroll = Str(b, "enrollNumber");
            if (enroll is null) return Err("enrollNumber required", 400);

            return await With(p, deviceLock, client =>
            {
                client.DeleteFaceTemplate(enroll, Int(b, "faceIndex") ?? 50);
                return Ok(new { enrollNumber = enroll, faceIndex = Int(b, "faceIndex") ?? 50, action = "deleted" });
            });
        });

        // ==================== Sync ====================

        // Sync user from source device to one or more target devices:
        // 1. Creates user on target if not exists
        // 2. Downloads all templates from source
        // 3. Uploads all templates to each target
        v1.MapPost("/sync/user", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var sourceIp = Str(b, "sourceIp");
            var sourcePort = Int(b, "sourcePort") ?? cfg.Device.Port;
            var enroll = Str(b, "enrollNumber");

            if (sourceIp is null) return Err("sourceIp required", 400);
            if (enroll is null) return Err("enrollNumber required", 400);

            // Parse target devices: [{"ip":"...", "port":4370}, ...]
            var targets = ParseTargets(b);
            if (targets.Count == 0) return Err("targets array required (each with ip, optionally port)", 400);

            var password = Int(b, "password") ?? cfg.Device.Password;
            var timeout = Int(b, "timeout") ?? cfg.Device.Timeout;
            var machine = Int(b, "machineNumber") ?? cfg.Device.MachineNumber;

            await deviceLock.WaitAsync();
            try
            {
                // Step 1: Get user + templates from source
                DeviceUser? sourceUser;
                UserTemplates templates;
                using (var source = new DeviceClient(sourceIp, sourcePort, machine))
                {
                    try { source.Connect(password, timeout); }
                    catch (Exception e) { return Err($"Source device error: {e.Message}", 500); }

                    sourceUser = source.GetUser(enroll);
                    if (sourceUser is null) return Err($"User {enroll} not found on source {sourceIp}:{sourcePort}", 404);
                    templates = source.GetAllTemplates(enroll);
                }

                // Step 2: Sync to each target
                var results = new List<object>();
                foreach (var target in targets)
                {
                    try
                    {
                        using var client = new DeviceClient(target.Ip, target.Port, machine);
                        client.Connect(password, timeout);

                        // Create user if not exists
                        var existing = client.GetUser(enroll);
                        if (existing is null)
                            client.CreateUser(enroll, sourceUser.Name, sourceUser.Privilege);

                        // Upload templates
                        var uploadResult = client.UploadAllTemplates(enroll, templates);
                        results.Add(new
                        {
                            device = $"{target.Ip}:{target.Port}",
                            success = true,
                            userCreated = existing is null,
                            uploadedFingers = uploadResult.UploadedFingers,
                            uploadedFaces = uploadResult.UploadedFaces,
                            errors = uploadResult.Errors,
                        });
                    }
                    catch (Exception e)
                    {
                        results.Add(new
                        {
                            device = $"{target.Ip}:{target.Port}",
                            success = false,
                            userCreated = false,
                            uploadedFingers = 0,
                            uploadedFaces = 0,
                            errors = new List<string> { e.Message },
                        });
                    }
                }

                return Ok(new
                {
                    enrollNumber = enroll,
                    source = $"{sourceIp}:{sourcePort}",
                    sourceTemplates = new { fingerprints = templates.Fingerprints.Count, faces = templates.Faces.Count },
                    targets = results,
                });
            }
            finally { deviceLock.Release(); }
        });

        // Sync a user to ALL configured devices (from config.json "devices" list)
        v1.MapPost("/sync/user/all", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var sourceIp = Str(b, "sourceIp");
            var sourcePort = Int(b, "sourcePort") ?? cfg.Device.Port;
            var enroll = Str(b, "enrollNumber");

            if (sourceIp is null) return Err("sourceIp required", 400);
            if (enroll is null) return Err("enrollNumber required", 400);

            // Use all configured devices except the source as targets
            var targets = cfg.Devices
                .Where(d => !(d.Ip == sourceIp && d.Port == sourcePort))
                .Select(d => (Ip: d.Ip, Port: d.Port))
                .ToList();

            if (targets.Count == 0) return Err("No target devices configured in config.json 'devices' list", 400);

            var password = Int(b, "password") ?? cfg.Device.Password;
            var timeout = Int(b, "timeout") ?? cfg.Device.Timeout;
            var machine = Int(b, "machineNumber") ?? cfg.Device.MachineNumber;

            await deviceLock.WaitAsync();
            try
            {
                using var source = new DeviceClient(sourceIp, sourcePort, machine);
                try { source.Connect(password, timeout); }
                catch (Exception e) { return Err($"Source device error: {e.Message}", 500); }

                var sourceUser = source.GetUser(enroll);
                if (sourceUser is null) return Err($"User {enroll} not found on source {sourceIp}:{sourcePort}", 404);
                var templates = source.GetAllTemplates(enroll);

                var results = new List<object>();
                foreach (var target in targets)
                {
                    try
                    {
                        using var client = new DeviceClient(target.Ip, target.Port, machine);
                        client.Connect(password, timeout);

                        var existing = client.GetUser(enroll);
                        if (existing is null)
                            client.CreateUser(enroll, sourceUser.Name, sourceUser.Privilege);

                        var uploadResult = client.UploadAllTemplates(enroll, templates);
                        results.Add(new
                        {
                            device = $"{target.Ip}:{target.Port}",
                            name = cfg.Devices.FirstOrDefault(d => d.Ip == target.Ip && d.Port == target.Port)?.Name ?? "",
                            success = true,
                            userCreated = existing is null,
                            uploadedFingers = uploadResult.UploadedFingers,
                            uploadedFaces = uploadResult.UploadedFaces,
                            errors = uploadResult.Errors,
                        });
                    }
                    catch (Exception e)
                    {
                        results.Add(new
                        {
                            device = $"{target.Ip}:{target.Port}",
                            name = cfg.Devices.FirstOrDefault(d => d.Ip == target.Ip && d.Port == target.Port)?.Name ?? "",
                            success = false,
                            userCreated = false,
                            uploadedFingers = 0,
                            uploadedFaces = 0,
                            errors = new List<string> { e.Message },
                        });
                    }
                }

                return Ok(new
                {
                    enrollNumber = enroll,
                    source = $"{sourceIp}:{sourcePort}",
                    sourceTemplates = new { fingerprints = templates.Fingerprints.Count, faces = templates.Faces.Count },
                    targets = results,
                });
            }
            finally { deviceLock.Release(); }
        });

        // ==================== Attendance ====================

        v1.MapPost("/attendance/all", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            return await With(p, deviceLock, client =>
            {
                var logs = client.ReadAllAttLogs();
                return Ok(new { device = $"{p.Ip}:{p.Port}", count = logs.Count, logs });
            });
        });

        v1.MapPost("/attendance/new", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            return await With(p, deviceLock, client =>
            {
                var logs = client.ReadNewAttLogs();
                return Ok(new { device = $"{p.Ip}:{p.Port}", count = logs.Count, logs });
            });
        });

        v1.MapPost("/attendance/range", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var start = Str(b, "startDate");
            var end = Str(b, "endDate");
            if (start is null || end is null) return Err("startDate and endDate required (yyyy-MM-dd HH:mm:ss)", 400);

            return await With(p, deviceLock, client =>
            {
                var logs = client.ReadAttLogsByDateRange(start, end);
                return Ok(new { device = $"{p.Ip}:{p.Port}", count = logs.Count, startDate = start, endDate = end, logs });
            });
        });

        v1.MapPost("/attendance/admin", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            return await With(p, deviceLock, client =>
            {
                var logs = client.ReadAdminLogs();
                return Ok(new { device = $"{p.Ip}:{p.Port}", count = logs.Count, logs });
            });
        });

        v1.MapPost("/attendance/clear", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            return await With(p, deviceLock, client =>
            {
                client.ClearAttLogs();
                return Ok(new { device = $"{p.Ip}:{p.Port}", action = "attendance_logs_cleared" });
            });
        });

        v1.MapPost("/attendance/clear-admin", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            return await With(p, deviceLock, client =>
            {
                client.ClearAdminLogs();
                return Ok(new { device = $"{p.Ip}:{p.Port}", action = "admin_logs_cleared" });
            });
        });

        v1.MapPost("/attendance/delete-range", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var start = Str(b, "startDate");
            var end = Str(b, "endDate");
            if (start is null || end is null) return Err("startDate and endDate required", 400);

            return await With(p, deviceLock, client =>
            {
                client.DeleteAttLogsByDateRange(start, end);
                return Ok(new { device = $"{p.Ip}:{p.Port}", action = "deleted", startDate = start, endDate = end });
            });
        });

        v1.MapPost("/attendance/delete-before", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var before = Str(b, "before");
            if (before is null) return Err("before required (yyyy-MM-dd HH:mm:ss)", 400);

            return await With(p, deviceLock, client =>
            {
                client.DeleteAttLogsBefore(before);
                return Ok(new { device = $"{p.Ip}:{p.Port}", action = "deleted", before });
            });
        });

        // ==================== Device ====================

        v1.MapPost("/device/info", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            return await With(p, deviceLock, client =>
            {
                var info = client.GetDeviceInfo();
                return Ok(new { device = $"{p.Ip}:{p.Port}", info });
            });
        });

        v1.MapPost("/device/time", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            return await With(p, deviceLock, client =>
            {
                var t = client.GetDeviceTime();
                return Ok(new
                {
                    device = $"{p.Ip}:{p.Port}",
                    time = $"{t.Year:D4}-{t.Month:D2}-{t.Day:D2} {t.Hour:D2}:{t.Minute:D2}:{t.Second:D2}",
                });
            });
        });

        v1.MapPost("/device/time/sync", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            return await With(p, deviceLock, client =>
            {
                client.SetDeviceTime(DateTime.Now);
                return Ok(new { device = $"{p.Ip}:{p.Port}", action = "time_synced" });
            });
        });

        v1.MapPost("/device/restart", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            return await With(p, deviceLock, client =>
            {
                client.RestartDevice();
                return Ok(new { device = $"{p.Ip}:{p.Port}", action = "restarting" });
            });
        });

        v1.MapPost("/device/voice", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            return await With(p, deviceLock, client =>
            {
                client.PlayVoice(Int(b, "index") ?? 0);
                return Ok(new { device = $"{p.Ip}:{p.Port}", action = "voice_played", index = Int(b, "index") ?? 0 });
            });
        });

        v1.MapPost("/device/door/lock", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            return await With(p, deviceLock, client =>
            {
                client.DoorLock();
                return Ok(new { device = $"{p.Ip}:{p.Port}", action = "locked" });
            });
        });

        v1.MapPost("/device/door/unlock", async (HttpRequest req) =>
        {
            var b = await Body(req);
            var p = Dev(b, cfg);
            var seconds = Int(b, "seconds") ?? 5;
            return await With(p, deviceLock, client =>
            {
                client.DoorUnlock(seconds);
                return Ok(new { device = $"{p.Ip}:{p.Port}", action = "unlocked", seconds });
            });
        });
    }

    // ============================================================================
    // Helpers
    // ============================================================================

    private static IResult Ok(object data) =>
        Results.Json(new { ok = true, data }, Json);

    private static IResult Err(string error, int status) =>
        Results.Json(new { ok = false, error }, Json, statusCode: status);

    private static async Task<Dictionary<string, JsonElement>> Body(HttpRequest req)
    {
        if (req.ContentLength is 0 or null) return new();
        using var doc = await JsonDocument.ParseAsync(req.Body);
        var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
            foreach (var p in doc.RootElement.EnumerateObject()) map[p.Name] = p.Value.Clone();
        return map;
    }

    private static string? Str(Dictionary<string, JsonElement> m, string key) =>
        m.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() :
        m.TryGetValue(key, out var n) && n.ValueKind == JsonValueKind.Number ? n.GetRawText() : null;

    private static int? Int(Dictionary<string, JsonElement> m, string key)
    {
        if (!m.TryGetValue(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    private record DevP(string Ip, int Port, int Password, int Timeout, int MachineNumber);

    private static DevP Dev(Dictionary<string, JsonElement> body, AppConfig cfg) => new(
        Ip: Str(body, "ip") ?? cfg.Device.Ip,
        Port: Int(body, "port") ?? cfg.Device.Port,
        Password: Int(body, "password") ?? cfg.Device.Password,
        Timeout: Int(body, "timeout") ?? cfg.Device.Timeout,
        MachineNumber: Int(body, "machineNumber") ?? cfg.Device.MachineNumber
    );

    private static async Task<IResult> With(DevP p, SemaphoreSlim gate, Func<DeviceClient, IResult> work)
    {
        await gate.WaitAsync();
        try
        {
            using var client = new DeviceClient(p.Ip, p.Port, p.MachineNumber);
            try { client.Connect(p.Password, p.Timeout); }
            catch (Exception e) { return Err(e.Message, 502); }

            try { return work(client); }
            catch (Exception e) { return Err($"{e.GetType().Name}: {e.Message}", 500); }
        }
        finally { gate.Release(); }
    }

    private static List<(string Ip, int Port)> ParseTargets(Dictionary<string, JsonElement> body)
    {
        var list = new List<(string, int)>();
        if (!body.TryGetValue("targets", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var ip = el.TryGetProperty("ip", out var ipv) ? ipv.GetString() : null;
            var port = el.TryGetProperty("port", out var pv) && pv.TryGetInt32(out var pn) ? pn : 4370;
            if (!string.IsNullOrWhiteSpace(ip)) list.Add((ip!, port));
        }
        return list;
    }

    private static UserTemplates ParseTemplates(Dictionary<string, JsonElement> body, string enroll)
    {
        var fingers = new List<FingerTemplateData>();
        var faces = new List<FaceTemplateData>();

        if (body.TryGetValue("fingerprints", out var fpArr) && fpArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in fpArr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var index = el.TryGetProperty("index", out var iv) && iv.TryGetInt32(out var idx) ? idx : 0;
                var tpl = el.TryGetProperty("template", out var tv) ? tv.GetString() : null;
                var flag = el.TryGetProperty("flag", out var fv) && fv.TryGetInt32(out var fl) ? fl : 1;
                var bytes = tpl is not null ? Convert.FromBase64String(tpl).Length : 0;
                if (tpl is not null) fingers.Add(new FingerTemplateData(index, tpl, bytes, flag));
            }
        }

        if (body.TryGetValue("faces", out var faceArr) && faceArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in faceArr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var index = el.TryGetProperty("index", out var iv) && iv.TryGetInt32(out var idx) ? idx : 50;
                var tpl = el.TryGetProperty("template", out var tv) ? tv.GetString() : null;
                var bytes = tpl is not null ? Convert.FromBase64String(tpl).Length : 0;
                if (tpl is not null) faces.Add(new FaceTemplateData(index, tpl, bytes));
            }
        }

        return new UserTemplates(enroll, fingers, faces);
    }
}
