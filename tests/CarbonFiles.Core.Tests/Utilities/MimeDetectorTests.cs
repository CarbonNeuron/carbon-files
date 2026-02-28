using CarbonFiles.Core.Utilities;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Core.Tests.Utilities;

public class MimeDetectorTests
{
    [Theory]
    [InlineData("image.png", "image/png")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("anim.gif", "image/gif")]
    [InlineData("icon.svg", "image/svg+xml")]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("doc.pdf", "application/pdf")]
    [InlineData("data.json", "application/json")]
    [InlineData("app.js", "text/javascript")]
    [InlineData("index.ts", "text/typescript")]
    [InlineData("page.html", "text/html")]
    [InlineData("style.css", "text/css")]
    [InlineData("readme.md", "text/markdown")]
    [InlineData("src/main.rs", "text/x-rust")]
    [InlineData("Program.cs", "text/x-csharp")]
    [InlineData("notes.txt", "text/plain")]
    [InlineData("font.woff2", "font/woff2")]
    [InlineData("app.wasm", "application/wasm")]
    public void DetectFromExtension_ReturnsCorrectMimeType(string filename, string expectedMime)
    {
        MimeDetector.DetectFromExtension(filename).Should().Be(expectedMime);
    }

    [Theory]
    [InlineData("file.xyz")]
    [InlineData("file.unknown")]
    public void DetectFromExtension_ReturnsOctetStreamForUnknownExtension(string filename)
    {
        MimeDetector.DetectFromExtension(filename).Should().Be("application/octet-stream");
    }

    [Theory]
    [InlineData("Makefile")]
    [InlineData("LICENSE")]
    [InlineData("Dockerfile")]
    public void DetectFromExtension_ReturnsOctetStreamForNoExtension(string filename)
    {
        MimeDetector.DetectFromExtension(filename).Should().Be("application/octet-stream");
    }

    [Theory]
    [InlineData("src/components/App.tsx", "text/typescript")]
    [InlineData("assets/images/logo.webp", "image/webp")]
    [InlineData("dist/bundle.js", "text/javascript")]
    public void DetectFromExtension_HandlesPathsWithDirectories(string filename, string expectedMime)
    {
        MimeDetector.DetectFromExtension(filename).Should().Be(expectedMime);
    }

    [Theory]
    [InlineData("IMAGE.PNG", "image/png")]
    [InlineData("Photo.JPG", "image/jpeg")]
    [InlineData("Data.JSON", "application/json")]
    public void DetectFromExtension_IsCaseInsensitive(string filename, string expectedMime)
    {
        MimeDetector.DetectFromExtension(filename).Should().Be(expectedMime);
    }
}
