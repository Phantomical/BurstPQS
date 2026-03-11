using BurstPQS.CompilerServices;
using KSPTextureLoader;
using Unity.Burst;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// BC4 compressed format. Encodes a single channel (R) in 8-byte blocks covering
    /// 4x4 pixels (4 bits/pixel). Each block stores two 8-bit endpoints and a 4x4 grid
    /// of 3-bit indices selecting from a 6- or 8-value palette interpolated between them.
    /// Uses the same alpha block encoding as DXT5.
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.BC4>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct BC4 : IMapSO { }
}
