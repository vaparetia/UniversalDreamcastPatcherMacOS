using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace UniversalDreamcastPatcher;

public partial class MainWindow : Window
{
    private string? _gdiFile;
    private string? _patchFile;

    private readonly Bitmap _logoOff;
    private readonly Bitmap _logoOn;

    public MainWindow()
    {
        InitializeComponent();

        _logoOff = new Bitmap(AssetLoader.Open(
            new Uri("avares://UniversalDreamcastPatcher/Assets/udp_logo_4.png")));
        _logoOn = new Bitmap(AssetLoader.Open(
            new Uri("avares://UniversalDreamcastPatcher/Assets/udp_logo_4_led.png")));

        ButtonSelectGDI.Click += ButtonSelectGDI_Click;
        ButtonSelectPatch.Click += ButtonSelectPatch_Click;
        ButtonApplyPatch.Click += ButtonApplyPatch_Click;
        ButtonQuit.Click += (_, _) => Close();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        string toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
        var missing = Patcher.CheckTools(toolsDir);

        if (missing.Count > 0)
        {
            string list = string.Join("\n", missing.Select(t => $"  - {t}"));
            await Dialogs.ShowAlert(this,
                $"One or more required tools are missing from the \"tools\" folder:\n\n{list}");
            Close();
        }
    }

    private async void ButtonSelectGDI_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select GDI or CUE",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("GDI and CUE files") { Patterns = new[] { "*.gdi", "*.cue" } },
                new FilePickerFileType("All files")         { Patterns = new[] { "*" } },
            }
        });

        if (files.Count == 0) return;

        _gdiFile = files[0].TryGetLocalPath();
        if (_gdiFile == null) return;

        // Show "Select Patch", hide "Apply Patch" (resets if user re-selects GDI)
        ButtonSelectPatch.IsVisible = true;
        ButtonApplyPatch.IsVisible = false;
        _patchFile = null;
    }

    private async void ButtonSelectPatch_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select DCP Patch",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("DCP patch files") { Patterns = new[] { "*.dcp" } },
                new FilePickerFileType("All files")       { Patterns = new[] { "*" } },
            }
        });

        if (files.Count == 0) return;

        _patchFile = files[0].TryGetLocalPath();
        if (_patchFile == null) return;

        ButtonSelectPatch.IsVisible = false;
        ButtonApplyPatch.IsVisible = true;
    }

    private async void ButtonApplyPatch_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_gdiFile == null || _patchFile == null) return;

        string patchName = Path.GetFileNameWithoutExtension(_patchFile);
        string outputFolder = patchName + " [GDI]";

        bool confirmed = await Dialogs.ShowConfirm(this,
            $"The source disc image will not be overwritten.\n\n" +
            $"The patched GDI will be created in:\n\n{outputFolder}\n\n" +
            $"Are you ready to proceed?");

        if (!confirmed) return;

        // ── Switch to progress UI ────────────────────────────────────────────
        ButtonsPanel.IsVisible = false;
        ProgressPanel.IsVisible = true;
        ProgressBar.Value = 0;
        ProgressPercentage.Text = "0%";
        ProgressDetails.Text = "Starting...";

        await AnimateLogo();

        string toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
        string gdiFile = _gdiFile;
        string patchFile = _patchFile;

        var progress = new Progress<PatchProgress>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                ProgressBar.Value = p.Percent;
                ProgressPercentage.Text = $"{p.Percent}%";
                ProgressDetails.Text = p.Details;
            });
        });

        try
        {
            string outputPath = await Task.Run(() => Patcher.ApplyPatch(gdiFile, patchFile, toolsDir, progress));

            await Dialogs.ShowAlert(this,
                $"The patch was successfully applied!\n\n" +
                $"The new GDI is in:\n\n{outputPath}\n\nHave fun!");
        }
        catch (PatchException ex)
        {
            await Dialogs.ShowAlert(this, ex.Message);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAlert(this, $"An unexpected error occurred:\n\n{ex.Message}");
        }
        finally
        {
            ResetUI();
        }
    }

    // Flash the logo LED on/off three times then leave it on during patching.
    private async Task AnimateLogo()
    {
        for (int i = 0; i < 3; i++)
        {
            LogoImage.Source = _logoOn;
            await Task.Delay(150);
            LogoImage.Source = _logoOff;
            await Task.Delay(150);
        }
        LogoImage.Source = _logoOn;
        await Task.Delay(500);
    }

    private void ResetUI()
    {
        LogoImage.Source = _logoOff;
        ProgressPanel.IsVisible = false;
        ProgressBar.Value = 0;
        ButtonsPanel.IsVisible = true;
        ButtonSelectGDI.IsVisible = true;
        ButtonSelectPatch.IsVisible = false;
        ButtonApplyPatch.IsVisible = false;
        _gdiFile = null;
        _patchFile = null;
    }
}
