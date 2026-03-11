using BurstPQS.CompilerServices;
using KSPTextureLoader;
using Unity.Burst;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// BC6H compressed format. Encodes HDR RGB (no alpha) in 16-byte blocks covering
    /// 4x4 pixels (8 bits/pixel). Supports 14 modes (0-13) with 1 or 2 subsets,
    /// half-float endpoints (10-16 bits) with optional delta encoding, and 3- or 4-bit
    /// indices. Endpoints are unquantized to 16-bit half-precision floats and interpolated.
    /// Comes in signed (<c>BC6H_SF16</c>) and unsigned (<c>BC6H_UF16</c>) variants.
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.BC6H>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct BC6H : IMapSO { }
}
