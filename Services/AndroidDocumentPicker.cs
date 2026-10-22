#if ANDROID
using Android.App;
using Android.Content;
using Android.Database;
using Android.Provider;
using Microsoft.Maui.ApplicationModel;
using Uri = Android.Net.Uri;

namespace XboxImageExtractorMobile.Services;

public sealed record SelectedDocument(Uri Uri, string Name);

/// <summary>Android native SAF file/tree picker routed through MainActivity.</summary>
public static class AndroidDocumentPicker
{
    private const int IsoRequest = 7021;
    private const int FolderRequest = 7022;
    private static TaskCompletionSource<Uri?>? _pending;
    private static int _request;

    public static async Task<SelectedDocument?> PickIsoAsync()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*"); // Some file providers report .iso as application/octet-stream.
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        Uri? result = await LaunchAsync(intent, IsoRequest);
        if (result is null) return null;
        var resolver = Platform.CurrentActivity!.ContentResolver!;
        string name = GetName(resolver, result);
        if (!name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Selecione um arquivo .iso.");
        return new SelectedDocument(result, name);
    }

    public static Task<Uri?> PickFolderAsync()
    {
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission |
            ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);
        return LaunchAsync(intent, FolderRequest);
    }

    private static Task<Uri?> LaunchAsync(Intent intent, int request)
    {
        if (_pending is not null) throw new InvalidOperationException("Já existe uma seleção em andamento.");
        Activity activity = Platform.CurrentActivity ?? throw new InvalidOperationException("Activity do Android não encontrada.");
        var tcs = new TaskCompletionSource<Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = tcs;
        _request = request;
        try { activity.StartActivityForResult(intent, request); }
        catch { _pending = null; throw; }
        return tcs.Task;
    }

    public static void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (_pending is null || requestCode != _request) return;
        var tcs = _pending;
        _pending = null;
        Uri? selected = resultCode == Result.Ok ? data?.Data : null;
        if (selected is not null)
        {
            try
            {
                var resolver = Platform.CurrentActivity!.ContentResolver!;
                var flags = requestCode == IsoRequest
                    ? ActivityFlags.GrantReadUriPermission
                    : ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission;
                resolver.TakePersistableUriPermission(selected, flags);
            }
            catch (Exception) { /* Session grant may still work; permission errors surface on use. */ }
        }
        tcs.TrySetResult(selected);
    }

    private static string GetName(ContentResolver resolver, Uri uri)
    {
        using ICursor? cursor = resolver.Query(uri, new[] { OpenableColumns.DisplayName }, null, null, null);
        if (cursor?.MoveToFirst() == true)
        {
            int index = cursor.GetColumnIndex(OpenableColumns.DisplayName);
            if (index >= 0) return cursor.GetString(index) ?? "arquivo.iso";
        }
        return uri.LastPathSegment ?? "arquivo.iso";
    }
}
#endif
