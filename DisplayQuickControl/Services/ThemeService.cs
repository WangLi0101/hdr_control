using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using Microsoft.Win32;
using static DisplayQuickControl.Native.NativeMethods;

namespace DisplayQuickControl.Services;

public sealed class ThemeService
{
    public void Apply(string theme)
    {
        var dark = theme == "Dark";
        if (theme == "System")
        {
            try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); dark = key?.GetValue("AppsUseLightTheme") is not int value || value == 0; }
            catch (System.Security.SecurityException) { dark = true; }
        }
        var colors = dark
            ? new[] { "#25262B", "#303238", "#3B3E46", "#F3F4F7", "#ADB1BE", "#484C57", "#98BFFF", "#263F64", "#B6D2FF", "#FFD0BC" }
            : new[] { "#F3F4F8", "#FFFFFF", "#E7EAF1", "#20232B", "#5A6271", "#CED3DF", "#235EB2", "#DBE9FF", "#164E98", "#A43A17" };
        var names = new[] { "Panel", "Card", "Hover", "Text", "Muted", "Stroke", "Accent", "Selected", "SelectedText", "Warning" };
        for (var i = 0; i < names.Length; i++)
            Application.Current.Resources[names[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        foreach (Window window in Application.Current.Windows) ConfigureWindow(window);
    }
    public static void ConfigureWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        var color = ((SolidColorBrush)Application.Current.Resources["Panel"]).Color;
        var dark = color.R < 128 ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, 4);
    }
}
