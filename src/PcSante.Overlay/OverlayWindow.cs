using System.Globalization;
using System.Resources;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PcSante.Core;
using PcSante.Core.Commands;
using PcSante.Core.Settings;
using PcSante.Core.Ui;
using PcSante.Core.Windows;
using PcSante.Ipc;
using PcSante.Ipc.Windows;

namespace PcSante.Overlay;

/// <summary>
/// Mini-affichage (M5) : barre compacte toujours au premier plan, transparente et « click-through »
/// (WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW), masquée en plein écran,
/// ou icône dans la zone de notification. Lecture seule : il ne fait que demander les mesures au service.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class OverlayWindow : IDisposable
{
    private const string ClassName = "PcSante.Overlay";
    private const nuint RefreshTimer = 1;
    private const nuint FullScreenTimer = 2;
    private const uint TrayCallback = Native.WmApp + 1;
    private const uint MetricsReady = Native.WmApp + 2;
    private const uint SettingsChanged = Native.WmApp + 3;
    private const int CloseCommand = 1;

    private static OverlayWindow? _instance;

    private readonly SettingsStore _store;
    private readonly ResourceManager _strings = new("PcSante.Overlay.Resources.OverlayStrings", typeof(OverlayWindow).Assembly);
    private readonly CancellationTokenSource _stop = new();
    private readonly FileSystemWatcher? _watcher;
    private CultureInfo _culture = CultureInfo.GetCultureInfo("fr-FR");
    private OverlaySettings _settings = new();
    private IntPtr _hwnd;
    private IntPtr _font;
    private IntPtr _background;
    private bool _trayAdded;
    private bool _hiddenForFullScreen;
    private volatile string _text = string.Empty;

    public OverlayWindow(SettingsStore store)
    {
        _store = store;
        var directory = Path.GetDirectoryName(store.FilePath);
        if (directory is not null && Directory.Exists(directory))
        {
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(store.FilePath)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
            _watcher.Changed += (_, _) => Native.PostMessageW(_hwnd, SettingsChanged, 0, 0);
            _watcher.Created += (_, _) => Native.PostMessageW(_hwnd, SettingsChanged, 0, 0);
            _watcher.Renamed += (_, _) => Native.PostMessageW(_hwnd, SettingsChanged, 0, 0);
        }
    }

    public unsafe int Run()
    {
        _instance = this;
        var instance = Native.GetModuleHandleW(null);
        var className = Marshal.StringToHGlobalUni(ClassName);
        try
        {
            var wc = new Native.WndClassEx
            {
                CbSize = (uint)sizeof(Native.WndClassEx),
                WndProc = &WndProc,
                Instance = instance,
                ClassName = className,
            };
            Native.RegisterClassExW(wc);
            _hwnd = Native.CreateWindowExW(
                Native.WsExLayered | Native.WsExTransparent | Native.WsExTopmost | Native.WsExToolWindow | Native.WsExNoActivate,
                ClassName, ProductInfo.Name, Native.WsPopup, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                return 1;
            }

            if (!ApplySettings())
            {
                return 0;
            }

            Native.SetTimer(_hwnd, FullScreenTimer, 1000, IntPtr.Zero);
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = true;
            }

            _ = Task.Run(() => PollAsync(_stop.Token));

            while (Native.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                Native.TranslateMessage(msg);
                Native.DispatchMessageW(msg);
            }

            return 0;
        }
        finally
        {
            Marshal.FreeHGlobal(className);
        }
    }

    /// <summary>Récupère les mesures du service toutes les 2 secondes (thread d'arrière-plan).</summary>
    private async Task PollAsync(CancellationToken ct)
    {
        await using var client = new PipeClient(ProductInfo.PipeName,
            new WindowsServerVerifier(Path.Combine(AppContext.BaseDirectory, "PcSante.Service.exe")), TimeSpan.FromSeconds(2));
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        do
        {
            var result = await client.SendAsync(CommandId.GetLiveMetrics, cancellationToken: ct).ConfigureAwait(false);
            _text = result.IsSuccess && result.GetData<LiveMetrics>() is { } metrics
                ? OverlayFormatter.Format(metrics, _settings.Indicators, Labels(), _culture)
                : T("ServiceDown");
            Native.PostMessageW(_hwnd, MetricsReady, 0, 0);
        }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    private OverlayLabels Labels() =>
        new(T("Cpu"), T("Memory"), T("Disk"), T("Network"), T("Temperature"), T("Battery"), T("NotAvailable"));

    private string T(string key) => _strings.GetString(key, _culture) ?? key;

    /// <summary>Relit les réglages ; renvoie faux si le mini-affichage a été désactivé.</summary>
    private bool ApplySettings()
    {
        var user = _store.Load();
        _settings = user.Overlay.Normalized();
        _culture = user.Language switch
        {
            "en" => CultureInfo.GetCultureInfo("en-US"),
            "ar" => CultureInfo.GetCultureInfo("ar-MA"),
            _ => CultureInfo.GetCultureInfo("fr-FR"),
        };
        if (!_settings.Enabled)
        {
            Native.DestroyWindow(_hwnd);
            return false;
        }

        if (_font != IntPtr.Zero)
        {
            Native.DeleteObject(_font);
        }

        var dpi = Math.Max(96u, Native.GetDpiForWindow(_hwnd));
        _font = Native.CreateFontW(-(int)(_settings.FontSize * dpi / 96), 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        if (_background == IntPtr.Zero)
        {
            _background = Native.CreateSolidBrush(0x00202020);
        }

        Native.SetLayeredWindowAttributes(_hwnd, 0, (byte)(_settings.OpacityPercent * 255 / 100), Native.LwaAlpha);
        if (_settings.TrayIconOnly)
        {
            Native.ShowWindow(_hwnd, Native.SwHide);
            UpdateTray();
        }
        else
        {
            RemoveTray();
            Layout();
        }

        return true;
    }

    private void Layout()
    {
        if (_settings.TrayIconOnly || _hiddenForFullScreen)
        {
            return;
        }

        var text = string.IsNullOrEmpty(_text) ? ProductInfo.Name : _text;
        var hdc = Native.GetDC(_hwnd);
        var old = Native.SelectObject(hdc, _font);
        Native.GetTextExtentPoint32W(hdc, text, text.Length, out var size);
        Native.SelectObject(hdc, old);
        Native.ReleaseDC(_hwnd, hdc);

        var width = size.Cx + 24;
        var height = size.Cy + 10;
        Native.SystemParametersInfoRect(Native.SpiGetWorkArea, 0, out var area, 0);
        const int margin = 8;
        var x = _settings.Position is OverlayPosition.TopLeft or OverlayPosition.BottomLeft ? area.Left + margin : area.Right - width - margin;
        var y = _settings.Position is OverlayPosition.TopLeft or OverlayPosition.TopRight ? area.Top + margin : area.Bottom - height - margin;
        Native.SetWindowPos(_hwnd, Native.HwndTopmost, x, y, width, height, Native.SwpNoActivate);
        Native.ShowWindow(_hwnd, Native.SwShowNoActivate);
        Native.InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    private void Paint()
    {
        var hdc = Native.BeginPaint(_hwnd, out var ps);
        var rect = ps.Paint;
        Native.FillRect(hdc, rect, _background);
        Native.SetBkMode(hdc, Native.Transparent);
        Native.SetTextColor(hdc, 0x00FFFFFF);
        var old = Native.SelectObject(hdc, _font);
        var flags = Native.DtSingleLine | Native.DtVCenter | Native.DtCenter | Native.DtNoPrefix
            | (_culture.TextInfo.IsRightToLeft ? Native.DtRtlReading : 0);
        var text = _text;
        Native.DrawTextW(hdc, text, text.Length, ref rect, flags);
        Native.SelectObject(hdc, old);
        Native.EndPaint(_hwnd, ps);
    }

    /// <summary>Masquage automatique pendant un jeu, une vidéo ou une présentation en plein écran.</summary>
    private void CheckFullScreen()
    {
        if (!_settings.HideInFullScreen || _settings.TrayIconOnly)
        {
            return;
        }

        var busy = Native.SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4;
        if (busy != _hiddenForFullScreen)
        {
            _hiddenForFullScreen = busy;
            if (busy)
            {
                Native.ShowWindow(_hwnd, Native.SwHide);
            }
            else
            {
                Layout();
            }
        }
    }

    private unsafe void UpdateTray()
    {
        var data = new Native.NotifyIconData
        {
            CbSize = (uint)sizeof(Native.NotifyIconData),
            Hwnd = _hwnd,
            Id = 1,
            Flags = Native.NifMessage | Native.NifIcon | Native.NifTip | Native.NifShowTip,
            CallbackMessage = TrayCallback,
            Icon = Native.LoadIconW(IntPtr.Zero, 32512),
        };
        var tip = (ProductInfo.Name + Environment.NewLine + _text).Replace("   ", Environment.NewLine, StringComparison.Ordinal);
        var length = Math.Min(tip.Length, 127);
        for (var i = 0; i < length; i++)
        {
            data.Tip[i] = tip[i];
        }

        data.Tip[length] = '\0';
        Native.Shell_NotifyIconW(_trayAdded ? Native.NimModify : Native.NimAdd, data);
        _trayAdded = true;
    }

    private unsafe void RemoveTray()
    {
        if (!_trayAdded)
        {
            return;
        }

        var data = new Native.NotifyIconData { CbSize = (uint)sizeof(Native.NotifyIconData), Hwnd = _hwnd, Id = 1 };
        Native.Shell_NotifyIconW(Native.NimDelete, data);
        _trayAdded = false;
    }

    private void ShowTrayMenu()
    {
        var menu = Native.CreatePopupMenu();
        Native.AppendMenuW(menu, Native.MfString, CloseCommand, T("Close"));
        Native.GetCursorPos(out var point);
        Native.SetForegroundWindow(_hwnd);
        var command = Native.TrackPopupMenu(menu, Native.TpmReturnCmd | Native.TpmRightButton, point.X, point.Y, 0, _hwnd, IntPtr.Zero);
        Native.DestroyMenu(menu);
        if (command == CloseCommand)
        {
            // Fermer depuis l'icône désactive le mini-affichage dans les réglages de l'utilisateur.
            var user = _store.Load();
            _store.Save(user with { Overlay = user.Overlay with { Enabled = false } });
            Native.DestroyWindow(_hwnd);
        }
    }

    [UnmanagedCallersOnly]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var self = _instance;
        if (self is null || (self._hwnd != IntPtr.Zero && hwnd != self._hwnd))
        {
            return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        switch (msg)
        {
            case Native.WmPaint:
                self.Paint();
                return 0;
            case MetricsReady:
                if (self._settings.TrayIconOnly)
                {
                    self.UpdateTray();
                }
                else
                {
                    self.Layout();
                }

                return 0;
            case SettingsChanged:
                self.ApplySettings();
                return 0;
            case Native.WmTimer when wParam == (IntPtr)FullScreenTimer:
                self.CheckFullScreen();
                return 0;
            case TrayCallback when (uint)lParam is Native.WmRButtonUp or Native.WmContextMenu:
                self.ShowTrayMenu();
                return 0;
            case Native.WmDpiChanged:
                self.ApplySettings();
                return 0;
            case Native.WmDestroy:
                self.RemoveTray();
                self._stop.Cancel();
                Native.PostQuitMessage(0);
                return 0;
            default:
                return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _watcher?.Dispose();
        if (_font != IntPtr.Zero)
        {
            Native.DeleteObject(_font);
        }

        if (_background != IntPtr.Zero)
        {
            Native.DeleteObject(_background);
        }
    }
}
