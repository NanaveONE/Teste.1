using XboxImageExtractorMobile.Core;
using XboxImageExtractorMobile.Services;
using Microsoft.Maui.ApplicationModel;

namespace XboxImageExtractorMobile;

public sealed class MainPage : ContentPage
{
    private readonly Label _status = new() { Text = "Selecione uma ISO de Xbox.", FontSize = 14 };
    private readonly Label _summary = new() { Text = "Nenhuma ISO aberta." };
    private readonly Label _currentFile = new() { Text = "", FontSize = 12, LineBreakMode = LineBreakMode.TailTruncation };
    private readonly ProgressBar _progress = new() { Progress = 0 };
    private readonly Button _open = new() { Text = "Abrir ISO" };
    private readonly Button _extract = new() { Text = "Escolher pasta e extrair", IsEnabled = false };
    private readonly Button _cancel = new() { Text = "Cancelar", IsEnabled = false };
    private readonly VerticalStackLayout _fileList = new() { Spacing = 2 };

    private SelectedDocument? _selected;
    private GdfxArchive? _archive;
    private CancellationTokenSource? _working;

    public MainPage()
    {
        Title = "Xbox ISO Extractor";
        _open.Clicked += async (_, _) => await OpenIsoAsync();
        _extract.Clicked += async (_, _) => await ExtractAsync();
        _cancel.Clicked += (_, _) => _working?.Cancel();

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20, 26),
                Spacing = 14,
                Children =
                {
                    new Label { Text = "Xbox ISO Extractor", FontSize = 26, FontAttributes = FontAttributes.Bold },
                    new Label { Text = "Xbox clássico (XISO) e Xbox 360 (GDFX)", FontSize = 13 },
                    _open, _summary, _status, _extract, _cancel, _progress, _currentFile,
                    new Label { Text = "Conteúdo da imagem (prévia)", FontSize = 18, FontAttributes = FontAttributes.Bold },
                    _fileList
                }
            }
        };
    }

    private async Task OpenIsoAsync()
    {
        SetBusy(true);
        try
        {
            var chosen = await AndroidDocumentPicker.PickIsoAsync();
            if (chosen is null) { _status.Text = "Seleção cancelada."; return; }
            _archive?.Dispose();
            _archive = null;
            _selected = null;
            _fileList.Children.Clear();
            _summary.Text = $"ISO: {chosen.Name}";
            _status.Text = "Lendo diretórios GDFX...";
            var resolver = Platform.CurrentActivity!.ContentResolver!;
            var stream = new AndroidIsoStream(resolver, chosen.Uri);
            var candidate = new GdfxArchive(stream);
            try { await candidate.LoadAsync(_working!.Token); }
            catch { candidate.Dispose(); throw; }
            _archive = candidate;
            _selected = chosen;
            _summary.Text = $"{chosen.Name} — {_archive.FileCount} arquivos, {_archive.DirectoryCount} pastas";
            _status.Text = "ISO analisada. Escolha uma pasta para extrair.";
            int shown = 0;
            PopulatePreview(_archive.RootDirectory!, "", ref shown);
            if (shown >= 150) _fileList.Children.Add(new Label { Text = "…prévia limitada a 150 itens" });
        }
        catch (OperationCanceledException) { _status.Text = "Leitura cancelada."; }
        catch (Exception ex) { _status.Text = $"Erro ao abrir ISO: {ex.Message}"; }
        finally { SetBusy(false); }
    }

    private void PopulatePreview(GdfxEntry entry, string prefix, ref int count)
    {
        foreach (var child in entry.Children)
        {
            if (count >= 150) return;
            _fileList.Children.Add(new Label
            {
                Text = $"{(child.IsDirectory ? "📁" : "📄")} {prefix}{child.Name}",
                FontSize = 12, LineBreakMode = LineBreakMode.TailTruncation
            });
            count++;
            if (child.IsDirectory) PopulatePreview(child, prefix + child.Name + "/", ref count);
        }
    }

    private async Task ExtractAsync()
    {
        if (_selected is null || _archive?.RootDirectory is null) return;
        SetBusy(true);
        try
        {
            var treeUri = await AndroidDocumentPicker.PickFolderAsync();
            if (treeUri is null) { _status.Text = "Seleção da pasta cancelada."; return; }
            _progress.Progress = 0;
            _status.Text = "Extraindo...";
            var root = _archive.RootDirectory;
            var resolver = Platform.CurrentActivity!.ContentResolver!;
            var sourceUri = _selected.Uri;
            var output = new AndroidSafFolder(resolver, treeUri);
            var progress = new Progress<ExtractionProgress>(p =>
            {
                _progress.Progress = Math.Clamp(p.Fraction, 0, 1);
                _currentFile.Text = $"{p.ExtractedFiles}/{p.TotalFiles} — {p.FileName} ({p.ExtractedBytes / (1024.0 * 1024):F1} MiB)";
            });
            CancellationToken ct = _working!.Token;
            await Task.Run(async () =>
            {
                using var iso = new AndroidIsoStream(resolver, sourceUri);
                await new GdfxExtractor().ExtractAsync(iso, root, output, progress, ct);
            }, ct);
            _status.Text = "Extração concluída na pasta escolhida.";
        }
        catch (OperationCanceledException) { _status.Text = "Extração cancelada. Arquivos já criados podem permanecer na pasta."; }
        catch (Exception ex) { _status.Text = $"Falha na extração: {ex.Message}"; }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        if (busy) _working = new CancellationTokenSource();
        else { _working?.Dispose(); _working = null; }
        _open.IsEnabled = !busy;
        _extract.IsEnabled = !busy && _archive?.RootDirectory is not null;
        _cancel.IsEnabled = busy;
    }
}
