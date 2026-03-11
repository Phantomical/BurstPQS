using BurstPQS.CompilerServices;
using KSPTextureLoader;
using Unity.Burst;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// RGB565 format. Each pixel is a 16-bit value with 5 bits red, 6 bits green,
    /// 5 bits blue (high to low bits). No alpha channel.
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.RGB565>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct RGB565 : IMapSO { }
}
