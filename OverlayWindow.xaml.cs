using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Screen = System.Windows.Forms.Screen;
using VoiceStudio.Core;

namespace VoiceStudio;
public partial class OverlayWindow : Window
{
    private bool locked = true;
    private int screenIndex;
    private CaptionMode mode;
    private string original = "", translation = "";
    private bool showingCaption;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr h, int index, int value);
    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => SetLocked(locked);
        SizeChanged += (_, _) => ClampToScreen();
    }
    public void SetText(string text)
    {
        showingCaption = false;
        Subtitle.Text = string.IsNullOrWhiteSpace(text) ? "等待语音…" : text;
        TranslatedSubtitle.Visibility = Visibility.Collapsed;
    }
    public void SetCaption(string text, string translated = "")
    {
        showingCaption = true; original = text; translation = translated; RenderCaption();
    }
    public void SetMode(CaptionMode value) { mode = value; if (showingCaption) RenderCaption(); }
    private void RenderCaption()
    {
        Subtitle.Text = mode == CaptionMode.Translation ? (translation.Length > 0 ? translation : "正在翻译…") : original;
        TranslatedSubtitle.Text = translation.Length > 0 ? translation : "正在翻译…";
        TranslatedSubtitle.Visibility = mode == CaptionMode.Bilingual && original != translation ? Visibility.Visible : Visibility.Collapsed;
    }
    public void SetStyle(double fontSize, double opacity)
    {
        Subtitle.FontSize = fontSize;
        TranslatedSubtitle.FontSize = fontSize;
        Backdrop.Background = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 16, 25, 33));
    }
    public void SetLocked(bool value)
    {
        locked = value;
        Hint.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        int style = GetWindowLong(handle, -20);
        SetWindowLong(handle, -20, value ? style | 0x20 | 0x08000000 : style & ~0x20 & ~0x08000000);
    }
    public void PlaceOnScreen(int index)
    {
        screenIndex = Math.Clamp(index, 0, Screen.AllScreens.Length - 1);
        var area = Screen.AllScreens[screenIndex].WorkingArea;
        var scale = VisualTreeHelper.GetDpi(this);
        Width = Math.Min(850, area.Width / scale.DpiScaleX - 48);
        Left = (area.Left + area.Width / 2.0) / scale.DpiScaleX - Width / 2;
        Top = (area.Bottom - 90) / scale.DpiScaleY - ActualHeight;
        ClampToScreen();
    }
    public void ClampToScreen()
    {
        if (!IsVisible) return;
        var screens = Screen.AllScreens;
        var area = screens[Math.Clamp(screenIndex, 0, screens.Length - 1)].WorkingArea;
        var scale = VisualTreeHelper.GetDpi(this);
        Left = Math.Clamp(Left, area.Left / scale.DpiScaleX, Math.Max(area.Left / scale.DpiScaleX, area.Right / scale.DpiScaleX - ActualWidth));
        Top = Math.Clamp(Top, area.Top / scale.DpiScaleY, Math.Max(area.Top / scale.DpiScaleY, area.Bottom / scale.DpiScaleY - ActualHeight));
    }
    private void Drag_Click(object sender, MouseButtonEventArgs e)
    {
        if (!locked && e.ButtonState == MouseButtonState.Pressed) { DragMove(); ClampToScreen(); }
    }
}
