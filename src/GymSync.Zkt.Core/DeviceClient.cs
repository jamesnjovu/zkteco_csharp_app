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
            // Always start with a fresh CZKEM instance.
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

    // ---------- Fingerprints ----------

    /// <summary>Get a fingerprint template (slots 0..9). Returns null if not enrolled.</summary>
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

    /// <summary>Push a fingerprint template previously obtained from <see cref="GetFingerTemplate"/>.</summary>
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

    /// <summary>Get a face template (slot 50). Returns null if not enrolled.</summary>
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

    /// <summary>Push a face template previously obtained from <see cref="GetFaceTemplate"/>.</summary>
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
