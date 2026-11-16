using MessagePack;

namespace Repro
{
    // The type the probe deserializes: one int field, payload [1] (2 bytes).
    [MessagePackObject]
    public class Ping
    {
        [Key(0)]
        public int Id;
    }
}
