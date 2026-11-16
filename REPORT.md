# First-use memory and time: MessagePack-CSharp vs Nerdbank.MessagePack on IL2CPP

This report compares what the first deserializations cost in a Unity IL2CPP player with
[MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp) 3.1.12 and
[Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) 1.4.104, on the same message catalog and the same
payload bytes.

**Summary**

- **One small message:** a first `Deserialize<Ping>` (one `int` field) costs 11,077 KB of native memory with
  MessagePack-CSharp and 3,798 KB with Nerdbank.MessagePack. MessagePack-CSharp's first lookup pays for every type its
  generated resolver knows.
- **Every message:** deserializing all 660 catalog types once costs 28,498 KB and 74 ms with MessagePack-CSharp, and
  369,876 KB and 1,262 ms with Nerdbank.MessagePack. With the warm-up and `Ping` steps, that is about 41 MB against about
  375 MB.
- **Why they differ:** MessagePack-CSharp pays a large fixed cost on its first lookup, then only for the types a message
  actually reads. Nerdbank.MessagePack pays, on the first use of a type, for every type that type can reach.
- **After first use** both cost no more memory, and all 660 messages deserialize again in 1.4 ms and 2.0 ms.

| At a glance | MessagePack-CSharp 3.1.12 | Nerdbank.MessagePack 1.4.104 |
|---|---:|---:|
| First use of a one-field type (`Ping`) | 11,077 KB | **3,798 KB** |
| All 660 catalog types, first time | **28,498 KB, 74 ms** | 369,876 KB, 1,262 ms |
| IL2CPP classes initialized by those 660 | **18,802** | 97,521 |
| Whole run: warm-up, `Ping` and all 660 | **40,935 KB** | 375,084 KB |
| All 660 again | 0 KB, **1.4 ms** | 0 KB, 2.0 ms |
| What a first use pays for | every registration on the first lookup, then the members each message reads | everything the type can reach |

## Setup

| | |
|---|---|
| Unity | 2022.3.57f1, IL2CPP with Unity's default code generation and stripping settings |
| Player | macOS standalone, Apple M6, macOS 27.2 |
| MessagePack-CSharp | 3.1.12 from NuGet through NuGetForUnity 4.5.0, plus the `MessagePack.Unity` git package at `#v3.1.12`; types marked `[MessagePackObject]`, members `[Key(n)]` |
| Nerdbank.MessagePack | 1.4.104 and PolyType 1.4.1 from NuGet through NuGetForUnity 4.5.0; types marked `[GenerateShape]`, members Nerdbank's `[Key(n)]` |

**Catalog** ([`Tools/generate_messages.py`](Tools/generate_messages.py), the same seed on every branch): 600 classes, 60
structs and 80 enums. Class members use primitives, enums, nullables, structs, other classes, `List<T>`, `T[]`,
`Dictionary<K, V>` and `HashSet<int>`. The classes form 6 layers of 100, and a class only references classes in the next
layer. That gives 2,139 registrations in MessagePack-CSharp's generated resolver; PolyType generates shapes for 2,152 types.

