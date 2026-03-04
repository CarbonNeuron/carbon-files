namespace CarbonFiles.Client.Models;

public class UploadProgress
{
    public long BytesSent { get; }
    public long? TotalBytes { get; }
    public int? Percentage { get; }

    public UploadProgress(long bytesSent, long? totalBytes)
    {
        BytesSent = bytesSent;
        TotalBytes = totalBytes;
        Percentage = totalBytes > 0 ? (int)(bytesSent * 100 / totalBytes.Value) : null;
    }
}
