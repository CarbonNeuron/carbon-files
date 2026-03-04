using System.Net;
using CarbonFiles.Client.Models;

namespace CarbonFiles.Client.Internal;

internal class ProgressStreamContent : HttpContent
{
    private readonly Stream _source;
    private readonly IProgress<UploadProgress>? _progress;
    private readonly int _bufferSize;

    public ProgressStreamContent(Stream source, IProgress<UploadProgress>? progress, int bufferSize = 81920)
    {
        _source = source;
        _progress = progress;
        _bufferSize = bufferSize;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var totalBytes = _source.CanSeek ? _source.Length : (long?)null;
        var buffer = new byte[_bufferSize];
        long bytesSent = 0;
        int bytesRead;

        while ((bytesRead = await _source.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await stream.WriteAsync(buffer, 0, bytesRead);
            bytesSent += bytesRead;
            _progress?.Report(new UploadProgress(bytesSent, totalBytes));
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        if (_source.CanSeek)
        {
            length = _source.Length;
            return true;
        }
        length = 0;
        return false;
    }
}
