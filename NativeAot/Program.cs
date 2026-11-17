using System;
using MessagePack;
using MessagePack.Resolvers;
using Repro;
using Repro.Messages;

// The steps of FirstDeserializeProbe on messagepack-all-messages. NativeAOT cannot run StandardResolver's dynamic
// fallbacks, so the generated resolver goes first, as MessagePack-CSharp recommends for AOT.
MessagePackSerializer.DefaultOptions = MessagePackSerializerOptions.Standard.WithResolver(
    CompositeResolver.Create(GeneratedMessagePackResolver.Instance, StandardResolver.Instance));
_ = AllMessages.Entries.Length; // build the payload table before measuring

int deserialized = 0;
void Part(int first, int end)
{
    for (int i = first; i < end; i++)
    {
        if (AllMessages.Entries[i].Deserialize() != null)
        {
            deserialized++;
        }
    }
}

int n = AllMessages.Entries.Length;
string report = ProbeRunner.Run(
    $"MessagePack-CSharp probe, {GeneratedMessagePackResolver.Instance.GetType().Assembly.GetName().Name}",
    new (string, Action)[]
    {
        ("1. Deserialize<int>, warm-up", () => MessagePackSerializer.Deserialize<int>(new byte[] { 0x01 })),
        ("2. GeneratedMessagePackResolver.Instance.GetFormatter<Ping>()", () => GeneratedMessagePackResolver.Instance.GetFormatter<Ping>()),
        ("3. Deserialize<Ping> #1", () => MessagePackSerializer.Deserialize<Ping>(new byte[] { 0x91, 0x01 })),
        ("4. Deserialize<Ping> #2", () => MessagePackSerializer.Deserialize<Ping>(new byte[] { 0x91, 0x01 })),
        ("5. Deserialize Msg0000", () => Part(0, 1)),
        ("6. Deserialize Msg0001-Msg0009", () => Part(1, 10)),
        ("7. Deserialize Msg0010-Msg0099", () => Part(10, 100)),
        ("8. Deserialize Msg0100-Msg0599", () => Part(100, 600)),
        ("9. Deserialize Pod000-Pod059", () => Part(600, n)),
        ("10. Deserialize all 660 again", () => Part(0, n)),
    },
    ("First use of Ping in total (2 + 3)", 1, 2),
    ("All 660 messages, first time (5-9)", 4, 8));
Console.Write(report);
Console.WriteLine($"Deserialized {deserialized} non-null messages of {n * 2} calls");
