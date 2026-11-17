using System;
using Nerdbank.MessagePack;
using PolyType;
using Repro;
using Repro.Messages;
using Repro.WarmUp;

// The steps of FirstDeserializeProbe on nerdbank-all-messages. On .NET, [GenerateShape] types implement IShapeable<T>,
// so the shape lookup is T.GetTypeShape() rather than the reflection-based TypeShapeResolver.ResolveDynamicOrThrow<T>().
_ = AllMessages.Entries.Length; // build the payload table before measuring

MessagePackSerializer serializer = null;
int deserialized = 0;
void Part(int first, int end)
{
    for (int i = first; i < end; i++)
    {
        if (AllMessages.Entries[i].Deserialize(serializer) != null)
        {
            deserialized++;
        }
    }
}

static ITypeShape<T> ShapeOf<T>() where T : IShapeable<T> => T.GetTypeShape();

int n = AllMessages.Entries.Length;
string report = ProbeRunner.Run(
    "Nerdbank.MessagePack probe",
    new (string, Action)[]
    {
        ("1. new MessagePackSerializer() + Deserialize<WarmUpMessage>, warm-up", () => { serializer = new MessagePackSerializer(); serializer.Deserialize<WarmUpMessage>(new byte[] { 0x91, 0x01 }); }),
        ("2. Ping's shape (IShapeable<Ping>.GetTypeShape())", () => ShapeOf<Ping>()),
        ("3. Deserialize<Ping> #1", () => serializer.Deserialize<Ping>(new byte[] { 0x91, 0x01 })),
        ("4. Deserialize<Ping> #2", () => serializer.Deserialize<Ping>(new byte[] { 0x91, 0x01 })),
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
