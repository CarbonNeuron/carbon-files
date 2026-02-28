using CarbonFiles.Core.Models;

namespace CarbonFiles.Core.Interfaces;

public interface IUploadService
{
    Task<BucketFile> StoreFileAsync(string bucketId, string path, Stream content, AuthContext auth);
    Task<long> GetStoredFileSizeAsync(string bucketId, string path);
}
