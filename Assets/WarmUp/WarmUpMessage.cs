using Nerdbank.MessagePack;
using PolyType;

namespace Repro.WarmUp
{
    // Lives in its own assembly, so deserializing it warms up the serializer without touching the shapes generated for
    // Assembly-CSharp, which holds the message catalog and Ping.
    [GenerateShape]
    public partial class WarmUpMessage
    {
        [Key(0)]
        public int Id;
    }
}
