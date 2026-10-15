using System.Windows;
using System.Windows.Interop;
using DisplayQuickControl.Services;
using DisplayQuickControl.ViewModels;
using static DisplayQuickControl.Native.NativeMethods;

namespace DisplayQuickControl.Views;

public partial class ModeEditorWindow : Window
{
    public ModeEditorWindow(ModeEditorViewModel viewModel)
    {
        InitializeComponent(); DataContext = viewModel; viewModel.Saved += Close;
        SourceInitialized += (_, _) => { var rounded = 2; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref rounded, 4); ThemeService.ConfigureWindow(this); };
    }
    private void CancelClick(object sender, RoutedEventArgs e) => Close();
}
