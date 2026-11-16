using System;
using System.Collections;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Nerdbank.MessagePack;
using PolyType;
using PolyType.Abstractions;
using Repro.WarmUp;
using UnityEngine;
using UnityEngine.Profiling;

namespace Repro
{
    /// <summary>
    /// Measures what the first Nerdbank.MessagePack deserialization of a tiny source-generated type costs in an IL2CPP player,
    /// and how much of that is the type shape lookup. The result is shown on screen and written to the player log.
    /// Pass -quitAfterProbe on the command line to exit when done.
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

        private static readonly byte[] Payload = { 0x91, 0x01 }; // [1]: one int under key 0

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

            GC.Collect();
            yield return null;

            // Nothing is logged until all measurements are taken.
            Snapshot s0 = Take();
            var serializer = new MessagePackSerializer();
            WarmUpMessage warmUp = serializer.Deserialize<WarmUpMessage>(Payload); // another assembly: warms up the serializer
            Snapshot s1 = Take();
            ITypeShape<Ping> shape = TypeShapeResolver.ResolveDynamicOrThrow<Ping>(); // the shape lookup Deserialize<Ping> starts with
            Snapshot s2 = Take();
            Ping first = serializer.Deserialize<Ping>(Payload); // first deserialization of a type from this assembly
            Snapshot s3 = Take();
            Ping second = serializer.Deserialize<Ping>(Payload);
            Snapshot s4 = Take();

            var sb = new StringBuilder();
            sb.AppendLine($"Nerdbank.MessagePack first-deserialize probe: Unity {Application.unityVersion}, {Application.platform}, IL2CPP: {IsIl2Cpp}");
            sb.AppendLine($"Types with [GenerateShape] in Assembly-CSharp: {CountShapedTypes()}");
            sb.AppendLine("step | native memory KB | managed heap KB | IL2CPP classes initialized | methods | generic classes | inflated methods");
            Row(sb, $"1. Deserialize<WarmUpMessage>, warm-up (Id={warmUp.Id})", s0, s1);
            Row(sb, $"2. TypeShapeResolver.ResolveDynamicOrThrow<Ping>() ({shape.Type.Name})", s1, s2);
            Row(sb, $"3. Deserialize<Ping> #1 (Id={first.Id})", s2, s3);
            Row(sb, $"4. Deserialize<Ping> #2 (Id={second.Id})", s3, s4);
            Row(sb, "First use of Ping in total (2 + 3)", s1, s3);
            report = sb.ToString();
            Debug.Log(report);

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

        // [GenerateShape] is not kept in metadata, so count the classes and structs it is applied to: the catalog and Ping.
        private static string CountShapedTypes()
        {
            try
            {
                int count = typeof(Ping).Assembly.GetTypes()
                    .Count(t => !t.IsNested && !t.IsEnum && (t.Namespace == "Repro.Messages" || t == typeof(Ping)));
                return count.ToString("N0");
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
