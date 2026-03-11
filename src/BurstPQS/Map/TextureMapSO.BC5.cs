using BurstPQS.CompilerServices;
using KSPTextureLoader;
using Unity.Burst;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// BC5 compressed format. Encodes two channels (R, G) in 16-byte blocks covering
    /// 4x4 pixels (8 bits/pixel). Each block consists of two independent BC4 blocks
    /// side by side, one for the red channel and one for the green channel.
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.BC5>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct BC5 : IMapSO { }
}