**Payloads:** `Ping` is `[1]`. Each catalog type's payload
([`Tools/generate_all_messages.py`](https://github.com/AlanLiu90/MessagePackIl2CppRepro/blob/messagepack-all-messages/Tools/generate_all_messages.py))
is the array a `[Key]` type is written as: primitives, enums and structs filled in; references, collections and nullables
nil. Both libraries read the same bytes.

**Measurements:** the probe snapshots these before and after each step, and logs nothing until all steps are done.

- **Native memory:** `Profiler.GetTotalAllocatedMemoryLong()`. IL2CPP allocates its runtime metadata (classes, method
  tables, generic instances) through Unity's memory manager, so this includes it.
- **Managed heap:** `GC.GetTotalMemory(false)`.
- **IL2CPP counters:** classes initialized, methods, generic classes and inflated methods, from `il2cpp_stats_get_value`.
- **Time:** `Stopwatch` around each group of all-messages steps. The one-type steps are not timed.

Every number comes from players built from clean clones of the four branches (see [Verification](#verification)), each
run three times. Native memory and the IL2CPP counters were the same in every run, except Nerdbank.MessagePack's steps 6
to 8, which varied by up to 128 KB; the managed heap varied by up to about 110 KB, and times by up to 4% (more for steps
under a millisecond). The tables give the first run's memory and the median time.

## Results

### One type: `Ping`

Branches [`main`](https://github.com/AlanLiu90/MessagePackIl2CppRepro/tree/main) and
[`nerdbank-messagepack`](https://github.com/AlanLiu90/MessagePackIl2CppRepro/tree/nerdbank-messagepack).

| Step | MessagePack-CSharp: native | classes | inflated methods | Nerdbank.MessagePack: native | classes | inflated methods |
|---|---:|---:|---:|---:|---:|---:|
| 1. Warm-up (`Deserialize<int>` / a type from another assembly) | 1,364 KB | 398 | 2,765 | 1,409 KB | 431 | 3,765 |
| 2. Lookup (`GeneratedMessagePackResolver.GetFormatter<Ping>()` / `TypeShapeResolver.ResolveDynamicOrThrow<Ping>()`) | 11,013 KB | 5,731 | 25,936 | 154 KB | 9 | 45 |
| 3. `Deserialize<Ping>` #1 | 64 KB | 24 | 93 | 3,644 KB | 44 | 547 |
| 4. `Deserialize<Ping>` #2 | 0 | 0 | 0 | 0 | 0 | 0 |
| **First use of `Ping` (2 + 3)** | **11,077 KB** | 5,755 | 26,029 | **3,798 KB** | 53 | 592 |

The warm-up differs because MessagePack-CSharp can warm up with a built-in type, while Nerdbank.MessagePack needs a
type with a shape; it is a separate step in both, so it does not change the first-use rows.

### Every message: `-allMessages`

Branches [`messagepack-all-messages`](https://github.com/AlanLiu90/MessagePackIl2CppRepro/tree/messagepack-all-messages)
and [`nerdbank-all-messages`](https://github.com/AlanLiu90/MessagePackIl2CppRepro/tree/nerdbank-all-messages), after
steps 1 to 4.

| Step | MessagePack-CSharp: native | managed | classes | time | Nerdbank.MessagePack: native | managed | classes | time |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 5. `Msg0000` | 449 KB | 60 KB | 179 | 0.7 ms | 140,040 KB | 5,840 KB | 36,043 | 381 ms |
| 6. `Msg0001`–`Msg0009` | 977 KB | 104 KB | 715 | 3.3 ms | 107,724 KB | 4,368 KB | 27,399 | 345 ms |
| 7. `Msg0010`–`Msg0099` | 6,261 KB | 348 KB | 4,650 | 13.2 ms | 113,227 KB | 5,688 KB | 29,657 | 492 ms |
| 8. `Msg0100`–`Msg0599` | 20,812 KB | 1,352 KB | 13,251 | 56.9 ms | 8,629 KB | 8 KB | 4,122 | 37 ms |
| 9. `Pod000`–`Pod059` | 0 | 8 KB | 7 | 0.1 ms | 256 KB | 68 KB | 300 | 0.8 ms |
| **All 660, first time (5–9)** | **28,498 KB** | 1,872 KB | 18,802 | **74 ms** | **369,876 KB** | 15,972 KB | 97,521 | **1,262 ms** |
| 10. All 660 again | 0 | 88 KB | 0 | 1.4 ms | 0 | 76 KB | 0 | 2.0 ms |

Nerdbank.MessagePack's total for steps 5 to 9 measured 369,996 KB in one of the three runs.

### In total

| Native memory, on the all-messages branches | MessagePack-CSharp | Nerdbank.MessagePack |
|---|---:|---:|
| Warm-up | 1,359 KB | 1,409 KB |
| First use of `Ping` | 11,077 KB | 3,798 KB |
| All 660 messages | 28,498 KB | 369,876 KB |
| **Whole run** | **40,935 KB** | **375,084 KB** |

## Why

### MessagePack-CSharp: everything on the first lookup, then only what is read

The source-generated resolver finds a type's formatter through a `Dictionary<Type, int>` filled with a `typeof()` of every
registration, then a single method that can create every formatter. IL2CPP initializes all the metadata a method uses
the first time it runs, so the first lookup creates the classes of all 2,139 registrations and their formatters: 5,731
classes and 11 MB, whatever type is asked for. [README.md](README.md) on `main` goes through this in detail.

After that, a formatter asks the resolver for the formatters of the members it reads, and each new type costs only its
own formatter. Deserializing one message touches its member types and nothing deeper, so the cost grows with the
messages deserialized: 449 KB for `Msg0000`, 28,498 KB for all 660.

### Nerdbank.MessagePack: everything a type can reach, on its first use

Building a converter builds the converters of every property, element, key and value type it can reach, at once;
delayed converters only break cycles. `Msg0000` is in the top layer and reaches much of the catalog, so its first
deserialization costs 140 MB. The first 100 messages, the whole top layer, reach nearly every type, which leaves 8.6 MB
for the other 500.

Each reachable property becomes several generic instantiations: PolyType's property shape, getter and setter delegates
over `<TDeclaringType, TPropertyType>`, and Nerdbank.MessagePack's closures over the same pair. IL2CPP gives every
instantiation its own class and, when it is initialized, a copy of every one of its methods (`vm/GenericClass.cpp`),
whether or not the code itself is shared. In a separate census of the same catalog on Nerdbank.MessagePack's main branch
(1.5 alpha), that came to about 13 classes and 14 KB per property, with each reachable class costing about 0.6 MB in all.

The 3.6 MB of `Deserialize<Ping>`, a one-field type, is mostly a cost that grows with the catalog. PolyType generates one
provider class for the assembly (19,303 methods in this build) and one class for its lambdas (25,902 methods). The first
shape from it initializes both, which creates a method record for every one of their methods. Neither class is generic,
so this does not show in the method counter: libil2cpp only counts the methods of generic instances and arrays
(`vm/GenericClass.cpp`, `metadata/ArrayMetadata.cpp`). That is why, on `nerdbank-messagepack`, the counters for `Ping`
stay the same with a catalog one-tenth the size while the memory falls from 3,798 KB to 642 KB.

### Time

The first pass takes 74 ms against 1,262 ms. The time falls where the memory does: MessagePack-CSharp spends most of it on
the 500 messages of step 8 (57 ms), Nerdbank.MessagePack over 96% of it on the first 100 messages. This probe does not show
where inside each library the time goes. Once every type has been used, both deserialize all 660 messages in about 2 ms
(1.4 ms and 2.0 ms) and allocate no more native memory.

## Verification

On 2026-11-17 each branch was cloned fresh from this repository and built in batch mode with
`Repro.Editor.BuildPlayer.Build`, the method behind **Repro > Build IL2CPP Player**. All four built without errors and left
their clones unchanged. Each player then ran three times as is, and the two all-messages players three more times with
`-allMessages`: no run threw, and every one of the 1,320 calls per run returned an object (field values were not checked).
Memory and the IL2CPP counters matched the first measurement (Nerdbank.MessagePack's steps 6 to 8 within the run-to-run
variation above), and first-pass times were within 4% of it.

The rerun found one bug in a probe, fixed in `nerdbank-all-messages`: its count of `[GenerateShape]` types included the
`AllMessages` table and read 662 instead of 661. Only that log line changed.

## Limitations

- **Platform:** a macOS player on Apple silicon, not a phone. The relative sizes should carry over to iOS and Android
  IL2CPP builds, but the absolute numbers will differ.
- **Timing:** the one-type steps are not timed, so the time of MessagePack-CSharp's 11 MB first lookup is not measured
  here. The times above are single cold passes, not throughput benchmarks.
- **Payloads:** references and collections are nil. MessagePack-CSharp still resolves the formatters of those members
  when it reads a nil, but it does not go deeper; payloads that fill them in would make it touch more types.
  Nerdbank.MessagePack builds the same converters either way.
- **Versions:** only MessagePack-CSharp 3.1.12 and Nerdbank.MessagePack 1.4.104 were measured here. A generator change
  that makes MessagePack-CSharp's first lookup resolve only the requested type
  ([AlanLiu90/MessagePack-CSharp `lazy-generated-resolver`](https://github.com/AlanLiu90/MessagePack-CSharp/tree/lazy-generated-resolver))
  would remove most of its 11 MB first-lookup cost; it was not measured in this project.

## Reproduce

| Branch | What it measures |
|---|---|
| `main` | MessagePack-CSharp, `Ping` |
| `messagepack-all-messages` | MessagePack-CSharp, `Ping` and `-allMessages` |
| `nerdbank-messagepack` | Nerdbank.MessagePack, `Ping` |
| `nerdbank-all-messages` | Nerdbank.MessagePack, `Ping` and `-allMessages` |

Build the player with **Repro > Build IL2CPP Player** (see [README.md](README.md)), then run it. On macOS, Unity runs
`git` to fetch the git packages; if `xcode-select` points at a broken Command Line Tools install, start Unity with
`DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer`.

```
Build/StandaloneOSX/MessagePackIl2CppRepro.app/Contents/MacOS/MessagePackIl2CppRepro -batchmode -nographics -quitAfterProbe [-allMessages] -logFile -
```
