using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;

namespace CarbonFiles.Core.Interfaces;

public interface INotificationService
{
    Task NotifyFileCreated(string bucketId, BucketFile file);
    Task NotifyFileUpdated(string bucketId, BucketFile file);
    Task NotifyFileDeleted(string bucketId, string path);
    Task NotifyBucketCreated(Bucket bucket);
    Task NotifyBucketUpdated(string bucketId, BucketChanges changes);
    Task NotifyBucketDeleted(string bucketId);
}
