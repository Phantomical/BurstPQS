using BurstPQS.CompilerServices;
using KSPTextureLoader;
using Unity.Burst;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.RHalf>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct RHalf : IMapSO { }
}
