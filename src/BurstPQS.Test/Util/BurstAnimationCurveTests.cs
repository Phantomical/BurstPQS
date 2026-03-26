using BurstPQS.Util;
using KSP.Testing;
using UnityEngine;

namespace BurstPQS.Test.Util;

/// <summary>
/// Validates <see cref="BurstAnimationCurve.Evaluate"/> against
/// <see cref="AnimationCurve.Evaluate"/> across a variety of curve shapes
/// and edge cases.
/// </summary>
public class BurstAnimationCurveTests : BurstPQSTestBase
{
    /// <summary>
    /// Evaluates both curves at many sample points and asserts they match.
    /// </summary>
    void AssertCurvesMatch(
        string label,
        AnimationCurve unity,
        BurstAnimationCurve burst,
        float tMin,
        float tMax,
        int samples = 64,
        float tol = 0.001f
    )
    {
        for (int i = 0; i <= samples; i++)
        {
            float t = Mathf.Lerp(tMin, tMax, i / (float)samples);
            float expected = unity.Evaluate(t);
            float actual = burst.Evaluate(t);
            assertFloatEquals($"{label} t={t:F4}", actual, expected, tol);
        }
    }

    [TestInfo("BurstAnimCurve_LinearTwoKeys")]
    public void TestLinearTwoKeys()
    {
        var curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        var burst = new BurstAnimationCurve(curve);
        try
        {
            AssertCurvesMatch("Linear01", curve, burst, -0.5f, 1.5f);
        }
        finally
        {
            burst.Dispose();
        }
    }

    [TestInfo("BurstAnimCurve_EaseInOut")]
    public void TestEaseInOut()
    {
        var curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        var burst = new BurstAnimationCurve(curve);
        try
        {
            AssertCurvesMatch("EaseInOut", curve, burst, 0f, 1f);
        }
        finally
        {
            burst.Dispose();
        }
    }

    [TestInfo("BurstAnimCurve_Constant")]
    public void TestConstant()
    {
        var curve = AnimationCurve.Constant(0f, 1f, 5f);
        var burst = new BurstAnimationCurve(curve);
        try
        {
            AssertCurvesMatch("Constant", curve, burst, 0f, 1f);
        }
        finally
        {
            burst.Dispose();
        }
    }

    [TestInfo("BurstAnimCurve_SingleKey")]
    public void TestSingleKey()
    {
        var curve = new AnimationCurve(new Keyframe(0.5f, 3f));
        var burst = new BurstAnimationCurve(curve);
        try
        {
            // Single key: should return 3.0 everywhere
            assertFloatEquals("SingleKey t=-1", burst.Evaluate(-1f), 3f, 0.001f);
            assertFloatEquals("SingleKey t=0", burst.Evaluate(0f), 3f, 0.001f);
            assertFloatEquals("SingleKey t=0.5", burst.Evaluate(0.5f), 3f, 0.001f);
            assertFloatEquals("SingleKey t=2", burst.Evaluate(2f), 3f, 0.001f);
        }
        finally
        {
            burst.Dispose();
        }
    }

