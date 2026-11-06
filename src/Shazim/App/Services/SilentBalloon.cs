using System.Reflection;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Shazim.App.Services;

/// <summary>
/// Windows notification without its sound. NotifyIcon.ShowBalloonTip cannot ask for that,
/// so the same icon is updated with Shell_NotifyIcon and NIIF_NOSOUND. Clicks still reach
/// NotifyIcon.BalloonTipClicked (same window, same callback message).
/// </summary>
internal static class SilentBalloon
{
    private const int NIM_MODIFY = 0x1;
    private const int NIF_INFO = 0x10;
    private const int NIIF_NONE = 0x0, NIIF_NOSOUND = 0x10, NIIF_LARGE_ICON = 0x20;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    /// <summary>Window handle and id WinForms uses for this icon (private fields), or null.</summary>
    internal static (IntPtr Hwnd, int Id)? Identity(Forms.NotifyIcon icon)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var t = typeof(Forms.NotifyIcon);
        var window = (t.GetField("_window", flags) ?? t.GetField("window", flags))?.GetValue(icon) as Forms.NativeWindow;
        var idValue = (t.GetField("_id", flags) ?? t.GetField("id", flags))?.GetValue(icon);
        if (window == null || idValue == null || window.Handle == IntPtr.Zero) return null;
        return (window.Handle, Convert.ToInt32(idValue));
    }

    /// <summary>Shows the notification without a sound. False when it could not (use ShowBalloonTip then).</summary>
    public static bool Show(Forms.NotifyIcon icon, string title, string text)
    {
        try
        {
            if (Identity(icon) is not { } id) return false;
            var data = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = id.Hwnd,
                uID = id.Id,
                uFlags = NIF_INFO,
                szTip = "",
                szInfo = Trim(text, 255),
                szInfoTitle = Trim(title, 63),
                uTimeoutOrVersion = 5000,
                dwInfoFlags = NIIF_NONE | NIIF_NOSOUND,
            };
            return Shell_NotifyIcon(NIM_MODIFY, ref data);
        }
        catch
        {
            return false;
        }
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
