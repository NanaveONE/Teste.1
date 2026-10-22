using System.Text;

namespace XboxImageExtractorMobile.Core;

/// <summary>
/// GDFX/XISO reader based on dromex1/XboxImageExtractor/Core/GdfxArchive.cs (MIT).
/// Uses a seekable Stream (Android SAF file descriptor) instead of a Windows file path.
/// </summary>
public sealed class GdfxArchive : IDisposable
{
    private const int SectorSize = 2048;
    private const int MaxDirectoryBytes = 10 * 1024 * 1024;
    private const int MaxEntries = 250_000;
    private const int MaxDepth = 48;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");
    private static readonly long[] CommonOffsets =
    {
        0x10000, 0xFDA0000, 0x1FB30000, 0x2090000,
        0x40B0000, 0x2080000, 0x2020000, 0
    };

    private readonly Stream _image;
    private readonly BinaryReader _reader;
    private readonly HashSet<long> _visitedDirectories = new();
    private int _parsedEntries;

    public GdfxEntry? RootDirectory { get; private set; }
    public long MainSectorOffset { get; private set; }
    public long PartitionBaseOffset { get; private set; }
    public long TotalImageSize => _image.Length;
    public int FileCount { get; private set; }
    public int DirectoryCount { get; private set; }

    public GdfxArchive(Stream seekableImage)
    {
        if (!seekableImage.CanRead || !seekableImage.CanSeek)
            throw new NotSupportedException("A origem da ISO precisa permitir leitura aleatória (seek). Copie a ISO para o armazenamento local e tente novamente.");
        _image = seekableImage;
        _reader = new BinaryReader(seekableImage, Encoding.ASCII, leaveOpen: true);
    }

    public Task LoadAsync(CancellationToken ct = default) => Task.Run(() => Load(ct), ct);

    private void Load(CancellationToken ct)
    {
        MainSectorOffset = FindMagicOffset(ct);
        if (MainSectorOffset < 0)
            throw new InvalidDataException("Cabeçalho GDFX/XISO não encontrado. Confira se o arquivo é uma ISO de Xbox compatível.");
        PartitionBaseOffset = MainSectorOffset - 0x10000;
        if (PartitionBaseOffset < 0)
            throw new InvalidDataException("Offset inválido de partição GDFX.");
        SeekChecked(MainSectorOffset + Magic.Length, sizeof(uint) * 2);
        uint rootSector = _reader.ReadUInt32();
        uint rootSize = _reader.ReadUInt32();
        if (rootSize == 0 || rootSize > MaxDirectoryBytes)
            throw new InvalidDataException("Tamanho inválido do diretório principal GDFX.");

        var root = new GdfxEntry
        {
            Name = "/", Attributes = 0x10, StartSector = rootSector,
            Size = rootSize, AbsoluteOffset = ToAbsoluteOffset(rootSector)
        };
        FileCount = DirectoryCount = _parsedEntries = 0;
        _visitedDirectories.Clear();
        ReadDirectory(root, 0, ct);
        RootDirectory = root;
    }

    private long FindMagicOffset(CancellationToken ct)
    {
        foreach (long offset in CommonOffsets)
            if (IsMagic(offset)) return offset;

        // Scan first 1 GiB at sector boundaries; constant RAM usage.
        const int bufferSize = 1024 * 1024;
        byte[] buffer = new byte[bufferSize];
        long stop = Math.Min(_image.Length, 1024L * 1024 * 1024);
        for (long pos = 0; pos < stop; pos += bufferSize)
        {
            ct.ThrowIfCancellationRequested();
            _image.Position = pos;
            int read = ReadUpTo(buffer, (int)Math.Min(bufferSize, stop - pos));
            for (int i = 0; i <= read - Magic.Length; i += SectorSize)
            {
                if (buffer[i] != Magic[0]) continue;
                if (buffer.AsSpan(i, Magic.Length).SequenceEqual(Magic)) return pos + i;
            }
        }
        return -1;
    }

    private bool IsMagic(long position)
    {
        if (position < 0 || position > _image.Length - Magic.Length) return false;
        _image.Position = position;
        return _reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic);
    }

    private int ReadUpTo(byte[] buffer, int requested)
    {
        int total = 0;
        while (total < requested)
        {
            int n = _image.Read(buffer, total, requested - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private long ToAbsoluteOffset(uint sector)
    {
        long offset = checked(PartitionBaseOffset + (long)sector * SectorSize);
        if (offset < 0 || offset > _image.Length)
            throw new InvalidDataException("Offset de setor fora da ISO.");
        return offset;
    }

    private void SeekChecked(long offset, long bytes)
    {
        if (offset < 0 || bytes < 0 || offset > _image.Length || bytes > _image.Length - offset)
            throw new InvalidDataException("ISO truncada ou estrutura GDFX corrompida.");
        _image.Position = offset;
    }

    private void ReadDirectory(GdfxEntry directory, int depth, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > MaxDepth) throw new InvalidDataException("ISO contém profundidade de diretórios excessiva.");
        if (directory.Size > MaxDirectoryBytes) throw new InvalidDataException("Diretório GDFX muito grande.");
        if (!_visitedDirectories.Add(directory.AbsoluteOffset))
            throw new InvalidDataException("Ciclo de diretórios detectado na ISO.");

        var entries = ParseDirectoryTree(directory.AbsoluteOffset, directory.Size, ct);
        directory.Children = entries;
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.IsDirectory)
            {
                DirectoryCount++;
                if (entry.Size > 0) ReadDirectory(entry, depth + 1, ct);
            }
            else
            {
                if (entry.AbsoluteOffset > _image.Length || entry.Size > _image.Length - entry.AbsoluteOffset)
                    throw new InvalidDataException($"Arquivo fora dos limites da ISO: {entry.Name}");
                FileCount++;
            }
        }
    }

    private List<GdfxEntry> ParseDirectoryTree(long baseOffset, uint directorySize, CancellationToken ct)
    {
        if (directorySize < 14) return new();
        SeekChecked(baseOffset, directorySize);
        var entries = new List<GdfxEntry>();
        var queue = new Queue<long>();
        var visited = new HashSet<long>();
        queue.Enqueue(0);
        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            long relative = queue.Dequeue();
            if (!visited.Add(relative)) continue;
            if (relative < 0 || relative > directorySize - 14) throw new InvalidDataException("Índice de diretório GDFX inválido.");
            if (++_parsedEntries > MaxEntries) throw new InvalidDataException("ISO possui entradas demais.");
            SeekChecked(baseOffset + relative, 14);
            ushort left = _reader.ReadUInt16();
            ushort right = _reader.ReadUInt16();
            uint sector = _reader.ReadUInt32();
            uint size = _reader.ReadUInt32();
            byte attrs = _reader.ReadByte();
            byte nameLength = _reader.ReadByte();
            if (nameLength == 0 || nameLength == 255) continue;
            if (14L + nameLength > directorySize - relative) throw new InvalidDataException("Nome GDFX fora dos limites do diretório.");
            string name = Encoding.ASCII.GetString(_reader.ReadBytes(nameLength)).TrimEnd('\0');
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Nome de arquivo vazio na ISO.");
            entries.Add(new GdfxEntry
            {
                LeftIndex = left, RightIndex = right, StartSector = sector,
                Size = size, Attributes = attrs, Name = name,
                AbsoluteOffset = ToAbsoluteOffset(sector)
            });
            if (left > 0) queue.Enqueue(left * 4L);
            if (right > 0) queue.Enqueue(right * 4L);
        }
        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    public void Dispose()
    {
        _reader.Dispose();
        _image.Dispose();
    }
}
