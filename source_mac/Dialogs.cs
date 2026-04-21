using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace UniversalDreamcastPatcher;

/// <summary>
/// Minimal modal dialogs for Avalonia 12 (no third-party dependency).
/// </summary>
public static class Dialogs
{
    public static Task ShowAlert(Window owner, string message) =>
        Show(owner, message, confirm: false);

    public static Task<bool> ShowConfirm(Window owner, string message) =>
        Show(owner, message, confirm: true);

    private static async Task<bool> Show(Window owner, string message, bool confirm)
    {
        var tcs = new TaskCompletionSource<bool>();

        var msgBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360,
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };

        var yesButton = new Button
        {
            Content = "Yes",
            Width = 80,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };

        var noButton = new Button
        {
            Content = "No",
            Width = 80,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 10,
        };

        if (confirm)
        {
            buttonRow.Children.Add(yesButton);
            buttonRow.Children.Add(noButton);
        }
        else
        {
            buttonRow.Children.Add(okButton);
        }

        var content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children = { msgBlock, buttonRow },
        };

        var dialog = new Window
        {
            Title = "Universal Dreamcast Patcher",
            Content = content,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        okButton.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        yesButton.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        noButton.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(false);

        await dialog.ShowDialog(owner);
        return await tcs.Task;
    }
}
