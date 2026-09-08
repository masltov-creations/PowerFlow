using System.Runtime.InteropServices;

namespace PowerFlow.Windows.Activity;

public sealed class DashboardTelemetrySource : IDashboardTelemetrySource
{
    private readonly EnergyMeterReader _energy = new();

    public DashboardTelemetry Read(DateTimeOffset at) => new(_energy.TryReadWatts(), TryReadAverageMhz(), at);

    public void Dispose() => _energy.Dispose();

    private static double? TryReadAverageMhz()
    {
        try
        {
            var count = GetActiveProcessorCount(0xffff);
            if (count == 0) return null;
            var info = new ProcessorPowerInformation[count];
            var length = checked((uint)(Marshal.SizeOf<ProcessorPowerInformation>() * info.Length));
            var status = CallNtPowerInformation(11, IntPtr.Zero, 0, info, length);
            if (status != 0) return null;
            return info.Average(x => (double)x.CurrentMhz);
        }
        catch { return null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPowerInformation
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(int informationLevel, IntPtr inputBuffer, uint inputBufferLength, [Out] ProcessorPowerInformation[] outputBuffer, uint outputBufferLength);

    [DllImport("kernel32.dll")]
    private static extern uint GetActiveProcessorCount(ushort groupNumber);

    private sealed class EnergyMeterReader : IDisposable
    {
        private const uint PdhFmtDouble = 0x00000200;
        private IntPtr _query;
        private IntPtr _counter;

        public EnergyMeterReader()
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) { _query = IntPtr.Zero; return; }
            if (PdhAddEnglishCounter(_query, @"\Energy Meter(rapl_package0_pkg)\Power", IntPtr.Zero, out _counter) != 0)
            {
                PdhCloseQuery(_query); _query = IntPtr.Zero; _counter = IntPtr.Zero; return;
            }
            PdhCollectQueryData(_query);
        }

        public double? TryReadWatts()
        {
            if (_query == IntPtr.Zero || _counter == IntPtr.Zero) return null;
            if (PdhCollectQueryData(_query) != 0) return null;
            if (PdhGetFormattedCounterValue(_counter, PdhFmtDouble, out _, out var value) != 0 || value.CStatus != 0) return null;
            var raw = value.DoubleValue;
            return raw > 1000 ? raw / 1000d : raw;
        }

        public void Dispose()
        {
            if (_query != IntPtr.Zero) PdhCloseQuery(_query);
            _query = IntPtr.Zero; _counter = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PdhFmtCounterValue
        {
            [FieldOffset(0)] public uint CStatus;
            [FieldOffset(8)] public double DoubleValue;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhAddEnglishCounter(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);
        [DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")]
        private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFmtCounterValue value);
        [DllImport("pdh.dll")]
        private static extern uint PdhCloseQuery(IntPtr query);
    }
}
