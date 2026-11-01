using Fomoxa;

namespace MyGame.Network.Models
{
    [Network]
    [Codec("net")]
    public class MoveInput
    {
        [Network("f32")]
        [Codec("net")]
        public float X { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float Z { get; set; }
    }
}
