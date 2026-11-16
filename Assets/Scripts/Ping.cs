using Nerdbank.MessagePack;
using PolyType;

namespace Repro
{
    // The type the probe deserializes: one int field, payload [1] (2 bytes).
    [GenerateShape]
    public partial class Ping
    {
        [Key(0)]
        public int Id;
    }
}
