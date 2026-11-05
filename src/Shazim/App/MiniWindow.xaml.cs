using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Shazim.App.Services;
using Shazim.Core.Recognition;

namespace Shazim.App;

/// <summary>Small always-on-top player: one button and the last result.</summary>
public partial class MiniWindow : Window
{
    private readonly IdentifyController _controller;

    public MiniWindow(IServiceProvider services)
    {
        _controller = services.GetRequiredService<IdentifyController>();
        InitializeComponent();
        Glyph.Source = Infrastructure.Brand.Logo;

        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 24;
        Top = area.Bottom - Height - 24;

        _controller.PropertyChanged += OnControllerChanged;
        Closed += (_, _) => _controller.PropertyChanged -= OnControllerChanged;
        Update();
    }

    private void OnControllerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IdentifyController.IsBusy) or nameof(IdentifyController.Phase) or nameof(IdentifyController.LastOutcome))
            Update();
    }

    private void Update()
    {
        bool busy = _controller.IsBusy;
        ListenButton.Tag = busy ? "busy" : null;
        Glyph.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        StopGlyph.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        if (busy)
        {
            TitleText.Text = Loc.T(_controller.Phase == IdentifyPhase.Searching ? "home.searching" : "home.listening");
            SubText.Text = Loc.T("mini.keep_sound");
            return;
        }

        var o = _controller.LastOutcome;
        if (o == null) return;
        if (o.Kind == IdentifyOutcomeKind.Match && o.Track != null)
        {
            TitleText.Text = o.Track.DisplayTitle;
            SubText.Text = o.Track.DisplayArtist;
        }
        else
        {
            TitleText.Text = o.Kind switch
            {
                IdentifyOutcomeKind.Silence => Loc.T("home.no_sound"),
                IdentifyOutcomeKind.NoMatch => Loc.T("home.no_result"),
                _ => Loc.T("home.cannot_recognise")
            };
            SubText.Text = o.Kind == IdentifyOutcomeKind.Error ? o.Message : Loc.T("mini.try_again");
        }
    }

    private async void Listen_Click(object sender, RoutedEventArgs e) => await _controller.ToggleAsync();

    private void Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Expand_Click(object sender, RoutedEventArgs e)
    {
        App.Current.ShowMainWindow();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
