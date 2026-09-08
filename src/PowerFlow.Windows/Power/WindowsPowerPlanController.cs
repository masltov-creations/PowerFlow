using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerFlow.Windows.Power;

public sealed class WindowsPowerPlanController : IPowerPlanController
{
    private readonly IPowerPlanNative _native;

    public WindowsPowerPlanController() : this(new PowerPlanNative()) { }
    public WindowsPowerPlanController(IPowerPlanNative native) => _native = native;

    public Task<IReadOnlyList<PowerPlanInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_native.EnumeratePlans());
    }

    public Task<PowerPlanInfo> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = _native.GetActiveScheme();
        var info = _native.EnumeratePlans().FirstOrDefault(p => p.Id == active)
            ?? new PowerPlanInfo(active, active.ToString());
        return Task.FromResult(info);
    }

    public Task<PowerPlanSwitchResult> ActivateAsync(Guid schemeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var error = _native.SetActiveScheme(schemeId);
        if (error != 0)
            return Task.FromResult(new PowerPlanSwitchResult(false, schemeId, TryGetActive(), $"PowerSetActiveScheme failed with Win32 code {error}."));

        var active = TryGetActive();
        if (active != schemeId)
            return Task.FromResult(new PowerPlanSwitchResult(false, schemeId, active, $"Power plan verification failed: requested {schemeId}, active {active?.ToString() ?? "unknown"}."));

        return Task.FromResult(new PowerPlanSwitchResult(true, schemeId, active, null));
    }

    private Guid? TryGetActive()
    {
        try { return _native.GetActiveScheme(); }
        catch { return null; }
    }

    private sealed class PowerPlanNative : IPowerPlanNative
    {
        private const uint AccessScheme = 16;
        private const uint ErrorMoreData = 234;
        private const uint ErrorNoMoreItems = 259;

        public IReadOnlyList<PowerPlanInfo> EnumeratePlans()
        {
            var list = new List<PowerPlanInfo>();
            for (uint index = 0; ; index++)
            {
                uint size = 0;
                var first = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, null, ref size);
                if (first == ErrorNoMoreItems) break;
                if (first != ErrorMoreData && first != 0) throw new Win32Exception((int)first);
                var buffer = new byte[size];
                var result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, buffer, ref size);
                if (result == ErrorNoMoreItems) break;
                if (result != 0) throw new Win32Exception((int)result);
                var id = new Guid(buffer.AsSpan(0, 16));
                list.Add(new PowerPlanInfo(id, ReadFriendlyName(id)));
            }
            return list;
        }

        public Guid GetActiveScheme()
        {
            var result = PowerGetActiveScheme(IntPtr.Zero, out var ptr);
            if (result != 0) throw new Win32Exception((int)result);
            try { return Marshal.PtrToStructure<Guid>(ptr); }
            finally { LocalFree(ptr); }
        }

        public int SetActiveScheme(Guid id)
        {
            var copy = id;
            return unchecked((int)PowerSetActiveScheme(IntPtr.Zero, ref copy));
        }

        private static string ReadFriendlyName(Guid id)
        {
            uint size = 0;
            var scheme = id;
            var first = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);
            if (first != ErrorMoreData && first != 0) return id.ToString();
            var buffer = new byte[size];
            var result = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size);
            if (result != 0) return id.ToString();
            return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        }

        [DllImport("powrprof.dll")]
        private static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid, uint accessFlags, uint index, byte[]? buffer, ref uint bufferSize);

        [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
        private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupGuid, IntPtr powerSettingGuid, byte[]? buffer, ref uint bufferSize);

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}
