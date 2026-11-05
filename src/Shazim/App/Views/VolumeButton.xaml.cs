using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Shazim.App.Infrastructure;
using Shazim.App.Services;

namespace Shazim.App.Views;

/// <summary>Speaker button that opens a slider for the preview volume.</summary>
public partial class VolumeButton : UserControl
{
    private readonly PreviewPlayer _player;
    private double _beforeMute = 0.25;
    private bool _syncing;

    public VolumeButton()
    {
        InitializeComponent();
        _player = App.Current.Services.GetRequiredService<PreviewPlayer>();

        Sync();
        _player.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PreviewPlayer.Volume)) Sync(); };
        // Avoid the popup closing and reopening when its own button is clicked.
        Panel.Opened += (_, _) => Toggle.IsHitTestVisible = false;
        Panel.Closed += (_, _) => { Toggle.IsHitTestVisible = true; _player.SaveVolume(); };
    }

    private void Sync()
    {
        _syncing = true;
        VolumeSlider.Value = _player.Volume;
        _syncing = false;

        var v = _player.Volume;
        PercentText.Text = $"{Math.Round(v * 100)} %";
        var icon = IconView.Get(v <= 0.001 ? "IconSpeakerX" : v < 0.5 ? "IconSpeakerLow" : "IconSpeakerHigh");
        Toggle.Tag = icon;
        MuteButton.Tag = icon;
        Toggle.ToolTip = Loc.F("volume.tooltip", Math.Round(v * 100));
    }

    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_syncing) _player.Volume = e.NewValue;
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (_player.Volume > 0.001)
        {
            _beforeMute = _player.Volume;
            _player.Volume = 0;
        }
        else
        {
            _player.Volume = _beforeMute > 0.001 ? _beforeMute : 0.25;
        }
    }
}
