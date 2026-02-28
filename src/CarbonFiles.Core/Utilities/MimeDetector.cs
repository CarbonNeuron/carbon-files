using System.Collections.Frozen;

namespace CarbonFiles.Core.Utilities;

public static class MimeDetector
{
    private static readonly FrozenDictionary<string, string> MimeTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif", [".webp"] = "image/webp", [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon", [".bmp"] = "image/bmp", [".tiff"] = "image/tiff",
        [".avif"] = "image/avif",
        // Video
        [".mp4"] = "video/mp4", [".webm"] = "video/webm", [".avi"] = "video/x-msvideo",
        [".mov"] = "video/quicktime", [".mkv"] = "video/x-matroska", [".wmv"] = "video/x-ms-wmv",
        // Audio
        [".mp3"] = "audio/mpeg", [".wav"] = "audio/wav", [".ogg"] = "audio/ogg",
        [".flac"] = "audio/flac", [".aac"] = "audio/aac", [".m4a"] = "audio/mp4",
        // Documents
        [".pdf"] = "application/pdf", [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        // Archives
        [".zip"] = "application/zip", [".tar"] = "application/x-tar",
        [".gz"] = "application/gzip", [".7z"] = "application/x-7z-compressed",
        [".rar"] = "application/vnd.rar",
        // Code/text
        [".json"] = "application/json", [".xml"] = "application/xml",
        [".js"] = "text/javascript", [".mjs"] = "text/javascript",
        [".ts"] = "text/typescript", [".tsx"] = "text/typescript",
        [".jsx"] = "text/javascript",
        [".html"] = "text/html", [".htm"] = "text/html",
        [".css"] = "text/css", [".csv"] = "text/csv",
        [".md"] = "text/markdown", [".txt"] = "text/plain",
        [".yaml"] = "text/yaml", [".yml"] = "text/yaml",
        [".toml"] = "text/toml",
        [".rs"] = "text/x-rust", [".cs"] = "text/x-csharp",
        [".py"] = "text/x-python", [".rb"] = "text/x-ruby",
        [".go"] = "text/x-go", [".java"] = "text/x-java",
        [".c"] = "text/x-c", [".cpp"] = "text/x-c++",
        [".h"] = "text/x-c", [".hpp"] = "text/x-c++",
        [".swift"] = "text/x-swift", [".kt"] = "text/x-kotlin",
        [".sh"] = "text/x-shellscript", [".bash"] = "text/x-shellscript",
        [".sql"] = "text/x-sql",
        [".dockerfile"] = "text/x-dockerfile",
        // Fonts
        [".woff"] = "font/woff", [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf", [".otf"] = "font/otf",
        // Data
        [".wasm"] = "application/wasm",
        [".bin"] = "application/octet-stream",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static string DetectFromExtension(string filename)
    {
        var ext = Path.GetExtension(filename);
        if (string.IsNullOrEmpty(ext))
            return "application/octet-stream";
        return MimeTypes.GetValueOrDefault(ext, "application/octet-stream");
    }
}
