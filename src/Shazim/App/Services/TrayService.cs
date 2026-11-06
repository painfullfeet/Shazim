using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Shazim.App.Infrastructure;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Shazim.App.Services;

/// <summary>
/// Notification-area icon with a right-click menu drawn like the rest of the app
/// (dark, rounded, Outfit, vector icons), plus Windows notifications.
/// </summary>
public sealed class TrayService : IDisposable
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly Forms.NotifyIcon _icon;
    private readonly ContextMenu _menu;
    private readonly TextBlock _statusText;
    private readonly Border _autoSwitch;
    private readonly Border _autoKnob;
    private Window? _anchor;
    private Action? _balloonClick;

    public TrayService(Action open, Action identify, Action toggleAuto, Action openMini, Action exit)
    {
        _statusText = new TextBlock { Text = Loc.T("tray.auto_off"), FontSize = 12, Foreground = Res<Brush>("MistBrush"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 190 };
        _autoKnob = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Res<Brush>("MistBrush"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 3, 0) };
        _autoSwitch = new Border { Width = 34, Height = 18, CornerRadius = new CornerRadius(9), Background = Res<Brush>("DuskBrush"), Child = _autoKnob, VerticalAlignment = VerticalAlignment.Center };

        _menu = new ContextMenu { MinWidth = 250, Placement = PlacementMode.MousePoint, HasDropShadow = true };
        _menu.Items.Add(Header());
        _menu.Items.Add(Item(Loc.T("tray.open"), "IconAppWindow", open));
        _menu.Items.Add(Item(Loc.T("tray.identify"), "IconWaveform", identify, accent: true));
        _menu.Items.Add(Item(Loc.T("mainwin.auto_shazim"), "IconMusicNotes", toggleAuto, trailing: _autoSwitch));
        _menu.Items.Add(Item(Loc.T("mainwin.mini_lecteur"), "IconPictureInPicture", openMini));
        _menu.Items.Add(Item(Loc.T("mainwin.statistiques"), "IconChartBar", () => { open(); App.Current.NavigateMain("stats"); }));
        _menu.Items.Add(new Separator { Style = Res<Style>("MenuSeparator") });
        _menu.Items.Add(Item(Loc.T("tray.quit"), "IconPower", exit));

        _icon = new Forms.NotifyIcon { Icon = LoadIcon(), Text = "Shazim", Visible = true };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) open();
            else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
        };
        _icon.BalloonTipClicked += (_, _) => (_balloonClick ?? open)();
    }

    private static T Res<T>(string key) => (T)Application.Current.FindResource(key);

    private UIElement Header()
    {
        var logo = new Grid { Width = 32, Height = 32, Margin = new Thickness(0, 0, 12, 0) };
        var disc = new System.Windows.Shapes.Ellipse();
        disc.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "SignalGradientBrush"); // follows the accent colour
        logo.Children.Add(disc);
        if (Brand.Logo != null) logo.Children.Add(new Image { Source = Brand.Logo, Width = 14, Height = 14 });

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = "Shazim", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Res<Brush>("PaperBrush"), FontFamily = Res<FontFamily>("DisplayFont") });
        texts.Children.Add(_statusText);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 12, 10), Children = { logo, texts } };
        return new MenuItem { Header = row, IsHitTestVisible = false, Focusable = false, Style = Res<Style>("MenuHeaderItem") };
    }

    private MenuItem Item(string text, string icon, Action action, bool accent = false, UIElement? trailing = null)
    {
        var grid = new Grid { MinWidth = 210 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new IconView
        {
            Data = IconView.Get(icon), Width = 18, Height = 18, Margin = new Thickness(0, 0, 12, 0),
        };
        glyph.SetResourceReference(Control.ForegroundProperty, accent ? "SignalBrush" : "MistBrush");
        var label = new TextBlock { Text = text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = Res<Brush>("PaperBrush") };
        Grid.SetColumn(label, 1);
        grid.Children.Add(glyph);
        grid.Children.Add(label);
        if (trailing != null)
        {
            Grid.SetColumn(trailing, 2);
            grid.Children.Add(trailing);
        }

        var item = new MenuItem { Header = grid };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Shows the menu at the cursor. A window of ours must be in front for it to close on outside clicks.</summary>
    private void ShowMenu()
    {
        if (_anchor == null)
        {
            _anchor = new Window
            {
                Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = true,
                AllowsTransparency = true, Background = Brushes.Transparent, Left = -32000, Top = -32000, Topmost = true
            };
            _anchor.Show();
        }
        SetForegroundWindow(new WindowInteropHelper(_anchor).Handle);
        _menu.IsOpen = false;
        _menu.IsOpen = true;
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            using var stream = Brand.OpenIcon();
            if (stream != null) return new Drawing.Icon(stream, 32, 32);
        }
        catch { }
        return Drawing.SystemIcons.Application;
    }

    public void SetAutoShazam(bool enabled, string status)
    {
        _statusText.Text = enabled ? Loc.T("mainwin.auto_shazim") + " : " + status : Loc.T("tray.auto_off");
        _autoSwitch.SetResourceReference(Border.BackgroundProperty, enabled ? "SignalBrush" : "DuskBrush");
        _autoKnob.Background = enabled ? Brushes.White : Res<Brush>("MistBrush");
        _autoKnob.HorizontalAlignment = enabled ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        var text = enabled ? $"Shazim · Auto : {status}" : "Shazim";
        _icon.Text = text.Length > 63 ? text[..63] : text; // Windows limit
    }

    /// <param name="sound">False: Windows shows it without its sound (NIIF_NOSOUND).</param>
    public void Notify(string title, string message, Action? onClick = null, bool sound = true)
    {
        _balloonClick = onClick;
        if (!sound && SilentBalloon.Show(_icon, title, message)) return;
        _icon.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.None);
    }

    /// <summary>For tests: the silent notification can find the icon's window.</summary>
    internal bool CanNotifySilently => SilentBalloon.Identity(_icon) != null;

    public void Dispose()
    {
        _menu.IsOpen = false;
        _anchor?.Close();
        _icon.Visible = false;
        _icon.Dispose();
    }
}
