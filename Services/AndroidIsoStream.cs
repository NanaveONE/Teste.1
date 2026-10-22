#if ANDROID
using Android.Content;
using Android.OS;
using Android.Systems;
using Uri = Android.Net.Uri;

namespace XboxImageExtractorMobile.Services;

/// <summary>
/// Reads large ISO documents with positional reads, without copying gigabytes
/// into application cache. The document provider must expose a seekable FD.
/// </summary>
public sealed class AndroidIsoStream : Stream
{
    private readonly ParcelFileDescriptor _descriptor;
    private readonly long _length;
    private long _position;
    private bool _disposed;

    public AndroidIsoStream(ContentResolver resolver, Uri uri)
    {
        _descriptor = resolver.OpenFileDescriptor(uri, "r")
            ?? throw new IOException("Não foi possível abrir o arquivo selecionado.");
        _length = _descriptor.StatSize;
        if (_length < 0)
        {
            _descriptor.Dispose();
            throw new NotSupportedException("Este provedor não informa o tamanho da ISO. Selecione um arquivo armazenado localmente.");
        }
        try
        {
            if (_length > 0)
                Os.Pread(_descriptor.FileDescriptor, Array.Empty<byte>(), 0, 0, 0);
        }
        catch (Exception ex)
        {
            _descriptor.Dispose();
            throw new NotSupportedException("O provedor não permite acesso aleatório à ISO. Use uma ISO armazenada localmente.", ex);
        }
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }
    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException();
        if (count == 0 || _position >= _length) return 0;
        int want = (int)Math.Min(count, _length - _position);
        int n;
        try { n = Os.Pread(_descriptor.FileDescriptor, buffer, offset, want, _position); }
        catch (Exception ex) { throw new IOException("Falha ao ler a ISO através do Android.", ex); }
        if (n > 0) _position += n;
        return n;
    }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Bounded 256 KiB extraction chunks; synchronous positional read happens on worker thread.
        var temp = new byte[Math.Min(buffer.Length, 256 * 1024)];
        int n = Read(temp, 0, temp.Length);
        temp.AsMemory(0, n).CopyTo(buffer);
        return ValueTask.FromResult(n);
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long next = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(_position + offset),
            SeekOrigin.End => checked(_length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (next < 0) throw new IOException("Posição negativa na ISO.");
        _position = next;
        return next;
    }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing) _descriptor.Dispose();
        _disposed = true;
        base.Dispose(disposing);
    }
}
#endif
