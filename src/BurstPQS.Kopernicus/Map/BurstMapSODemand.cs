using BurstPQS.Map;
using Kopernicus.OnDemand;
using static Kopernicus.OnDemand.MapSODemand;

namespace BurstPQS.Kopernicus.Map;

internal static partial class BurstMapSODemand
{
    internal static BurstMapSO Create(MapSODemand mapSO)
    {
        if (!mapSO.IsLoaded)
            mapSO.Load();

        if (!mapSO.IsLoaded)
            return BurstMapSO.Create(new InvalidMapSO());

        var image = mapSO.Image;
        var width = mapSO.Width;
        var height = mapSO.Height;

        return mapSO.Format switch
        {
            MemoryFormat.A8 => BurstMapSO.Create(
                new TextureMapSO.Alpha8(image, width, height, mapSO.Depth)
            ),
            MemoryFormat.R8 => BurstMapSO.Create(
                new TextureMapSO.R8(image, width, height, mapSO.Depth)
            ),
            MemoryFormat.R16 => BurstMapSO.Create(
                new TextureMapSO.R16(image.Reinterpret<ushort>(1), width, height, mapSO.Depth)
            ),
            MemoryFormat.RA16 => BurstMapSO.Create(
                new TextureMapSO.RA16(image, width, height, mapSO.Depth)
            ),
            MemoryFormat.RGB24 => BurstMapSO.Create(
                new TextureMapSO.RGB24(image, width, height, mapSO.Depth)
            ),
            MemoryFormat.RGBA32 => BurstMapSO.Create(
                new TextureMapSO.RGBA32(image, width, height, mapSO.Depth)
            ),
            _ => BurstMapSO.Create(new InvalidMapSO()),
        };
    }
}
