using BurstPQS.CompilerServices;
using KSPTextureLoader;
using Unity.Burst;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// DXT5/BC3 compressed format. Encodes RGBA in 16-byte blocks covering 4x4 pixels
    /// (8 bits/pixel). Each block consists of an 8-byte alpha block followed by an 8-byte
    /// DXT1 color block. The alpha block stores two 8-bit endpoints and a 4x4 grid of
    /// 3-bit indices selecting from an 8-value palette interpolated between them.
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.DXT5>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct DXT5 : IMapSO { }
}
