using Avalonia.Controls;
using Avalonia.Interactivity;
using Lychee.Core;

namespace Lychee.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly ModuleManager _moduleManager;
    private bool _isInitialized;

    public SettingsWindow(SettingsService settings, ModuleManager moduleManager)
    {
        InitializeComponent();
        _settings = settings;
        _moduleManager = moduleManager;

        AlwaysShowCheckBox.IsChecked = _settings.Current.AlwaysShowPanel;
        AlertIpCheckBox.IsChecked = _settings.Current.AlertOnIpChange;
        AlertQueryFailCheckBox.IsChecked = _settings.Current.AlertOnQueryFailure;
        SnapCheckBox.IsChecked = _settings.Current.SnapToEdge;
        ModuleToggleList.ItemsSource = _moduleManager.Modules.ToList();
        _isInitialized = true;
    }

    /// <summary>
    /// Window.Owner is protected in Avalonia; this lets the owning window
    /// establish the ownership relationship from the outside.
    /// </summary>
    public void AttachOwner(Window owner) => Owner = owner;

    private void AlwaysShow_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        _settings.Update(s => s.AlwaysShowPanel = AlwaysShowCheckBox.IsChecked == true);
    }

    private void AlertIp_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        _settings.Update(s => s.AlertOnIpChange = AlertIpCheckBox.IsChecked == true);
    }

    private void AlertQueryFail_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        _settings.Update(s => s.AlertOnQueryFailure = AlertQueryFailCheckBox.IsChecked == true);
    }

    private void Snap_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        _settings.Update(s => s.SnapToEdge = SnapCheckBox.IsChecked == true);
    }

    private void ModuleToggle_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        if (sender is not CheckBox cb) return;
        if (cb.DataContext is not IInfoModule module) return;

        _moduleManager.SetEnabled(module.Id, cb.IsChecked == true);
    }
}
