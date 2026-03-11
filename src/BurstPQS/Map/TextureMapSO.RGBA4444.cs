using BurstPQS.CompilerServices;
using KSPTextureLoader;
using Unity.Burst;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// RGBA4444 format. Each pixel is a 16-bit value with 4 bits per channel
    /// in the order R, G, B, A (high to low bits).
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.RGBA4444>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct RGBA4444 : IMapSO { }
}
