using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS.Jobs;

/// <summary>
/// Clamps a completed block's height data to the sphere's own bounds, copies it into the
/// correct position within the full-resolution output array, and records the block's range.
/// </summary>
/// <remarks>
/// Ranges are recorded per block so no two of these ever touch the same slot, and folding them
/// together afterwards reads a couple of floats per block rather than the whole texture again.
/// </remarks>
[BurstCompile]
internal struct TextureExportCopyHeightsJob : IJob
{
    [ReadOnly]
    public NativeArray<float> blockHeights;

    [NativeDisableContainerSafetyRestriction]
    public NativeArray<float> outputHeights;

    /// <summary>Takes this block's minimum at <c>block * 2</c> and its maximum right after.</summary>
    [NativeDisableContainerSafetyRestriction]
    public NativeArray<float> blockMinMax;

    public int block;

    public float clampMin,
        clampMax;

    public int resX;
    public int startX,
        startY;
    public int blockW,
        blockH;

    public void Execute()
    {
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;

        for (int ly = 0; ly < blockH; ly++)
        {
            int outRow = (startY + ly) * resX + startX;
            int blkRow = ly * blockW;

            for (int lx = 0; lx < blockW; lx++)
            {
                float height = math.clamp(blockHeights[blkRow + lx], clampMin, clampMax);

                outputHeights[outRow + lx] = height;
                min = math.min(min, height);
                max = math.max(max, height);
            }
        }

        blockMinMax[block * 2] = min;
        blockMinMax[block * 2 + 1] = max;
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
