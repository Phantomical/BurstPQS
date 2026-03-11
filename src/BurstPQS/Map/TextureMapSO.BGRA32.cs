using BurstPQS.CompilerServices;
using KSPTextureLoader;
using Unity.Burst;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.BGRA32>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct BGRA32 : IMapSO { }
}
