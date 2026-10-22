namespace XboxImageExtractorMobile.Core;

// Adapted from dromex1/XboxImageExtractor/Core/GdfxExtractor.cs (MIT).
public sealed record ExtractionProgress(string FileName, int ExtractedFiles, int TotalFiles,
    long ExtractedBytes, long TotalBytes)
{
    public double Fraction => TotalBytes > 0 ? (double)ExtractedBytes / TotalBytes
        : TotalFiles > 0 ? (double)ExtractedFiles / TotalFiles : 1;
}

public interface IExtractionFolder
{
    Task<IExtractionFolder> CreateSubfolderAsync(string name, CancellationToken ct);
    Task<Stream> CreateFileAsync(string name, CancellationToken ct);
}

public sealed class GdfxExtractor
{
    // 256 KiB buffer, rather than the desktop's 4 MiB. Suitable for Android RAM.
    private const int BufferSize = 256 * 1024;

    public async Task ExtractAsync(Stream isoStream, GdfxEntry root, IExtractionFolder destination,
        IProgress<ExtractionProgress>? progress, CancellationToken ct)
    {
        if (!isoStream.CanSeek || !isoStream.CanRead)
            throw new NotSupportedException("Leitura aleatória da ISO não está disponível.");
        var counters = new Counters();
        CountFiles(root, counters);
        byte[] buffer = new byte[BufferSize];
        await ExtractEntryAsync(isoStream, root, destination, buffer, counters, progress, ct);
        progress?.Report(new ExtractionProgress("Concluído", counters.Done, counters.TotalFiles,
            counters.BytesDone, counters.TotalBytes));
    }

    private static void CountFiles(GdfxEntry entry, Counters c)
    {
        if (entry.IsDirectory)
        {
            foreach (var child in entry.Children) CountFiles(child, c);
        }
        else
        {
            c.TotalFiles++;
            c.TotalBytes += entry.Size;
        }
    }

    private static async Task ExtractEntryAsync(Stream iso, GdfxEntry entry, IExtractionFolder folder,
        byte[] buffer, Counters c, IProgress<ExtractionProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (entry.IsDirectory)
        {
            if (entry.Name != "/") folder = await folder.CreateSubfolderAsync(SafeName(entry.Name), ct);
            foreach (var child in entry.Children)
                await ExtractEntryAsync(iso, child, folder, buffer, c, progress, ct);
            return;
        }

        string name = SafeName(entry.Name);
        iso.Position = entry.AbsoluteOffset;
        using Stream output = await folder.CreateFileAsync(name, ct);
        long remaining = entry.Size;
        long lastProgressAt = c.BytesDone;
        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();
            int read = await iso.ReadAsync(buffer.AsMemory(0, (int)Math.Min(remaining, buffer.Length)), ct);
            if (read == 0) throw new EndOfStreamException($"ISO truncada ao extrair: {name}");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            remaining -= read;
            c.BytesDone += read;
            if (c.BytesDone - lastProgressAt >= 1024 * 1024)
            {
                Report(name, c, progress);
                lastProgressAt = c.BytesDone;
            }
        }
        await output.FlushAsync(ct);
        c.Done++;
        Report(name, c, progress);
    }

    // Keep ISO-provided path components inside the selected destination folder.
    private static string SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            name.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0 || name.Any(char.IsControl))
            throw new InvalidDataException($"Nome de arquivo inseguro encontrado na ISO: {name}");
        return name;
    }

    private static void Report(string name, Counters c, IProgress<ExtractionProgress>? progress) =>
        progress?.Report(new ExtractionProgress(name, c.Done, c.TotalFiles, c.BytesDone, c.TotalBytes));

    private sealed class Counters
    {
        public int TotalFiles;
        public int Done;
        public long TotalBytes;
        public long BytesDone;
    }
}
