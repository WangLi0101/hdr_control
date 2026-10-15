using System.Windows;
using System.Windows.Interop;
using DisplayQuickControl.ViewModels;
using static DisplayQuickControl.Native.NativeMethods;

namespace DisplayQuickControl.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent(); DataContext = viewModel; viewModel.Saved += Close;
        SourceInitialized += (_, _) => { var rounded = 2; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref rounded, 4); Services.ThemeService.ConfigureWindow(this); };
    }
    private void CancelClick(object sender, RoutedEventArgs e) => Close();
    private void CustomizeModesClick(object sender, RoutedEventArgs e)
    {
        var settings = (SettingsViewModel)DataContext;
        var editor = new ModeEditorViewModel(settings.Draft.Modes, modes => { settings.ReplaceModes(modes); return ""; });
        new ModeEditorWindow(editor) { Owner = this }.ShowDialog();
    }
}
