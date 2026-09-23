using PcSante.Core.Settings;
using PcSante.Overlay;

if (!OperatingSystem.IsWindows())
{
    return 2;
}

// Une seule instance par session utilisateur.
using var mutex = new Mutex(true, @"Local\PcSante.Overlay", out var first);
if (!first)
{
    return 0;
}

var store = new SettingsStore(SettingsStore.DefaultPath());

// Lancement automatique à l'ouverture de session : rien à faire si l'utilisateur ne l'a pas activé.
if (args.Contains("--autostart") && !store.Load().Overlay.Enabled)
{
    return 0;
}

using var window = new OverlayWindow(store);
return window.Run();
