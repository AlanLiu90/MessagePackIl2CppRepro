using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using MessagePack;
using Repro.Messages;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace Repro
{
    /// <summary>
    /// Measures what the first <see cref="MessagePackSerializer.Deserialize{T}(ReadOnlyMemory{byte}, MessagePackSerializerOptions, System.Threading.CancellationToken)"/>
    /// of a tiny source-generated type costs in an IL2CPP player, and how much of that is the generated resolver's first
    /// <see cref="GeneratedMessagePackResolver.GetFormatter{T}"/>. The result is shown on screen and written to the player log.
    /// Pass -quitAfterProbe on the command line to exit when done.
    /// Pass -allMessages to then deserialize every catalog class and struct (AllMessages.g.cs) and report what that costs.
    /// </summary>
    public class FirstDeserializeProbe : MonoBehaviour
    {
#if ENABLE_IL2CPP && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern ulong il2cpp_stats_get_value(int stat);
#else
        private static ulong il2cpp_stats_get_value(int stat) => 0;
#endif

        // Il2CppStat in libil2cpp/il2cpp-api-types.h
        private const int InitializedClassCount = 1, MethodCount = 2, GenericClassCount = 5, InflatedMethodCount = 6;

        private static readonly byte[] IntPayload = { 0x01 };        // 1
        private static readonly byte[] PingPayload = { 0x91, 0x01 }; // [1]

        private string report = "Measuring...";

        private struct Snapshot
        {
            public long Native, Managed;
            public ulong Classes, Methods, GenericClasses, InflatedMethods;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create() => new GameObject(nameof(FirstDeserializeProbe)).AddComponent<FirstDeserializeProbe>();

        private static Snapshot Take() => new Snapshot
        {
            // IL2CPP allocates its runtime metadata through Unity's memory manager, so this includes it.
            Native = Profiler.GetTotalAllocatedMemoryLong(),
            Managed = GC.GetTotalMemory(false),
            Classes = il2cpp_stats_get_value(InitializedClassCount),
            Methods = il2cpp_stats_get_value(MethodCount),
            GenericClasses = il2cpp_stats_get_value(GenericClassCount),
            InflatedMethods = il2cpp_stats_get_value(InflatedMethodCount),
        };

        private IEnumerator Start()
        {
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            bool allMessages = Array.IndexOf(Environment.GetCommandLineArgs(), "-allMessages") >= 0;
            if (allMessages)
            {
                // Build the table of payloads and delegates now, so its own cost stays out of the measurements.
                _ = AllMessages.Entries.Length;
            }

            GC.Collect();
            yield return null;

            // Nothing is logged until all measurements are taken.
            Snapshot s0 = Take();
            int warmUp = MessagePackSerializer.Deserialize<int>(IntPayload); // built-in type: warms up the standard resolvers
            Snapshot s1 = Take();
            object formatter = GeneratedMessagePackResolver.Instance.GetFormatter<Ping>(); // what Deserialize<Ping> reaches through StandardResolver
            Snapshot s2 = Take();
            Ping first = MessagePackSerializer.Deserialize<Ping>(PingPayload); // first deserialization of a generated type
            Snapshot s3 = Take();
            Ping second = MessagePackSerializer.Deserialize<Ping>(PingPayload);
            Snapshot s4 = Take();

            // -allMessages: the catalog's 600 classes, then its 60 structs, in parts, then all of them again.
            var parts = new (string Name, int Start, int End)[] { ("Msg0000", 0, 1), ("Msg0001-Msg0009", 1, 10), ("Msg0010-Msg0099", 10, 100), ("Msg0100-Msg0599", 100, 600), ("Pod000-Pod059", 600, AllMessages.Entries.Length), ("all 660 again", 0, AllMessages.Entries.Length) };
            var partSnapshots = new Snapshot[parts.Length + 1];
            var partMs = new double[parts.Length];
            int deserialized = 0;
            if (allMessages)
            {
                partSnapshots[0] = Take();
                for (int p = 0; p < parts.Length; p++)
                {
                    long start = Stopwatch.GetTimestamp();
                    for (int i = parts[p].Start; i < parts[p].End; i++)
                    {
                        if (AllMessages.Entries[i].Deserialize() != null)
                        {
                            deserialized++;
                        }
                    }

                    partMs[p] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                    partSnapshots[p + 1] = Take();
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"MessagePack first-deserialize probe: Unity {Application.unityVersion}, {Application.platform}, IL2CPP: {IsIl2Cpp}");
            sb.AppendLine($"Registrations in GeneratedMessagePackResolver: {CountRegistrations()}");
            sb.AppendLine("step | native memory KB | managed heap KB | IL2CPP classes initialized | methods | generic classes | inflated methods");
            Row(sb, "1. Deserialize<int>, warm-up", s0, s1);
            Row(sb, $"2. GeneratedMessagePackResolver.Instance.GetFormatter<Ping>() ({formatter.GetType().Name})", s1, s2);
            Row(sb, $"3. Deserialize<Ping> #1 (Id={first.Id})", s2, s3);
            Row(sb, $"4. Deserialize<Ping> #2 (Id={second.Id})", s3, s4);
            Row(sb, "First use of Ping in total (2 + 3)", s1, s3);
            if (allMessages)
            {
                for (int p = 0; p < parts.Length; p++)
                {
                    Row(sb, $"{p + 5}. Deserialize {parts[p].Name} ({parts[p].End - parts[p].Start} messages, {partMs[p]:F1} ms)", partSnapshots[p], partSnapshots[p + 1]);
                }

                Row(sb, $"All 660 messages, first time (5-9, {partMs.Take(parts.Length - 1).Sum():F1} ms)", partSnapshots[0], partSnapshots[parts.Length - 1]);
                sb.AppendLine($"Deserialized {deserialized} non-null messages of {AllMessages.Entries.Length * 2} calls");
            }

            report = sb.ToString();
            Debug.Log(report + $"(warm-up value {warmUp})");

            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-quitAfterProbe") >= 0)
            {
                yield return null;
                Application.Quit(0);
            }
        }

        private static bool IsIl2Cpp =>
#if ENABLE_IL2CPP
            true;
#else
            false;
#endif

        private static void Row(StringBuilder sb, string name, Snapshot a, Snapshot b)
        {
            sb.AppendLine($"{name} | {(b.Native - a.Native) / 1024.0:F1} | {(b.Managed - a.Managed) / 1024.0:F1} | " +
                          $"{(long)(b.Classes - a.Classes)} | {(long)(b.Methods - a.Methods)} | {(long)(b.GenericClasses - a.GenericClasses)} | " +
                          $"{(long)(b.InflatedMethods - a.InflatedMethods)}");
        }

        // Reads the size of the generated type lookup table, only to report how large the model is.
        private static string CountRegistrations()
        {
            try
            {
                Type helper = typeof(GeneratedMessagePackResolver).GetNestedType("GeneratedMessagePackResolverGetFormatterHelper", BindingFlags.NonPublic);
                var table = helper?.GetField("closedTypeLookup", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as ICollection;
                return table != null ? table.Count.ToString("N0") : "unknown";
            }
            catch (Exception e)
            {
                return "unknown (" + e.GetType().Name + ")";
            }
        }

        private void OnGUI()
        {
            GUI.skin.label.fontSize = Math.Max(14, Screen.height / 45);
            GUI.Label(new Rect(10, 10, Screen.width - 20, Screen.height - 20), report);
        }
    }
}
