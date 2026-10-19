using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System.Globalization;
using System.IO;
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
        RefreshBallStyles();
        BallStyleComboBox.SelectedItem = _settings.Current.FloatingBallStyle;
        var fonts = FontManager.Current.SystemFonts.OrderBy(font => font.ToString(), StringComparer.OrdinalIgnoreCase).ToList();
        PanelFontComboBox.ItemsSource = fonts;
        PanelFontComboBox.SelectedItem = fonts.FirstOrDefault(font => string.Equals(font.ToString(), _settings.Current.PanelFontFamily, StringComparison.OrdinalIgnoreCase));
        LoadAppearance(_settings.Current);
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

    private void BallStyle_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || BallStyleComboBox.SelectedItem is not string style) return;
        if (style == "custom" && !File.Exists(_settings.Current.CustomBallImagePath)) return;
        _settings.Update(s => s.FloatingBallStyle = style);
    }

    private async void ImportBallImage_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import floating ball image",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Image files") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"] }]
        });
        if (files.Count == 0) return;
        try
        {
            var source = files[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(source)) return;
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lychee");
            Directory.CreateDirectory(dir);
            var destination = Path.Combine(dir, $"custom-ball-{Guid.NewGuid():N}{Path.GetExtension(source)}");
            File.Copy(source, destination);
            _settings.Update(s => { s.CustomBallImagePath = destination; s.FloatingBallStyle = "custom"; });
            RefreshBallStyles();
        }
        catch (Exception ex)
        {
            AppLog.Error("ball-style-import", ex);
        }
    }

    private void RefreshBallStyles()
    {
        var styles = new List<string> { "default", "basketball", "planet", "moon", "pixel" };
        if (File.Exists(_settings.Current.CustomBallImagePath)) styles.Add("custom");
        BallStyleComboBox.ItemsSource = styles;
        BallStyleComboBox.SelectedItem = _settings.Current.FloatingBallStyle;
    }

    private void LoadAppearance(AppSettings s)
    {
        UseCustomAppearanceCheckBox.IsChecked = s.UseCustomPanelAppearance;
        BackgroundRedTextBox.Text = s.PanelBackgroundRed.ToString(CultureInfo.InvariantCulture);
        BackgroundGreenTextBox.Text = s.PanelBackgroundGreen.ToString(CultureInfo.InvariantCulture);
        BackgroundBlueTextBox.Text = s.PanelBackgroundBlue.ToString(CultureInfo.InvariantCulture);
        TextRedTextBox.Text = s.PanelTextRed.ToString(CultureInfo.InvariantCulture);
        TextGreenTextBox.Text = s.PanelTextGreen.ToString(CultureInfo.InvariantCulture);
        TextBlueTextBox.Text = s.PanelTextBlue.ToString(CultureInfo.InvariantCulture);
        BackgroundOpacitySlider.Value = Math.Clamp(s.PanelBackgroundOpacity, 0, 100);
        OpacityValueText.Text = $"Background opacity: {(int)BackgroundOpacitySlider.Value}%";
        RefreshAppearancePreview();
    }

    private bool TryAppearanceRgb(out Color background, out Color text)
    {
        background = default; text = default;
        if (!TryColor(BackgroundRedTextBox, BackgroundGreenTextBox, BackgroundBlueTextBox, out background) ||
            !TryColor(TextRedTextBox, TextGreenTextBox, TextBlueTextBox, out text)) return false;
        return true;
    }

    private static bool TryColor(TextBox r, TextBox g, TextBox b, out Color color)
    {
        color = default;
        if (!byte.TryParse(r.Text, out var red) || !byte.TryParse(g.Text, out var green) || !byte.TryParse(b.Text, out var blue)) return false;
        color = Color.FromRgb(red, green, blue); return true;
    }

    private void AppearanceColor_Changed(object? sender, TextChangedEventArgs e)
    {
        if (!_isInitialized || !TryAppearanceRgb(out var background, out var text)) return;
        _settings.Update(s =>
        {
            s.UseCustomPanelAppearance = true;
            s.PanelBackgroundRed = background.R; s.PanelBackgroundGreen = background.G; s.PanelBackgroundBlue = background.B;
            s.PanelTextRed = text.R; s.PanelTextGreen = text.G; s.PanelTextBlue = text.B;
        });
        RefreshAppearancePreview();
    }

    private async void PickBackgroundColor_Click(object? sender, RoutedEventArgs e) => await PickColorAsync(background: true);

    private async void PickTextColor_Click(object? sender, RoutedEventArgs e) => await PickColorAsync(background: false);

    private async Task PickColorAsync(bool background)
    {
        var settings = _settings.Current;
        var redBox = background ? BackgroundRedTextBox : TextRedTextBox;
        var greenBox = background ? BackgroundGreenTextBox : TextGreenTextBox;
        var blueBox = background ? BackgroundBlueTextBox : TextBlueTextBox;
        if (!TryColor(redBox, greenBox, blueBox, out var initial))
        {
            initial = background
                ? Color.FromRgb((byte)Math.Clamp(settings.PanelBackgroundRed, 0, 255), (byte)Math.Clamp(settings.PanelBackgroundGreen, 0, 255), (byte)Math.Clamp(settings.PanelBackgroundBlue, 0, 255))
                : Color.FromRgb((byte)Math.Clamp(settings.PanelTextRed, 0, 255), (byte)Math.Clamp(settings.PanelTextGreen, 0, 255), (byte)Math.Clamp(settings.PanelTextBlue, 0, 255));
        }

        var picker = new ColorPickerWindow(initial);
        var selected = await picker.ShowDialog<Color?>(this);
        if (selected is not Color color) return;

        _isInitialized = false;
        redBox.Text = color.R.ToString(CultureInfo.InvariantCulture);
        greenBox.Text = color.G.ToString(CultureInfo.InvariantCulture);
        blueBox.Text = color.B.ToString(CultureInfo.InvariantCulture);
        _isInitialized = true;
        _settings.Update(s =>
        {
            s.UseCustomPanelAppearance = true;
            if (background)
            {
                s.PanelBackgroundRed = color.R;
                s.PanelBackgroundGreen = color.G;
                s.PanelBackgroundBlue = color.B;
            }
            else
            {
                s.PanelTextRed = color.R;
                s.PanelTextGreen = color.G;
                s.PanelTextBlue = color.B;
            }
        });
        UseCustomAppearanceCheckBox.IsChecked = true;
        RefreshAppearancePreview();
    }

    private void UseCustomAppearance_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        _settings.Update(s => s.UseCustomPanelAppearance = UseCustomAppearanceCheckBox.IsChecked == true);
        RefreshAppearancePreview();
    }

    private void BackgroundOpacity_Changed(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_isInitialized) return;
        var opacity = (int)Math.Round(BackgroundOpacitySlider.Value);
        OpacityValueText.Text = $"Background opacity: {opacity}%";
        _settings.Update(s => { s.UseCustomPanelAppearance = true; s.PanelBackgroundOpacity = opacity; });
        RefreshAppearancePreview();
    }

    private void PanelFont_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || PanelFontComboBox.SelectedItem is not FontFamily font) return;
        _settings.Update(s => { s.UseCustomPanelAppearance = true; s.PanelFontFamily = font.ToString(); });
        RefreshAppearancePreview();
    }

    private void RefreshAppearancePreview()
    {
        if (!TryAppearanceRgb(out var bg, out var fg)) return;
        var alpha = (byte)Math.Round(Math.Clamp(BackgroundOpacitySlider.Value, 0, 100) * 2.55);
        AppearancePreviewBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, bg.R, bg.G, bg.B));
        AppearancePreviewText.Foreground = new SolidColorBrush(fg);
        if (PanelFontComboBox.SelectedItem is FontFamily font) AppearancePreviewText.FontFamily = font;
    }

    private void ResetAppearance_Click(object? sender, RoutedEventArgs e)
    {
        _settings.Update(s =>
        {
            s.UseCustomPanelAppearance = false; s.PanelBackgroundRed = 30; s.PanelBackgroundGreen = 30; s.PanelBackgroundBlue = 46;
            s.PanelBackgroundOpacity = 90; s.PanelTextRed = 255; s.PanelTextGreen = 255; s.PanelTextBlue = 255; s.PanelFontFamily = "Arial";
        });
        _isInitialized = false;
        PanelFontComboBox.SelectedItem = (PanelFontComboBox.ItemsSource as IEnumerable<FontFamily>)?.FirstOrDefault(font => font.ToString() == "Arial");
        LoadAppearance(_settings.Current);
        _isInitialized = true;
    }

    private void ModuleToggle_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        if (sender is not CheckBox cb) return;
        if (cb.DataContext is not IInfoModule module) return;

        _moduleManager.SetEnabled(module.Id, cb.IsChecked == true);
    }
}
