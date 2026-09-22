using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PcSante.Core.Windows;
using PcSante.WindowsApi.Native;

namespace PcSante.WindowsApi;

/// <summary>Plans d'alimentation (powrprof.dll).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPowerApi : IPowerApi
{
    public Task<IReadOnlyList<PowerPlan>> ListPlansAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<PowerPlan>>(() =>
    {
        var active = ActivePlan();
        var plans = new List<PowerPlan>();
        var guidSize = (uint)Marshal.SizeOf<Guid>();
        var buffer = Marshal.AllocHGlobal((int)guidSize);
        try
        {
            for (uint index = 0; index < 64; index++)
            {
                var size = guidSize;
                if (NativeMethods.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, NativeMethods.AccessScheme, index, buffer, ref size) != 0)
                {
                    break;
                }

                var id = Marshal.PtrToStructure<Guid>(buffer);
                plans.Add(new PowerPlan(id, FriendlyName(id), id == active));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return plans;
    }, cancellationToken);

    public Task<Guid?> GetActivePlanAsync(CancellationToken cancellationToken) => Task.FromResult(ActivePlan());

    public Task<bool> SetActivePlanAsync(Guid planId, CancellationToken cancellationToken)
    {
        var id = planId;
        return Task.FromResult(NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref id) == 0);
    }

    private static Guid? ActivePlan()
    {
        if (NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(ptr);
        }
        finally
        {
            NativeMethods.LocalFree(ptr);
        }
    }

    private static string FriendlyName(Guid id)
    {
        uint size = 0;
        NativeMethods.PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
        if (size == 0)
        {
            return id.ToString();
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            return NativeMethods.PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
                ? Marshal.PtrToStringUni(buffer) ?? id.ToString()
                : id.ToString();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