    [TestInfo("BurstAnimCurve_MultipleSegments")]
    public void TestMultipleSegments()
    {
        // Piecewise curve with 4 keyframes
        var curve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 2f),
            new Keyframe(0.3f, 1f, 0f, 0f),
            new Keyframe(0.7f, -0.5f, 0f, 0f),
            new Keyframe(1f, 1f, 2f, 0f)
        );
        var burst = new BurstAnimationCurve(curve);
        try
        {
            AssertCurvesMatch("MultiSeg", curve, burst, 0f, 1f);
        }
        finally
        {
            burst.Dispose();
        }
    }

    [TestInfo("BurstAnimCurve_SteepTangents")]
    public void TestSteepTangents()
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 10f),
            new Keyframe(1f, 1f, 10f, 0f)
        );
        var burst = new BurstAnimationCurve(curve);
        try
        {
            AssertCurvesMatch("SteepTangents", curve, burst, 0f, 1f);
        }
        finally
        {
            burst.Dispose();
        }
    }

    [TestInfo("BurstAnimCurve_NegativeTimeRange")]
    public void TestNegativeTimeRange()
    {
        var curve = new AnimationCurve(
            new Keyframe(-2f, 5f, 0f, 1f),
            new Keyframe(0f, 0f, -1f, -1f),
            new Keyframe(2f, -3f, 1f, 0f)
        );
        var burst = new BurstAnimationCurve(curve);
        try
        {
            AssertCurvesMatch("NegTime", curve, burst, -2f, 2f);
        }
        finally
        {
            burst.Dispose();
        }
    }

    [TestInfo("BurstAnimCurve_ExactKeyframeTimes")]
    public void TestExactKeyframeTimes()
    {
        // Evaluate exactly at each keyframe time
        var keys = new[]
        {
            new Keyframe(0f, 1f, 0f, 2f),
            new Keyframe(0.25f, 2f, 1f, -1f),
            new Keyframe(0.75f, -1f, 0.5f, 0.5f),
            new Keyframe(1f, 0f, -2f, 0f),
        };
        var curve = new AnimationCurve(keys);
        var burst = new BurstAnimationCurve(curve);
        try
        {
            for (int i = 0; i < keys.Length; i++)
            {
                float expected = curve.Evaluate(keys[i].time);
                float actual = burst.Evaluate(keys[i].time);
                assertFloatEquals($"AtKey[{i}] t={keys[i].time}", actual, expected, 0.001f);
            }
        }
        finally
        {
            burst.Dispose();
        }
    }

    [TestInfo("BurstAnimCurve_ClampBefore")]
    public void TestClampBefore()
    {
        var curve = AnimationCurve.Linear(1f, 5f, 2f, 10f);
        var burst = new BurstAnimationCurve(curve);
        try
        {
            // Before the first key, BurstAnimationCurve clamps to first value
            float expected = curve.keys[0].value;
            assertFloatEquals("ClampBefore t=-10", burst.Evaluate(-10f), expected, 0.001f);
            assertFloatEquals("ClampBefore t=0", burst.Evaluate(0f), expected, 0.001f);
            assertFloatEquals("ClampBefore t=0.99", burst.Evaluate(0.99f), expected, 0.001f);
        }
        finally
        {
            burst.Dispose();
        }
    }

    [TestInfo("BurstAnimCurve_ClampAfter")]
    public void TestClampAfter()
    {
        var curve = AnimationCurve.Linear(0f, 0f, 1f, 5f);
        var burst = new BurstAnimationCurve(curve);
        try
        {
            // After the last key, BurstAnimationCurve clamps to last value
            float expected = curve.keys[curve.keys.Length - 1].value;
            assertFloatEquals("ClampAfter t=1.01", burst.Evaluate(1.01f), expected, 0.001f);
            assertFloatEquals("ClampAfter t=100", burst.Evaluate(100f), expected, 0.001f);
        }
        finally
        {
            burst.Dispose();
        }
    }

    [TestInfo("BurstAnimCurve_AsymmetricTangents")]
    public void TestAsymmetricTangents()
    {
        // Keys where inTangent != outTangent to stress the Hermite interpolation
        var curve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 5f),
            new Keyframe(0.5f, 1f, -3f, 3f),
            new Keyframe(1f, 0f, -5f, 0f)
        );
        var burst = new BurstAnimationCurve(curve);
        try
        {
            AssertCurvesMatch("AsymTan", curve, burst, 0f, 1f, samples: 128);
        }
        finally
        {
            burst.Dispose();
        }
    }

    [TestInfo("BurstAnimCurve_ManyKeys")]
    public void TestManyKeys()
    {
        // Build a curve with many keyframes to test segment selection
        var keys = new Keyframe[20];
        for (int i = 0; i < keys.Length; i++)
        {
            float t = i / (float)(keys.Length - 1);
            float v = Mathf.Sin(t * Mathf.PI * 2f);
            keys[i] = new Keyframe(t, v, 0f, 0f);
        }
        var curve = new AnimationCurve(keys);
        var burst = new BurstAnimationCurve(curve);
        try
        {
            AssertCurvesMatch("ManyKeys", curve, burst, 0f, 1f, samples: 200);
        }
        finally
        {
            burst.Dispose();
        }
    }

    [TestInfo("BurstAnimCurve_EmptyCurve")]
    public void TestEmptyCurve()
    {
        var curve = new AnimationCurve();
        var burst = new BurstAnimationCurve(curve);
        try
        {
            // Empty curve should return 0
            assertFloatEquals("Empty t=0", burst.Evaluate(0f), 0f, 0.001f);
            assertFloatEquals("Empty t=1", burst.Evaluate(1f), 0f, 0.001f);
        }
        finally
        {
            burst.Dispose();
        }
    }

    [TestInfo("BurstAnimCurve_CloselySpacedKeys")]
    public void TestCloselySpacedKeys()
    {
        // Very small time intervals to test numerical stability
        var curve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 1f),
            new Keyframe(0.001f, 1f, 1f, -1f),
            new Keyframe(0.002f, 0f, -1f, 0f)
        );
        var burst = new BurstAnimationCurve(curve);
        try
        {
            AssertCurvesMatch("CloseTimes", curve, burst, 0f, 0.002f, samples: 32, tol: 0.01f);
        }
        finally
        {
            burst.Dispose();
        }
    }

    [TestInfo("BurstAnimCurve_LargeValues")]
    public void TestLargeValues()
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, -10000f, 0f, 50000f),
            new Keyframe(100f, 10000f, 50000f, 0f)
        );
        var burst = new BurstAnimationCurve(curve);
        try
        {
            AssertCurvesMatch("LargeVals", curve, burst, 0f, 100f, tol: 1f);
        }
        finally
        {
            burst.Dispose();
        }
    }
}
