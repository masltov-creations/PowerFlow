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
        return new SystemMetricsSnapshot(
            used,
            machine,
            coreSnapshot.AwakePhysicalCores,
            coreSnapshot.TotalPhysicalCores,
            coreSnapshot.LogicalProcessors,
            coreSnapshot.ProcessorQueueLength,
            coreSnapshot.AggregateProcessorPerformancePercent);
    }

    public void Dispose() => _parking.Dispose();

    public static double CalculateUsedPercent(ulong total, ulong available)
        => total == 0 ? 0 : Math.Clamp((total - Math.Min(total, available)) * 100d / total, 0, 100);

    public static int? NormalizeTotalProcessorCount(uint count)
        => count is > 0 and <= int.MaxValue ? (int)count : null;

    public static double? NormalizeOptionalCounter(double value, double minimum, double maximum)
    {
        if (!double.IsFinite(value) || !double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum < minimum) return null;
        return value >= minimum && value <= maximum ? value : null;
    }

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
        private readonly List<LogicalProcessorCounters> _counters = [];
        private IntPtr _query;
        private IntPtr _queueCounter;
        private IntPtr _totalPerformanceCounter;

        public CoreParkingReader()
        {
            try
            {
                var topology = ReadSingleGroupTopology();
                TotalPhysicalCores = topology.Count == 0 ? null : topology.Values.Distinct().Count();
                if (topology.Count == 0 || PdhOpenQuery(null, IntPtr.Zero, out _query) != 0)
                {
                    _query = IntPtr.Zero;
                    return;
                }

                _ = PdhAddEnglishCounter(_query, @"\System\Processor Queue Length", IntPtr.Zero, out _queueCounter);
                _ = PdhAddEnglishCounter(_query, @"\Processor Information(_Total)\% Processor Performance", IntPtr.Zero, out _totalPerformanceCounter);

                foreach (var pair in topology.OrderBy(pair => pair.Key))
                {
                    var parkingPath = $@"\Processor Information(0,{pair.Key})\Parking Status";
                    if (PdhAddEnglishCounter(_query, parkingPath, IntPtr.Zero, out var parkingCounter) != 0)
                    {
                        ResetQuery();
                        return;
                    }

                    var utilityPath = $@"\Processor Information(0,{pair.Key})\% Processor Utility";
                    var frequencyPath = $@"\Processor Information(0,{pair.Key})\Processor Frequency";
                    var maximumFrequencyPath = $@"\Processor Information(0,{pair.Key})\% of Maximum Frequency";
                    var performancePath = $@"\Processor Information(0,{pair.Key})\% Processor Performance";
                    var utilityCounter = IntPtr.Zero;
                    var frequencyCounter = IntPtr.Zero;
                    var maximumFrequencyCounter = IntPtr.Zero;
                    var performanceCounter = IntPtr.Zero;
                    _ = PdhAddEnglishCounter(_query, utilityPath, IntPtr.Zero, out utilityCounter);
                    _ = PdhAddEnglishCounter(_query, frequencyPath, IntPtr.Zero, out frequencyCounter);
                    _ = PdhAddEnglishCounter(_query, maximumFrequencyPath, IntPtr.Zero, out maximumFrequencyCounter);
                    _ = PdhAddEnglishCounter(_query, performancePath, IntPtr.Zero, out performanceCounter);
                    _counters.Add(new LogicalProcessorCounters(pair.Key, pair.Value, parkingCounter, utilityCounter, frequencyCounter, maximumFrequencyCounter, performanceCounter));
                }

                if (_counters.Count != topology.Count)
                {
                    ResetQuery();
                    return;
                }

                PdhCollectQueryData(_query);
            }
            catch
            {
                ResetQuery();
            }
        }

        public int? TotalPhysicalCores { get; }

        public CoreParkingSnapshot Read()
        {
            if (_query == IntPtr.Zero || _counters.Count == 0)
                return new CoreParkingSnapshot(null, TotalPhysicalCores, null, null, null);
            if (PdhCollectQueryData(_query) != 0)
                return new CoreParkingSnapshot(null, TotalPhysicalCores, null, null, null);

            var queueLength = ReadOptionalCounter(_queueCounter, 0d, 1024d);
            var aggregatePerformance = ReadOptionalCounter(_totalPerformanceCounter, 0d, 250d);
            var logical = new List<LogicalProcessorTelemetry>(_counters.Count);
            foreach (var entry in _counters)
            {
                if (PdhGetFormattedCounterValue(entry.ParkingCounter, PdhFmtDouble, out _, out var parkedValue) != 0 || parkedValue.CStatus != 0)
                    return new CoreParkingSnapshot(null, TotalPhysicalCores, null, null, null);

                var utilization = ReadOptionalCounter(entry.UtilityCounter, 0d, 250d);
                var frequency = ReadOptionalCounter(entry.FrequencyCounter, 0d, 10000d);
                var maximumFrequency = ReadOptionalCounter(entry.MaximumFrequencyCounter, 0d, 250d);
                var processorPerformance = ReadOptionalCounter(entry.ProcessorPerformanceCounter, 0d, 250d);

                logical.Add(new LogicalProcessorTelemetry(
                    entry.LogicalProcessor,
                    entry.PhysicalCore,
                    IsParked: parkedValue.DoubleValue >= 0.5d,
                    UtilizationPercent: utilization,
                    FrequencyMhz: frequency,
                    PercentOfMaximumFrequency: maximumFrequency,
                    ProcessorPerformancePercent: processorPerformance));
            }

            var awake = CountAwakePhysicalCores(logical.Select(state => new CoreParkingState(state.PhysicalCoreIndex, state.IsParked)));
            return new CoreParkingSnapshot(awake, TotalPhysicalCores, logical, queueLength, aggregatePerformance);
        }

        public void Dispose() => ResetQuery();

        private void ResetQuery()
        {
            if (_query != IntPtr.Zero) PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            _queueCounter = IntPtr.Zero;
            _totalPerformanceCounter = IntPtr.Zero;
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

        private double? ReadOptionalCounter(IntPtr counter, double minimum, double maximum)
        {
            if (counter == IntPtr.Zero) return null;
            if (PdhGetFormattedCounterValue(counter, PdhFmtDouble, out _, out var value) != 0 || value.CStatus != 0 || !double.IsFinite(value.DoubleValue))
                return null;
            return NormalizeOptionalCounter(value.DoubleValue, minimum, maximum);
        }

        private sealed record LogicalProcessorCounters(
            int LogicalProcessor,
            int PhysicalCore,
            IntPtr ParkingCounter,
            IntPtr UtilityCounter,
            IntPtr FrequencyCounter,
            IntPtr MaximumFrequencyCounter,
            IntPtr ProcessorPerformanceCounter);

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

    private sealed record CoreParkingSnapshot(
        int? AwakePhysicalCores,
        int? TotalPhysicalCores,
        IReadOnlyList<LogicalProcessorTelemetry>? LogicalProcessors,
        double? ProcessorQueueLength = null,
        double? AggregateProcessorPerformancePercent = null);
}
