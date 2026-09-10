using System.Runtime.InteropServices;

namespace PowerFlow.Windows.Activity;

public sealed record CoreParkingState(int PhysicalCoreIndex, bool IsParked);

public sealed class WindowsSystemMetricsProvider : ISystemMetricsProvider, IDisposable
{
    private readonly CoreParkingReader _parking = new();

    public SystemMetricsSnapshot Read()
    {
        double? used = null;
        try
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status) && status.TotalPhysical > 0)
                used = CalculateUsedPercent(status.TotalPhysical, status.AvailablePhysical);
        }
        catch
        {
            used = null;
        }

        string? machine = null;
        try { machine = Environment.MachineName; } catch { }

        var coreSnapshot = _parking.Read();
        return new SystemMetricsSnapshot(used, machine, coreSnapshot.AwakePhysicalCores, coreSnapshot.TotalPhysicalCores);
    }

    public void Dispose() => _parking.Dispose();

    public static double CalculateUsedPercent(ulong total, ulong available)
        => total == 0 ? 0 : Math.Clamp((total - Math.Min(total, available)) * 100d / total, 0, 100);

    public static int? NormalizeTotalProcessorCount(uint count)
        => count is > 0 and <= int.MaxValue ? (int)count : null;

    public static int CountAwakePhysicalCores(IEnumerable<CoreParkingState> states)
        => states
            .GroupBy(state => state.PhysicalCoreIndex)
            .Count(core => core.Any(thread => !thread.IsParked));

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private sealed class CoreParkingReader : IDisposable
    {
        private const uint PdhFmtDouble = 0x00000200;
        private const int RelationProcessorCore = 0;
        private readonly Dictionary<int, int> _logicalToPhysical = new();
        private readonly List<(int PhysicalCore, IntPtr Counter)> _counters = new();
        private IntPtr _query;

        public CoreParkingReader()
        {
            try
            {
                var topology = ReadSingleGroupTopology();
                foreach (var pair in topology) _logicalToPhysical[pair.Key] = pair.Value;
                TotalPhysicalCores = topology.Count == 0 ? null : topology.Values.Distinct().Count();
                if (topology.Count == 0 || PdhOpenQuery(null, IntPtr.Zero, out _query) != 0)
                {
                    _query = IntPtr.Zero;
                    return;
                }

                foreach (var pair in topology.OrderBy(pair => pair.Key))
                {
                    var path = $@"\Processor Information(0,{pair.Key})\Parking Status";
                    if (PdhAddEnglishCounter(_query, path, IntPtr.Zero, out var counter) == 0)
                        _counters.Add((pair.Value, counter));
                }

                if (_counters.Count != topology.Count)
                {
                    PdhCloseQuery(_query);
                    _query = IntPtr.Zero;
                    _counters.Clear();
                    return;
                }

                PdhCollectQueryData(_query);
            }
            catch
            {
                if (_query != IntPtr.Zero) PdhCloseQuery(_query);
                _query = IntPtr.Zero;
                _counters.Clear();
            }
        }

        public int? TotalPhysicalCores { get; }

        public CoreParkingSnapshot Read()
        {
            if (_query == IntPtr.Zero || _counters.Count == 0)
                return new CoreParkingSnapshot(null, TotalPhysicalCores);
            if (PdhCollectQueryData(_query) != 0)
                return new CoreParkingSnapshot(null, TotalPhysicalCores);

            var states = new List<CoreParkingState>(_counters.Count);
            foreach (var entry in _counters)
            {
                if (PdhGetFormattedCounterValue(entry.Counter, PdhFmtDouble, out _, out var value) != 0 || value.CStatus != 0)
                    return new CoreParkingSnapshot(null, TotalPhysicalCores);
                states.Add(new CoreParkingState(entry.PhysicalCore, IsParked: value.DoubleValue >= 0.5d));
            }

            return new CoreParkingSnapshot(CountAwakePhysicalCores(states), TotalPhysicalCores);
        }

        public void Dispose()
        {
            if (_query != IntPtr.Zero) PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            _counters.Clear();
        }

        private static Dictionary<int, int> ReadSingleGroupTopology()
        {
            uint length = 0;
            _ = GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length);
            if (length == 0) return new Dictionary<int, int>();

            var buffer = Marshal.AllocHGlobal(checked((int)length));
            try
            {
                if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length))
                    return new Dictionary<int, int>();

                var result = new Dictionary<int, int>();
                var offset = 0;
                var physicalCore = 0;
                while (offset < length)
                {
                    var item = IntPtr.Add(buffer, offset);
                    var relationship = Marshal.ReadInt32(item, 0);
                    var size = Marshal.ReadInt32(item, 4);
                    if (size <= 0) break;
                    if (relationship == RelationProcessorCore)
                    {
                        var groupCount = Marshal.ReadInt16(item, 30);
                        if (groupCount != 1) return new Dictionary<int, int>();
                        var affinityOffset = 32;
                        ulong mask = IntPtr.Size == 8
                            ? unchecked((ulong)Marshal.ReadInt64(item, affinityOffset))
                            : unchecked((uint)Marshal.ReadInt32(item, affinityOffset));
                        var group = Marshal.ReadInt16(item, affinityOffset + IntPtr.Size);
                        if (group != 0) return new Dictionary<int, int>();
                        for (var bit = 0; bit < 64; bit++)
                            if ((mask & (1UL << bit)) != 0)
                                result[bit] = physicalCore;
                        physicalCore++;
                    }
                    offset += size;
                }
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PdhFmtCounterValue
        {
            public uint CStatus;
            public double DoubleValue;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLogicalProcessorInformationEx(int relationshipType, IntPtr buffer, ref uint returnedLength);

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

    private sealed record CoreParkingSnapshot(int? AwakePhysicalCores, int? TotalPhysicalCores);
}
