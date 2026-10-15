using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
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
            Require(vm.CurrentModeName == "未读取（请刷新状态）" && vm.Modes.All(m => !m.IsActive), "Unread display state does not select any preset");
            var panel = new ControlPanel(vm, settings, new PanelPositionService(tray));
            Pump(panel.OpenAsync());
            Require(panel.IsVisible && panel.ActualWidth > 100 && panel.ActualHeight > 100, "Panel opens and measures");
            Require(!panel.ShowInTaskbar && panel.Topmost, "Flyout window flags");
            var display = new DisplayState("test-display-path", "27G5K-Mini", "Test adapter", 2560, 1440, 180, false, 8, "RGB", true)
            { AutoColorManagementEnabled = true };
            vm.Displays.Add(display); vm.SelectedDisplay = display;
            Pump(Task.Delay(50)); panel.UpdateLayout();
            CheckModeSelection(panel, vm, "SDR 180", "sdr");
            Require(Descendants<TextBlock>(panel).Any(t => t.Text == "自动颜色管理：开"), "Panel displays Windows ACM preference independently of SDR and NVIDIA color policy");
            Require(vm.Modes.Single(m => m.Definition.Id == "sdr").Caption == "默认颜色 · SDR"
                && !vm.Modes.Single(m => m.Definition.Id == "sdr").Details.Contains("8 位"), "SDR card displays default colors without claiming a fixed bit depth");
            var hdrDisplay = display with { HdrEnabled = true };
            vm.Displays.Add(hdrDisplay); vm.SelectedDisplay = hdrDisplay; panel.UpdateLayout();
            CheckModeSelection(panel, vm, "HDR 180", "hdr");
            var customDisplay = display with { RefreshRate = 165 };
            vm.Displays.Add(customDisplay); vm.SelectedDisplay = customDisplay; panel.UpdateLayout();
            Require(vm.CurrentModeName == "自定义模式（未匹配预设）" && vm.Modes.All(m => !m.IsActive), "External custom display modes clear the previous card selection");
            var unknownDisplay = display with { HdrEnabled = null };
            vm.Displays.Add(unknownDisplay); vm.SelectedDisplay = unknownDisplay; panel.UpdateLayout();
            Require(vm.CurrentModeName == "模式未知（HDR 状态不可用）" && vm.Modes.All(m => !m.IsActive), "Unknown HDR state never claims a selected mode");
            var partialDisplay = display with { BitsPerChannel = null, ColorEncoding = "Unknown" };
            vm.Displays.Add(partialDisplay); vm.SelectedDisplay = partialDisplay; panel.UpdateLayout();
            Require(vm.CurrentModeName == "SDR 180（部分色彩参数未知）", "Current mode reports unavailable optional color information");
            vm.SelectedDisplay = display; panel.UpdateLayout();
            var displayPicker = Descendants<ComboBox>(panel).Single(c => AutomationProperties.GetName(c) == "目标显示器");
            Require(Descendants<TextBlock>(displayPicker).Any(t => t.Text == display.Label), "Selected display renders only its friendly label");
            displayPicker.IsDropDownOpen = true; Pump(Task.Delay(50));
            var displayItem = (ComboBoxItem)displayPicker.ItemContainerGenerator.ContainerFromIndex(0);
            Require(Descendants<TextBlock>(displayItem).Any(t => t.Text == display.Label), "Display dropdown renders friendly labels");
            displayPicker.IsDropDownOpen = false;
            var initialized = 0;
            var settingsVm = new SettingsViewModel(settings.Copy(), _ => "", draft =>
            {
                initialized++; Require(draft.Modes.Count == 3, "Initialization receives current settings draft modes");
                return "初始化完成";
            });
            var window = new SettingsWindow(settingsVm); window.Show();
            var initButton = Descendants<Button>(window).Single(b => Equals(b.Content, "初始化 ColorControl 预设"));
            Require(initButton.Command!.CanExecute(null), "Initialize preset button is enabled and bound");
            initButton.Command.Execute(null);
            Require(initialized == 1 && settingsVm.Status == "初始化完成" && window.IsVisible, "Initialization displays result without closing settings or switching modes");
            var syncOption = Descendants<CheckBox>(window).Single(c => Equals(c.Content, "保存模式时同步到 ColorControl"));
            Require(syncOption.IsChecked == true, "Custom mode synchronization is enabled by default");
            syncOption.IsChecked = false;
            Require(!settingsVm.Draft.SyncColorControlPresets, "Preset synchronization can be disabled without changing live settings");
            panel.Show(); panel.Activate(); Pump(Task.Delay(80));
            Require(window.IsVisible, "Settings remains open after losing focus");
            var themePicker = Descendants<ComboBox>(window).Single(c => AutomationProperties.GetName(c) == "主题");
            foreach (var option in settingsVm.Themes)
            {
                themePicker.SelectedValue = option.Value; Pump(Task.Delay(30)); window.UpdateLayout();
                Require(settingsVm.Draft.Theme == option.Value && Descendants<TextBlock>(themePicker).Any(t => t.Text == option.Label), "Theme selection renders Chinese label and preserves value: " + option.Label);
            }
            foreach (var t in new[] { "Light", "Dark", "System" })
            {
                theme.Apply(t); window.UpdateLayout(); panel.UpdateLayout();
                CheckModeSelection(panel, vm, "SDR 180", "sdr");
            }
            Require(HotkeyService.Parse("Ctrl+Alt+H") == (0x4003u, 0x48u), "Default hotkey virtual key mapping");
            var duplicate = new AppSettings(); duplicate.Modes[1].Hotkey = "Alt+Ctrl+H";
            try { HotkeyService.Validate(duplicate); throw new Exception("Duplicate shortcut accepted"); } catch (FormatException) { }
            using var keys = new HotkeyService(new WindowInteropHelper(window).Handle);
            var shortcut = new AppSettings { Modes = [DisplayMode.Hdr180 with { Hotkey = "Ctrl+Alt+Shift+F11" }] };
            var warning = keys.Apply(shortcut);
            if (warning.Length == 0)
            {
                using var conflict = new HotkeyService(new WindowInteropHelper(panel).Handle);
                Require(conflict.Apply(shortcut).Length > 0, "Occupied global shortcut reports conflict");
            }
            else Console.WriteLine("SKIP: test shortcut already occupied.");
            var savedCount = 0; List<DisplayMode>? saved = null;
            var editorVm = new ModeEditorViewModel(settings.Current.Modes, modes => { saved = modes; savedCount++; return ""; });
            editorVm.AddCommand.Execute(null); var added = editorVm.Selected!;
            added.Name = "电影 4K"; added.PresetName = "Movie-4K"; added.Width = "3840"; added.Height = "2160"; added.Hertz = "invalid";
            editorVm.SaveCommand.Execute(null);
            Require(savedCount == 0 && editorVm.Status.Length > 0, "Invalid input does not save or close mode editor");
            added.Hertz = "59.94"; added.IsSafeFallback = true;
            Require(editorVm.Modes.Count(m => m.IsSafeFallback) == 1, "Choosing a fallback clears the previous fallback");
            editorVm.UpCommand.Execute(null); editorVm.SaveCommand.Execute(null);
            Require(saved?.Count == 4 && saved[2].Hertz == 59.94 && saved[2].Name == "电影 4K", "Editor adds, reorders and saves a custom fractional-rate mode");
            Require(settings.Current.Modes.Count == 3, "Editor leaves live settings unchanged until accepted");
            editorVm.DeleteCommand.Execute(null); editorVm.SaveCommand.Execute(null);
            Require(saved?.Count == 3 && saved.All(m => m.Name != "电影 4K"), "Editor deletes selected mode");
            var modeWindow = new ModeEditorWindow(new ModeEditorViewModel(settings.Current.Modes, _ => "")); modeWindow.Show();
            var colorEditor = (ModeEditorViewModel)modeWindow.DataContext;
            colorEditor.Selected = colorEditor.Modes.Single(m => m.Name == "SDR 180"); modeWindow.UpdateLayout();
            var defaultColors = Descendants<CheckBox>(modeWindow).Single(c => AutomationProperties.GetName(c) == "使用默认颜色设置");
            var bitDepth = Descendants<ComboBox>(modeWindow).Single(c => AutomationProperties.GetName(c) == "目标色深");
            Require(defaultColors.IsChecked == true && !bitDepth.IsEnabled && colorEditor.Selected.ToMode().UseDefaultColorSettings, "SDR editor roundtrips default color policy and disables fixed color controls");
            defaultColors.IsChecked = false; modeWindow.UpdateLayout();
            Require(bitDepth.IsEnabled && !colorEditor.Selected.ToMode().UseDefaultColorSettings, "Disabling default colors enables editing fixed color values");
            vm.ReloadModes(); Require(vm.Modes.Count == 3, "Panel creates cards from saved modes");
            settings.Current.Modes = [DisplayMode.Sdr180 with { Name = "One custom mode", Hertz = 165, PresetName = "SDR-165" }];
            vm.ReloadModes(); Require(vm.Modes.Count == 1 && vm.Modes[0].Hertz == "165", "Panel reload removes fixed cards and uses edited targets");
            Require(vm.CurrentModeName == "自定义模式（未匹配预设）", "Editing saved mode targets updates the current mode label");
            Pump(Task.Delay(100)); listener.Flush();
            Require(errors.Length == 0, "No WPF binding errors: " + errors);
            modeWindow.Close(); window.Close(); panel.CloseForExit(); app.Shutdown();
            Console.WriteLine("Windows WPF smoke checks passed. No display presets were applied."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); listener.Dispose(); if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root,true); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    private static void CheckModeSelection(ControlPanel panel, ControlPanelViewModel vm, string name, string id)
    {
        Require(vm.CurrentModeName == name && vm.Modes.Count(m => m.IsActive) == 1 && vm.Modes.Single(m => m.IsActive).Definition.Id == id, "Actual display state identifies the current mode: " + name);
        var label = Descendants<TextBlock>(panel).Single(t => AutomationProperties.GetName(t) == "当前模式名称");
        Require(label.Text == name, "Current mode name is rendered in the panel");
        var cards = Descendants<Button>(panel).Where(b => b.DataContext is ModeCardViewModel).ToArray();
        foreach (var button in cards)
        {
            var card = (ModeCardViewModel)button.DataContext;
            var chrome = (Border)button.Template.FindName("CardChrome", button);
            var expected = (SolidColorBrush)Application.Current.Resources[card.IsActive ? "Selected" : "Card"];
            Require(chrome.BorderThickness == new Thickness(card.IsActive ? 2 : 1) && ((SolidColorBrush)chrome.Background).Color == expected.Color, "Card selection has the expected visible background and border: " + card.Name);
            var badge = Descendants<TextBlock>(button).Single(t => t.Text == "当前模式");
            Require(badge.Visibility == Visibility.Visible && ((UIElement)VisualTreeHelper.GetParent(badge)).Visibility == (card.IsActive ? Visibility.Visible : Visibility.Collapsed)
                && AutomationProperties.GetItemStatus(button) == (card.IsActive ? "当前模式" : ""), "Current mode checkmark and accessibility status follow actual selection");
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        _ = task.ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
}
