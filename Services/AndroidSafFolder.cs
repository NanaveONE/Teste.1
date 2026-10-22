#if ANDROID
using Android.Content;
using Android.Provider;
using XboxImageExtractorMobile.Core;
using Uri = Android.Net.Uri;

namespace XboxImageExtractorMobile.Services;

/// <summary>Creates directories/files inside a user-selected SAF tree.</summary>
public sealed class AndroidSafFolder : IExtractionFolder
{
    private readonly ContentResolver _resolver;
    private readonly Uri _treeUri;
    private readonly Uri _directoryUri;

    public AndroidSafFolder(ContentResolver resolver, Uri treeUri)
    {
        _resolver = resolver;
        _treeUri = treeUri;
        _directoryUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri,
            DocumentsContract.GetTreeDocumentId(treeUri))
            ?? throw new IOException("A pasta selecionada não está disponível.");
    }

    private AndroidSafFolder(ContentResolver resolver, Uri treeUri, Uri directoryUri)
    {
        _resolver = resolver;
        _treeUri = treeUri;
        _directoryUri = directoryUri;
    }

    public Task<IExtractionFolder> CreateSubfolderAsync(string name, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var newUri = DocumentsContract.CreateDocument(_resolver, _directoryUri,
            DocumentsContract.Document.MimeTypeDir, name)
            ?? throw new IOException($"Não foi possível criar a pasta: {name}");
        return Task.FromResult<IExtractionFolder>(new AndroidSafFolder(_resolver, _treeUri, newUri));
    }

    public Task<Stream> CreateFileAsync(string name, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fileUri = DocumentsContract.CreateDocument(_resolver, _directoryUri,
            "application/octet-stream", name)
            ?? throw new IOException($"Não foi possível criar o arquivo: {name}");
        Stream stream = _resolver.OpenOutputStream(fileUri, "w")
            ?? throw new IOException($"Não foi possível escrever no arquivo: {name}");
        return Task.FromResult(stream);
    }
}
#endif
