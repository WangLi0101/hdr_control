using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DisplayQuickControl.Models;
using DisplayQuickControl.Services;
using DisplayQuickControl.ViewModels;
using DisplayQuickControl.Views;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        if (!Environment.UserInteractive) { Console.WriteLine("SKIP: interactive Windows desktop is required for WPF smoke checks."); return 0; }
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dqc-wpf-" + Guid.NewGuid());
        var errors = new StringBuilder();
        var listener = new TextWriterTraceListener(new System.IO.StringWriter(errors));
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/DisplayQuickControl;component/Resources/Styles/Controls.xaml") });
            var settings = new SettingsService(root, new LogService(root));
            settings.Current.RefreshOnOpen = false;
            var log = new LogService(root); var theme = new ThemeService(); theme.Apply("Dark");
            using var tray = new TrayService(root, log);
            var vm = new ControlPanelViewModel(settings, new DisplayService(log), new ColorControlService(settings,log),log);
            var panel = new ControlPanel(vm, settings, new PanelPositionService(tray));
            Pump(panel.OpenAsync());
            Require(panel.IsVisible && panel.ActualWidth > 100 && panel.ActualHeight > 100, "Panel opens and measures");
            Require(!panel.ShowInTaskbar && panel.Topmost, "Flyout window flags");
            var settingsVm = new SettingsViewModel(settings.Copy(), _ => "");
            var window = new SettingsWindow(settingsVm); window.Show();
            panel.Show(); panel.Activate(); Pump(Task.Delay(80));
            Require(window.IsVisible, "Settings remains open after losing focus");
            foreach (var t in new[] { "Light", "Dark", "System" }) { theme.Apply(t); window.UpdateLayout(); panel.UpdateLayout(); }
            Require(HotkeyService.Parse("Ctrl+Alt+H") == (0x4003u, 0x48u), "Default hotkey virtual key mapping");
            var duplicate = new AppSettings { SdrHotkey = "Alt+Ctrl+H" };
            try { HotkeyService.Validate(duplicate); throw new Exception("Duplicate shortcut accepted"); } catch (FormatException) { }
            using var keys = new HotkeyService(new WindowInteropHelper(window).Handle);
            var shortcut = new AppSettings { HdrHotkey = "Ctrl+Alt+Shift+F11", SdrHotkey = "", SafeHotkey = "" };
            var warning = keys.Apply(shortcut);
            if (warning.Length == 0)
            {
                using var conflict = new HotkeyService(new WindowInteropHelper(panel).Handle);
                Require(conflict.Apply(shortcut).Length > 0, "Occupied global shortcut reports conflict");
            }
            else Console.WriteLine("SKIP: test shortcut already occupied.");
            Pump(Task.Delay(100)); listener.Flush();
            Require(errors.Length == 0, "No WPF binding errors: " + errors);
            window.Close(); panel.CloseForExit(); app.Shutdown();
            Console.WriteLine("Windows WPF smoke checks passed. No display presets were applied."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); listener.Dispose(); if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root,true); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        _ = task.ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
}
