using System.IO.Compression;
using Client.Tools.Logging;
using Serilog.Events;
using Xunit;

namespace Client.Application.Tests;

public class LogFilesTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("flowrate-logs-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void Write(string name, string text, int minutesAgo)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));
    }

    [Fact]
    public void ClearingKeepsTheNewestFileOfEachKind()
    {
        Write("app-20261029.txt", "old", 60);
        Write("app-20261030.txt", "new", 1);
        Write("gstreamer-20261029.txt", "old", 60);
        Write("gstreamer-20261029_001.txt", "new", 2);
        Write("libwebrtc-20261030.txt", "only", 3);
        Write("notes.txt", "not of the journal", 90);

        Assert.Equal(2, LogFiles.Clear(_directory));

        Assert.Equal(["app-20261030.txt", "gstreamer-20261029_001.txt", "libwebrtc-20261030.txt"],
            LogFiles.List(_directory).Select(file => file.Name).Order());
        Assert.True(File.Exists(Path.Combine(_directory, "notes.txt")));
        Assert.Equal(3 + 3 + 4, LogFiles.Size(_directory));
    }

    [Fact]
    public async Task TheArchiveHasTheFilesTheSystemAndTheRecentEntries()
    {
        Write("app-20261030.txt", "the journal", 1);
        var recent = new[]
        {
            new LogEntry(1, DateTimeOffset.Now, LogEventLevel.Warning, "Client.Api.Gateway", "Gateway",
                "Connection lost", "System.IO.IOException: reset")
        };

        using var archive = new MemoryStream();

        // The file being written is open, as the journal holds it
        await using (new FileStream(Path.Combine(_directory, "app-20261030.txt"), FileMode.Open, FileAccess.Write,
                         FileShare.Read | FileShare.Delete))
        {
            await LogFiles.WriteArchiveAsync(archive, _directory, "FlowRate: 0.7.1", recent);
        }

        archive.Position = 0;
        using var zip = new ZipArchive(archive);

        Assert.Equal(["logs/app-20261030.txt", "recent.txt", "system.txt"],
            zip.Entries.Select(entry => entry.FullName).Order());
        Assert.Equal("the journal", Read(zip, "logs/app-20261030.txt"));
        Assert.Equal("FlowRate: 0.7.1", Read(zip, "system.txt"));
        Assert.Contains("Warning] [Client.Api.Gateway] Connection lost", Read(zip, "recent.txt"));
        Assert.Contains("IOException: reset", Read(zip, "recent.txt"));
    }

    [Fact]
    public void NoFolderIsNoFiles()
    {
        Assert.Empty(LogFiles.List(Path.Combine(_directory, "missing")));
        Assert.Equal(0, LogFiles.Clear(Path.Combine(_directory, "missing")));
    }

    private static string Read(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }
}
