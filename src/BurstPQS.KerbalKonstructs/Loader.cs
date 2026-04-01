using BurstPQS.Map;
using KerbalKonstructs.Core;
using UnityEngine;

namespace BurstPQS.KerbalKonstructs;

[KSPAddon(KSPAddon.Startup.Instantly, once: true)]
internal class Loader : MonoBehaviour
{
    void Start()
    {
        BurstMapSO.RegisterMapSOFactoryFunc<MapDecalsMap>(mapSO =>
            BurstMapSO.Create(new StockBurstMapSO(mapSO))
        );
    }
}
