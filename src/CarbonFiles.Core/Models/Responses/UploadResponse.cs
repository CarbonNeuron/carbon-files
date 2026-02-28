using CarbonFiles.Core.Models;

namespace CarbonFiles.Core.Models.Responses;

public sealed class UploadResponse
{
    public required IReadOnlyList<BucketFile> Uploaded { get; init; }
}
