using BurstPQS.CompilerServices;
using KSPTextureLoader;
using Unity.Burst;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// BC7 compressed format. Encodes RGBA in 16-byte blocks covering 4x4 pixels
    /// (8 bits/pixel). Supports 8 modes (0-7) with varying numbers of subsets (1-3),
    /// endpoint precision (4-8 bits), index precision (2-4 bits), and optional
    /// per-endpoint p-bits, channel rotation, and separate color/alpha index sets.
    /// Partition tables select which pixels belong to which subset.
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.BC7>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct BC7 : IMapSO { }
}
