using BurstPQS.CompilerServices;
using KSPTextureLoader;
using Unity.Burst;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// ARGB4444 format. Each pixel is a 16-bit value with 4 bits per channel
    /// in the order A, R, G, B (high to low bits).
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.ARGB4444>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct ARGB444 : IMapSO { }
}
