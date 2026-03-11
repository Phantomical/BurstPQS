using BurstPQS.CompilerServices;
using KSPTextureLoader;
using Unity.Burst;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// DXT1/BC1 compressed format. Encodes RGB with optional 1-bit alpha in 8-byte blocks
    /// covering 4x4 pixels (4 bits/pixel). Each block stores two RGB565 color endpoints and
    /// a 4x4 grid of 2-bit indices selecting from a 4-color palette interpolated between them.
    /// When <c>color0 &lt;= color1</c>, index 3 produces transparent black (1-bit alpha).
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.DXT1>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct DXT1 : IMapSO { }
}
