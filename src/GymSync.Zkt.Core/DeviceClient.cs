using System.Runtime.Versioning;
using zkemkeeper;

namespace GymSync.Zkt.Core;

/// <summary>
/// Wrapper around the ZKTeco <c>CZKEM</c> COM object via Interop.zkemkeeper.dll.
/// All COM calls are marshalled onto a dedicated STA thread via <see cref="StaExecutor"/>
/// because CZKEM requires single-threaded apartment and ASP.NET Core uses MTA threads.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DeviceClient : IDisposable
{
    public int MachineNumber { get; }
    public string Ip { get; }
    public int Port { get; }

    private readonly StaExecutor _sta = new("ZkSdk");
    private CZKEM? _czkem;
    private bool _connected;
    private bool _disabled;

    public DeviceClient(string ip, int port = 4370, int machineNumber = 1)
    {
        Ip = ip;
        Port = port;
        MachineNumber = machineNumber;
    }

    public void Connect(int commPassword = 0, int timeoutSeconds = 10)
    {
        _sta.Invoke(() =>
        {
            if (_czkem is not null)
            {
                try { _czkem.Disconnect(); } catch { }
            }
            _czkem = new CZKEM();

            if (commPassword != 0)
            {
                try { _czkem.SetCommPassword(commPassword); } catch { }
            }

            if (!_czkem.Connect_Net(Ip, Port))
            {
                int errCode = 0;
                try { _czkem.GetLastError(ref errCode); } catch { }
                throw new IOException(
                    $"Cannot connect to ZKTeco device at {Ip}:{Port} (SDK error={errCode}; check IP, port, and comm password).");
            }

            _connected = true;
        });
    }

    public void Disable()
    {
        _sta.Invoke(() =>
        {
            Require();
            _czkem!.EnableDevice(MachineNumber, false);
            _disabled = true;
        });
    }

    public void Enable()
    {
        _sta.Invoke(() =>
        {
            if (!_disabled || _czkem is null) return;
            try { _czkem.EnableDevice(MachineNumber, true); }
            finally { _disabled = false; }
        });
    }

    public void Disconnect()
    {
        _sta.Invoke(() =>
        {
            if (_czkem is null) return;
            try { if (_disabled) { _czkem.EnableDevice(MachineNumber, true); _disabled = false; } } catch { }
            try { _czkem.Disconnect(); } catch { }
            _czkem = null;
            _connected = false;
        });
    }

    public void Dispose()
    {
        try { Disconnect(); } catch { }
        _sta.Dispose();
    }

    // ---------- Users ----------

    public List<DeviceUser> ListUsers()
    {
        return _sta.Invoke(() =>
        {
            Require();
            var users = new List<DeviceUser>();

            if (!_czkem!.ReadAllUserID(MachineNumber))
                return users;

            string enrollNumber = "", name = "", password = "";
            int privilege = 0;
            bool enabled = true;

            while (_czkem.SSR_GetAllUserInfo(
                       MachineNumber, out enrollNumber, out name, out password,
                       out privilege, out enabled))
            {
                users.Add(new DeviceUser(enrollNumber ?? "", name ?? "", privilege, enabled));
            }

            return users;
        });
    }

    public DeviceUser? GetUser(string enrollNumber)
    {
        return _sta.Invoke(() =>
        {
            Require();
            string name = "", password = "";
            int privilege = 0;
            bool enabled = true;

            if (!_czkem!.SSR_GetUserInfo(MachineNumber, enrollNumber, out name, out password, out privilege, out enabled))
                return null;

            return new DeviceUser(enrollNumber, name ?? "", privilege, enabled);
        });
    }

    public void CreateUser(string enrollNumber, string name, int privilege = 0, string password = "", bool enabled = true)
    {
        _sta.Invoke(() =>
        {
            Require();
            bool ok = _czkem!.SSR_SetUserInfo(MachineNumber, enrollNumber, name, password, privilege, enabled);
            if (!ok) throw new IOException($"SSR_SetUserInfo failed for {enrollNumber}: {LastError()}");
        });
    }

    public void DeleteUser(string enrollNumber)
    {
        _sta.Invoke(() =>
        {
            Require();
            bool ok = _czkem!.SSR_DeleteEnrollData(MachineNumber, enrollNumber, 12);
            if (!ok) throw new IOException($"SSR_DeleteEnrollData failed for {enrollNumber}: {LastError()}");
        });
    }

    // ---------- Enrollment ----------

    public void StartEnrollFingerprint(string enrollNumber, int fingerIndex)
    {
        _sta.Invoke(() =>
        {
            Require();
            bool ok = _czkem!.StartEnrollEx(enrollNumber, fingerIndex, 1);
            if (!ok) throw new IOException($"StartEnrollEx failed for {enrollNumber} finger={fingerIndex}: {LastError()}");
        });
    }

    public void StartEnrollFace(string enrollNumber)
    {
        _sta.Invoke(() =>
        {
            Require();
            if (!int.TryParse(enrollNumber, out var uid))
                throw new ArgumentException($"enrollNumber must be numeric for face enrollment: {enrollNumber}");
            bool ok = _czkem!.StartEnroll(uid, 50);
            if (!ok) throw new IOException($"StartEnroll(face) failed for {enrollNumber}: {LastError()}");
        });
    }

    public void CancelEnroll()
    {
        _sta.Invoke(() =>
        {
            Require();
            _czkem!.CancelOperation();
        });
    }

    // ---------- Fingerprints ----------

    public byte[]? GetFingerTemplate(string enrollNumber, int fingerIndex)
    {
        return _sta.Invoke(() =>
        {
            Require();
            string tmpData = "";
            int tmpLength = 0;
            int flag = 0;

            bool ok = _czkem!.GetUserTmpExStr(
                MachineNumber, enrollNumber, fingerIndex,
                out flag, out tmpData, out tmpLength);

            if (!ok || string.IsNullOrEmpty(tmpData) || tmpLength == 0) return null;
            return System.Text.Encoding.UTF8.GetBytes(tmpData);
        });
    }

    public void SetFingerTemplate(string enrollNumber, int fingerIndex, byte[] template, int flag = 1)
    {
        _sta.Invoke(() =>
        {
            Require();
            string tmpData = System.Text.Encoding.UTF8.GetString(template);
            bool ok = _czkem!.SetUserTmpExStr(MachineNumber, enrollNumber, fingerIndex, flag, tmpData);
            if (!ok) throw new IOException($"SetUserTmpExStr failed for {enrollNumber} slot={fingerIndex}: {LastError()}");
        });
    }

    // ---------- Faces ----------

    public byte[]? GetFaceTemplate(string enrollNumber, int faceIndex = 50)
    {
        return _sta.Invoke(() =>
        {
            Require();
            string tmpData = "";
            int tmpLength = 0;

            bool ok = _czkem!.GetUserFaceStr(
                MachineNumber, enrollNumber, faceIndex, ref tmpData, ref tmpLength);

            if (!ok || string.IsNullOrEmpty(tmpData) || tmpLength == 0) return null;
            return System.Text.Encoding.UTF8.GetBytes(tmpData);
        });
    }

    public void SetFaceTemplate(string enrollNumber, byte[] template, int faceIndex = 50)
    {
        _sta.Invoke(() =>
        {
            Require();
            string tmpData = System.Text.Encoding.UTF8.GetString(template);
            bool ok = _czkem!.SetUserFaceStr(MachineNumber, enrollNumber, faceIndex, tmpData, tmpData.Length);
            if (!ok) throw new IOException($"SetUserFaceStr failed for {enrollNumber} slot={faceIndex}: {LastError()}");
        });
    }

    // ---------- Attendance Logs ----------

    public List<AttLog> ReadAllAttLogs()
    {
        return _sta.Invoke(() =>
        {
            Require();
            var logs = new List<AttLog>();
            if (!_czkem!.ReadGeneralLogData(MachineNumber)) return logs;

            string uid = "";
            int verify = 0, inOut = 0, y = 0, mo = 0, d = 0, h = 0, mi = 0, s = 0, wc = 0;

            while (_czkem.SSR_GetGeneralLogData(
                       MachineNumber, out uid, out verify, out inOut,
                       out y, out mo, out d, out h, out mi, out s, ref wc))
            {
                logs.Add(new AttLog(uid ?? "", SafeDate(y, mo, d, h, mi, s), verify, inOut, wc));
            }
            return logs;
        });
    }

    public List<AttLog> ReadNewAttLogs()
    {
        return _sta.Invoke(() =>
        {
            Require();
            var logs = new List<AttLog>();
            if (!_czkem!.ReadNewGLogData(MachineNumber)) return logs;

            string uid = "";
            int verify = 0, inOut = 0, y = 0, mo = 0, d = 0, h = 0, mi = 0, s = 0, wc = 0;

            while (_czkem.SSR_GetGeneralLogData(
                       MachineNumber, out uid, out verify, out inOut,
                       out y, out mo, out d, out h, out mi, out s, ref wc))
            {
                logs.Add(new AttLog(uid ?? "", SafeDate(y, mo, d, h, mi, s), verify, inOut, wc));
            }
            return logs;
        });
    }

    public List<AttLog> ReadAttLogsByDateRange(string startDate, string endDate)
    {
        return _sta.Invoke(() =>
        {
            Require();
            var logs = new List<AttLog>();
            if (!_czkem!.ReadTimeGLogData(MachineNumber, startDate, endDate)) return logs;

            string uid = "";
            int verify = 0, inOut = 0, y = 0, mo = 0, d = 0, h = 0, mi = 0, s = 0, wc = 0;

            while (_czkem.SSR_GetGeneralLogData(
                       MachineNumber, out uid, out verify, out inOut,
                       out y, out mo, out d, out h, out mi, out s, ref wc))
            {
                logs.Add(new AttLog(uid ?? "", SafeDate(y, mo, d, h, mi, s), verify, inOut, wc));
            }
            return logs;
        });
    }

    public List<AdminLog> ReadAdminLogs()
    {
        return _sta.Invoke(() =>
        {
            Require();
            var logs = new List<AdminLog>();
            if (!_czkem!.ReadSuperLogData(MachineNumber)) return logs;

            while (_czkem.SSR_GetSuperLogData(
                       MachineNumber, out int _, out string admin, out string target,
                       out int manipulation, out string timeStr, out int _, out int _, out int _))
            {
                var ts = DateTime.TryParse(timeStr, out var parsed) ? parsed.ToString("yyyy-MM-dd HH:mm:ss") : timeStr;
                logs.Add(new AdminLog(admin ?? "", target ?? "", manipulation, ts));
            }
            return logs;
        });
    }

    public void ClearAttLogs()
    {
        _sta.Invoke(() =>
        {
            Require();
            _czkem!.ClearGLog(MachineNumber);
        });
    }

    public void ClearAdminLogs()
    {
        _sta.Invoke(() =>
        {
            Require();
            _czkem!.ClearSLog(MachineNumber);
        });
    }

    public void DeleteAttLogsByDateRange(string startDate, string endDate)
    {
        _sta.Invoke(() =>
        {
            Require();
            bool ok = _czkem!.DeleteAttlogBetweenTheDate(MachineNumber, startDate, endDate);
            if (!ok) throw new IOException($"DeleteAttlogBetweenTheDate failed: {LastError()}");
        });
    }

    public void DeleteAttLogsBefore(string dateTime)
    {
        _sta.Invoke(() =>
        {
            Require();
            bool ok = _czkem!.DeleteAttlogByTime(MachineNumber, dateTime);
            if (!ok) throw new IOException($"DeleteAttlogByTime failed: {LastError()}");
        });
    }

    private static string SafeDate(int y, int mo, int d, int h, int mi, int s)
    {
        try { return new DateTime(y, mo, d, h, mi, s).ToString("yyyy-MM-dd HH:mm:ss"); }
        catch { return $"{y:D4}-{mo:D2}-{d:D2} {h:D2}:{mi:D2}:{s:D2}"; }
    }

    // ---------- Delete templates ----------

    public void DeleteFingerTemplate(string enrollNumber, int fingerIndex)
    {
        _sta.Invoke(() =>
        {
            Require();
            bool ok = _czkem!.SSR_DelUserTmpExt(MachineNumber, enrollNumber, fingerIndex);
            if (!ok) throw new IOException($"SSR_DelUserTmpExt failed for {enrollNumber} finger={fingerIndex}: {LastError()}");
        });
    }

    public void DeleteFaceTemplate(string enrollNumber, int faceIndex = 50)
    {
        _sta.Invoke(() =>
        {
            Require();
            bool ok = _czkem!.DelUserFace(MachineNumber, enrollNumber, faceIndex);
            if (!ok) throw new IOException($"DelUserFace failed for {enrollNumber} face={faceIndex}: {LastError()}");
        });
    }

    // ---------- User validity ----------

    public UserValidity GetUserValidDate(string enrollNumber)
    {
        return _sta.Invoke(() =>
        {
            Require();
            bool ok = _czkem!.GetUserValidDate(MachineNumber, enrollNumber, out int expires, out int validCount, out string startDate, out string endDate);
            if (!ok) return new UserValidity(enrollNumber, false, 0, "", "");
            return new UserValidity(enrollNumber, expires == 1, validCount, startDate ?? "", endDate ?? "");
        });
    }

    public void SetUserValidDate(string enrollNumber, bool expires, string startDate, string endDate)
    {
        _sta.Invoke(() =>
        {
            Require();
            bool ok = _czkem!.SetUserValidDate(MachineNumber, enrollNumber, expires ? 1 : 0, 0, startDate, endDate);
            if (!ok) throw new IOException($"SetUserValidDate failed for {enrollNumber}: {LastError()}");
        });
    }

    // ---------- Device ----------

    public void RestartDevice()
    {
        _sta.Invoke(() =>
        {
            Require();
            _czkem!.RestartDevice(MachineNumber);
        });
    }

    public void PlayVoice(int index)
    {
        _sta.Invoke(() =>
        {
            Require();
            _czkem!.PlayVoiceByIndex(index);
        });
    }

    public void DoorLock()
    {
        _sta.Invoke(() =>
        {
            Require();
            _czkem!.EnableDevice(MachineNumber, false);
            _disabled = true;
        });
    }

    public void DoorUnlock(int seconds = 5)
    {
        _sta.Invoke(() =>
        {
            Require();
            _czkem!.ACUnlock(MachineNumber, seconds);
        });
    }

    public DeviceTimeInfo GetDeviceTime()
    {
        return _sta.Invoke(() =>
        {
            Require();
            int y = 0, mo = 0, d = 0, h = 0, mi = 0, s = 0;
            bool ok = _czkem!.GetDeviceTime(MachineNumber, ref y, ref mo, ref d, ref h, ref mi, ref s);
            if (!ok) throw new IOException($"GetDeviceTime failed: {LastError()}");
            return new DeviceTimeInfo(y, mo, d, h, mi, s);
        });
    }

    public void SetDeviceTime(DateTime t)
    {
        _sta.Invoke(() =>
        {
            Require();
            bool ok = _czkem!.SetDeviceTime2(MachineNumber, t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second);
            if (!ok) throw new IOException($"SetDeviceTime failed: {LastError()}");
        });
    }

    public DeviceInfo GetDeviceInfo()
    {
        return _sta.Invoke(() =>
        {
            Require();
            string firmware = "", platform = "", vendor = "", sdkVer = "", mac = "";
            _czkem!.GetSerialNumber(MachineNumber, out string serial);
            _czkem.GetFirmwareVersion(MachineNumber, ref firmware);
            _czkem.GetPlatform(MachineNumber, ref platform);
            _czkem.GetVendor(ref vendor);
            _czkem.GetProductCode(MachineNumber, out string product);
            _czkem.GetSDKVersion(ref sdkVer);
            _czkem.GetDeviceMAC(MachineNumber, ref mac);

            int userCount = 0, fpCount = 0, faceCount = 0, attLogCount = 0;
            int userCap = 0, fpCap = 0, faceCap = 0, attCap = 0;
            _czkem.GetDeviceStatus(MachineNumber, 2, ref userCount);
            _czkem.GetDeviceStatus(MachineNumber, 3, ref fpCount);
            _czkem.GetDeviceStatus(MachineNumber, 6, ref attLogCount);
            _czkem.GetDeviceStatus(MachineNumber, 7, ref fpCap);
            _czkem.GetDeviceStatus(MachineNumber, 8, ref userCap);
            _czkem.GetDeviceStatus(MachineNumber, 9, ref attCap);
            try { _czkem.GetDeviceStatus(MachineNumber, 21, ref faceCount); } catch { }
            try { _czkem.GetDeviceStatus(MachineNumber, 22, ref faceCap); } catch { }

            return new DeviceInfo(
                serial ?? "", firmware ?? "", platform ?? "", vendor ?? "",
                product ?? "", sdkVer ?? "", mac ?? "",
                userCount, userCap, fpCount, fpCap,
                faceCount, faceCap, attLogCount, attCap);
        });
    }

    /// <summary>Fingerprint template slot range (0..9).</summary>
    public static IEnumerable<int> FingerSlots => Enumerable.Range(0, 10);

    /// <summary>Face template slots. Most firmware supports 50 only; 51..54 exist on some models.</summary>
    public static IEnumerable<int> FaceSlots => Enumerable.Range(50, 5);

    private int LastError()
    {
        try { int code = 0; _czkem!.GetLastError(ref code); return code; }
        catch { return 0; }
    }

    private void Require()
    {
        if (!_connected || _czkem is null)
            throw new InvalidOperationException("Device not connected. Call Connect() first.");
    }
}
