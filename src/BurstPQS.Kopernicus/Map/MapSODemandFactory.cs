using BurstPQS.Map;
using Kopernicus.OnDemand;
using UnityEngine;

namespace BurstPQS.Kopernicus.Map;

[KSPAddon(KSPAddon.Startup.Instantly, once: true)]
internal class MapSODemandFactory : MonoBehaviour
{
    void Start()
    {
        BurstMapSO.RegisterMapSOFactoryFunc<MapSODemand>(Create);
    }

    static BurstMapSO Create(MapSODemand mapSO)
    {
        if (!mapSO.IsLoaded)
            mapSO.Load();

        if (!mapSO.Image.IsCreated)
        {
            if (mapSO._data != null)
                return BurstMapSO.Create(new StockBurstMapSO(mapSO));

            Debug.LogError(
                $"[BurstPQS.Kopernicus] MapSODemand '{mapSO.name}' has no accessible pixel data"
            );
            return new BurstMapSO();
        }

        var image = mapSO.Image;
        int width = mapSO.Width;
        int height = mapSO.Height;

        return mapSO.Format switch
        {
            MapSODemand.MemoryFormat.R8 or MapSODemand.MemoryFormat.A8 => BurstMapSO.Create(
                new TextureMapSO.R8(image, width, height, MapSO.MapDepth.Greyscale)
            ),
            MapSODemand.MemoryFormat.R16 => BurstMapSO.Create(
                new TextureMapSO.R16(
                    image.Reinterpret<ushort>(1),
                    width,
                    height,
                    MapSO.MapDepth.Greyscale
                )
            ),
            MapSODemand.MemoryFormat.RA16 => BurstMapSO.Create(
                new TextureMapSO.RG16(image, width, height, MapSO.MapDepth.HeightAlpha)
            ),
            MapSODemand.MemoryFormat.RGB24 => BurstMapSO.Create(
                new TextureMapSO.RGB24(image, width, height, MapSO.MapDepth.RGB)
            ),
            MapSODemand.MemoryFormat.RGBA32 => BurstMapSO.Create(
                new TextureMapSO.RGBA32(image, width, height, MapSO.MapDepth.RGBA)
            ),
            _ => CreateFallback(mapSO),
        };
    }

    static BurstMapSO CreateFallback(MapSODemand mapSO)
    {
        Debug.LogWarning(
            $"[BurstPQS.Kopernicus] Unsupported MapSODemand format for '{mapSO.name}', falling back to stock"
        );
        return BurstMapSO.Create(new StockBurstMapSO(mapSO));
    }
}
