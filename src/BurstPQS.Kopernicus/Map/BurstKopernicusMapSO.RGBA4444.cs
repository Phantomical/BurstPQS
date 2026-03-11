using BurstPQS.CompilerServices;
using BurstPQS.Map;
using KSPTextureLoader;
using Unity.Burst;

namespace BurstPQS.Kopernicus.Map;

static partial class BurstKopernicusMapSO
{
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.RGBA4444>), Name = "mapSO")]
    [BurstCompile]
    public partial struct RGBA4444 : IMapSO { }
}
