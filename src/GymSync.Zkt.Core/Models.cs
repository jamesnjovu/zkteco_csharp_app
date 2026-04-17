namespace GymSync.Zkt.Core;

public sealed record DeviceUser(
    string EnrollNumber,
    string Name,
    int Privilege,
    bool Enabled
);

public sealed record TemplateBlob(
    int Slot,
    string File,
    int Bytes,
    string Sha256
);

public sealed record DeviceTimeInfo(
    int Year, int Month, int Day,
    int Hour, int Minute, int Second
);

public sealed record DeviceInfo(
    string Serial, string Firmware, string Platform, string Vendor,
    string Product, string SdkVersion, string Mac,
    int UserCount, int UserCapacity,
    int FingerprintCount, int FingerprintCapacity,
    int FaceCount, int FaceCapacity,
    int AttLogCount, int AttLogCapacity
);

public sealed record AttLog(
    string UserId,
    string Timestamp,
    int VerifyMethod,
    int InOutState,
    int WorkCode
);

public sealed record UserValidity(
    string EnrollNumber,
    bool Expires,
    int ValidCount,
    string StartDate,
    string EndDate
);

public sealed record AdminLog(
    string Admin,
    string Target,
    int Manipulation,
    string Timestamp
);

public sealed class Manifest
{
    public string DeviceIp { get; set; } = "";
    public int DevicePort { get; set; }
    public string EnrollNumber { get; set; } = "";
    public string Name { get; set; } = "";
    public string DownloadedAt { get; set; } = "";
    public List<TemplateBlob> Fingerprints { get; set; } = new();
    public List<TemplateBlob> Faces { get; set; } = new();
}
