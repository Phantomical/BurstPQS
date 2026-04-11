using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS.Jobs;

/// <summary>
/// Copies a completed block's height data into the correct position
/// within the full-resolution output array.
/// </summary>
[BurstCompile]
internal struct TextureExportCopyHeightsJob : IJob
{
    [ReadOnly]
    public NativeArray<float> blockHeights;

    [NativeDisableContainerSafetyRestriction]
    public NativeArray<float> outputHeights;

    public int resX;
    public int startX,
        startY;
    public int blockW,
        blockH;

    public void Execute()
    {
        for (int ly = 0; ly < blockH; ly++)
        {
            int outRow = (startY + ly) * resX + startX;
            int blkRow = ly * blockW;

            for (int lx = 0; lx < blockW; lx++)
                outputHeights[outRow + lx] = blockHeights[blkRow + lx];
        }
    }
}

/// <summary>
/// Copies a completed block's normal data into the correct position
/// within the full-resolution output array.
/// </summary>
[BurstCompile]
internal struct TextureExportCopyNormalsJob : IJob
{
    [ReadOnly]
    public NativeArray<Vector3> blockNormals;

    [NativeDisableContainerSafetyRestriction]
    public NativeArray<Color32> outputNormals;

    public int resX;
    public int startX,
        startY;
    public int blockW,
        blockH;

    public void Execute()
    {
        for (int ly = 0; ly < blockH; ly++)
        {
            int outRow = (startY + ly) * resX + startX;
            int blkRow = ly * blockW;

            for (int lx = 0; lx < blockW; lx++)
            {
                var n = blockNormals[blkRow + lx];
                outputNormals[outRow + lx] = new Color32(
                    (byte)(n.x * 127.5f + 127.5f),
                    (byte)(n.y * 127.5f + 127.5f),
                    (byte)(n.z * 127.5f + 127.5f),
                    255
                );
            }
        }
    }
}

/// <summary>
/// Copies a completed block's color data into the correct position
/// within the full-resolution output array.
/// </summary>
[BurstCompile]
internal struct TextureExportCopyColorsJob : IJob
{
    [ReadOnly]
    public NativeArray<Color> blockColors;

    [NativeDisableContainerSafetyRestriction]
    public NativeArray<Color32> outputColors;

    public int resX;
    public int startX,
        startY;
    public int blockW,
        blockH;

    public void Execute()
    {
        for (int ly = 0; ly < blockH; ly++)
        {
            int outRow = (startY + ly) * resX + startX;
            int blkRow = ly * blockW;

            for (int lx = 0; lx < blockW; lx++)
                outputColors[outRow + lx] = blockColors[blkRow + lx];
        }
    }
}
