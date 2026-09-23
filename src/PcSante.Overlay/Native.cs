using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PcSante.Overlay;

/// <summary>API Win32 minimales pour une fenêtre superposée légère (sans WPF : objectif &lt; 30 Mo de RAM).</summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class Native
{
    public const uint WsPopup = 0x80000000;
    public const uint WsExLayered = 0x00080000;
    public const uint WsExTransparent = 0x00000020;
    public const uint WsExTopmost = 0x00000008;
    public const uint WsExToolWindow = 0x00000080;
    public const uint WsExNoActivate = 0x08000000;
    public const uint LwaAlpha = 0x2;

    public const uint WmDestroy = 0x0002;
    public const uint WmPaint = 0x000F;
    public const uint WmClose = 0x0010;
    public const uint WmTimer = 0x0113;
    public const uint WmCommand = 0x0111;
    public const uint WmDpiChanged = 0x02E0;
    public const uint WmApp = 0x8000;
    public const uint WmRButtonUp = 0x0205;
    public const uint WmContextMenu = 0x007B;

    public const int SwHide = 0;
    public const int SwShowNoActivate = 4;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpNoZOrder = 0x0004;
    public static readonly IntPtr HwndTopmost = new(-1);

    public const uint SpiGetWorkArea = 0x0030;
    public const int Transparent = 1;
    public const uint DtSingleLine = 0x20;
    public const uint DtVCenter = 0x4;
    public const uint DtCenter = 0x1;
    public const uint DtRtlReading = 0x20000;
    public const uint DtNoPrefix = 0x800;

    public const uint NimAdd = 0;
    public const uint NimModify = 1;
    public const uint NimDelete = 2;
    public const uint NifMessage = 0x1;
    public const uint NifIcon = 0x2;
    public const uint NifTip = 0x4;
    public const uint NifShowTip = 0x80;
    public const uint TpmReturnCmd = 0x0100;
    public const uint TpmRightButton = 0x0002;
    public const uint MfString = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Size
    {
        public int Cx, Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PaintStruct
    {
        public IntPtr Hdc;
        public int Erase;
        public Rect Paint;
        public int Restore;
        public int IncUpdate;
        public fixed byte Reserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WndClassEx
    {
        public uint CbSize;
        public uint Style;
        public delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr> WndProc;
        public int ClsExtra;
        public int WndExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public IntPtr MenuName;
        public IntPtr ClassName;
        public IntPtr IconSmall;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NotifyIconData
    {
        public uint CbSize;
        public IntPtr Hwnd;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        public fixed char Tip[128];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint Version;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial ushort RegisterClassExW(in WndClassEx wc);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll")]
    public static partial IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(out Msg msg, IntPtr hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(in Msg msg);

    [LibraryImport("user32.dll")]
    public static partial IntPtr DispatchMessageW(in Msg msg);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int code);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(IntPtr hwnd, nuint id, uint elapse, IntPtr func);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InvalidateRect(IntPtr hwnd, IntPtr rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [LibraryImport("user32.dll")]
    public static partial IntPtr BeginPaint(IntPtr hwnd, out PaintStruct ps);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EndPaint(IntPtr hwnd, in PaintStruct ps);

    [LibraryImport("user32.dll")]
    public static partial int FillRect(IntPtr hdc, in Rect rect, IntPtr brush);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int DrawTextW(IntPtr hdc, string text, int count, ref Rect rect, uint format);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hwnd, int cmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(IntPtr hwnd);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfoRect(uint action, uint param, out Rect rect, uint winIni);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetDC(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [LibraryImport("user32.dll")]
    public static partial IntPtr LoadIconW(IntPtr instance, IntPtr name);

    [LibraryImport("user32.dll")]
    public static partial IntPtr CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenuW(IntPtr menu, uint flags, nuint id, string text);

    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(IntPtr menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateSolidBrush(uint color);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(IntPtr obj);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetTextColor(IntPtr hdc, uint color);

    [LibraryImport("gdi32.dll")]
    public static partial int SetBkMode(IntPtr hdc, int mode);

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline,
        uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTextExtentPoint32W(IntPtr hdc, string text, int length, out Size size);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr GetModuleHandleW(string? name);

    /// <summary>État de l'utilisateur : 2 = occupé (plein écran), 3 = jeu Direct3D plein écran, 4 = présentation.</summary>
    [LibraryImport("shell32.dll")]
    public static partial int SHQueryUserNotificationState(out int state);

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shell_NotifyIconW(uint message, in NotifyIconData data);
}
