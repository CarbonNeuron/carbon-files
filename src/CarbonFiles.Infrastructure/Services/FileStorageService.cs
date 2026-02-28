using CarbonFiles.Core.Configuration;
using Microsoft.Extensions.Options;

namespace CarbonFiles.Infrastructure.Services;

public sealed class FileStorageService
{
    private readonly string _dataDir;

    public FileStorageService(IOptions<CarbonFilesOptions> options)
    {
        _dataDir = options.Value.DataDir;
    }

    public string GetFilePath(string bucketId, string filePath)
    {
        var encoded = Uri.EscapeDataString(filePath.ToLowerInvariant());
        return Path.Combine(_dataDir, bucketId, encoded);
    }

    public async Task<long> StoreAsync(string bucketId, string filePath, Stream content)
    {
        var targetPath = GetFilePath(bucketId, filePath);
        var dir = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(dir);

        // Atomic write: temp file + rename
        var tempPath = $"{targetPath}.tmp.{Guid.NewGuid():N}";
        long size;
        await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
        {
            await content.CopyToAsync(fs);
            size = fs.Length;
        }
        File.Move(tempPath, targetPath, overwrite: true);
        return size;
    }

    public FileStream? OpenRead(string bucketId, string filePath)
    {
        var path = GetFilePath(bucketId, filePath);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920) : null;
    }

    public long GetFileSize(string bucketId, string filePath)
    {
        var path = GetFilePath(bucketId, filePath);
        return File.Exists(path) ? new System.IO.FileInfo(path).Length : -1;
    }

    public bool FileExists(string bucketId, string filePath)
    {
        var path = GetFilePath(bucketId, filePath);
        return File.Exists(path);
    }

    public async Task<long> PatchFileAsync(string bucketId, string filePath, Stream content, long offset, bool append)
    {
        var path = GetFilePath(bucketId, filePath);
        if (!File.Exists(path))
            return -1;

        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 81920);

        if (append)
        {
            fs.Seek(0, SeekOrigin.End);
        }
        else
        {
            fs.Seek(offset, SeekOrigin.Begin);
        }

        await content.CopyToAsync(fs);
        return fs.Length;
    }

    public void DeleteFile(string bucketId, string filePath)
    {
        var path = GetFilePath(bucketId, filePath);
        if (File.Exists(path))
            File.Delete(path);
    }

    public void DeleteBucketDir(string bucketId)
    {
        var dir = Path.Combine(_dataDir, bucketId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
    }
}
