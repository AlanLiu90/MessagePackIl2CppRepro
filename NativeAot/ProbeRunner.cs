using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Runs the probe's steps and prints, per step: the process's physical footprint (what macOS and iOS count against the
/// app), the managed heap, the managed bytes allocated, and the time. Nothing is printed until every step has run.
/// At the end it also reports what is still held after a full GC, and the peak footprint.
/// </summary>
internal static class ProbeRunner
{
    // struct rusage_info_v4 (sys/resource.h): a UUID, then 64-bit fields. ri_phys_footprint is field 7 and
    // ri_lifetime_max_phys_footprint field 28.
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct RUsageInfoV4
    {
        public fixed byte Uuid[16];
        public fixed ulong Fields[36];
    }

    private const int RusageInfoV4 = 4;

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int proc_pid_rusage(int pid, int flavor, out RUsageInfoV4 info);

    private static unsafe (long Footprint, long Peak) Footprint()
    {
        proc_pid_rusage(Environment.ProcessId, RusageInfoV4, out RUsageInfoV4 usage);
        return ((long)usage.Fields[7], (long)usage.Fields[28]);
    }

    // PublishAot turns off RuntimeFeature.IsDynamicCodeSupported (and with it IsDynamicCodeCompiled) for JIT runs too, so
    // tell the runtimes apart by Assembly.Location, which is always empty under NativeAOT.
    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty Location is how NativeAOT is detected.")]
    private static string RuntimeName => string.IsNullOrEmpty(typeof(object).Assembly.Location) ? "NativeAOT" : "JIT";

    private struct Snapshot
    {
        public long Footprint, Heap, Allocated;
    }

    private static Snapshot Take()
    {
        return new Snapshot
        {
            Footprint = Footprint().Footprint,
            Heap = GC.GetTotalMemory(false),
            Allocated = GC.GetTotalAllocatedBytes(precise: true),
        };
    }

    /// <param name="totals">Extra rows: a name and the first and last step (0-based) they sum.</param>
    public static string Run(string title, IReadOnlyList<(string Name, Action Run)> steps, params (string Name, int First, int Last)[] totals)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long liveStart = GC.GetTotalMemory(forceFullCollection: true);
        var snaps = new Snapshot[steps.Count + 1];
        var ms = new double[steps.Count];
        snaps[0] = Take();
        for (int i = 0; i < steps.Count; i++)
        {
            long start = Stopwatch.GetTimestamp();
            steps[i].Run();
            ms[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            snaps[i + 1] = Take();
        }

        long peak = Footprint().Peak;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long liveEnd = GC.GetTotalMemory(forceFullCollection: true);
        long footprintEnd = Footprint().Footprint;

        var sb = new StringBuilder();
        sb.AppendLine($"{title}: {RuntimeInformation.FrameworkDescription}, {RuntimeName}, {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine("step | footprint KB | managed heap KB | managed allocated KB | ms");
        void Row(string name, Snapshot a, Snapshot b, double time) =>
            sb.AppendLine($"{name} | {(b.Footprint - a.Footprint) / 1024.0:F1} | {(b.Heap - a.Heap) / 1024.0:F1} | {(b.Allocated - a.Allocated) / 1024.0:F1} | {time:F2}");
        for (int i = 0; i < steps.Count; i++)
        {
            Row(steps[i].Name, snaps[i], snaps[i + 1], ms[i]);
        }

        foreach (var (name, first, last) in totals)
        {
            double sum = 0;
            for (int i = first; i <= last; i++)
            {
                sum += ms[i];
            }

            Row(name, snaps[first], snaps[last + 1], sum);
        }

        sb.AppendLine($"After all steps and a full GC: managed heap {(liveEnd - liveStart) / 1024.0:F1} KB above the start, footprint {(footprintEnd - snaps[0].Footprint) / 1024.0:F1} KB above the start");
        sb.AppendLine($"Process footprint: {snaps[0].Footprint / 1024.0:F0} KB at the start, {footprintEnd / 1024.0:F0} KB at the end, peak {peak / 1024.0:F0} KB");
        return sb.ToString();
    }
}
