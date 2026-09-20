using System;
using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // PHASE2_BATCH4_PLAN.md §5 V2-a～V2-e：圓形／扇形的邊界判定與名冊收集。
    // 刻意避開軸對齊角度（批 2 R9 教訓，不用 0°／45°／90°），一律用方位角計算落點座標。
    public sealed class ElementGeometryTests
    {
        private const float Epsilon = 1e-4f;

        private static void PointAtAngle(float originX, float originZ, float angleDegrees, float distance,
                                          out float x, out float z)
        {
            double rad = angleDegrees * Math.PI / 180.0;
            x = (float)(originX + Math.Cos(rad) * distance);
            z = (float)(originZ + Math.Sin(rad) * distance);
        }

        // V2-a IsInsideCircle_IncludesTheBoundary_AndExcludesAPointJustOutside
        // 用 3-4-5 直角三角形的精確浮點分量（1.8/2.4）取代三角函數算出來的落點：
        // 三角函數＋單次四捨五入雖然數學上更準，但對「距圓心恰好等於半徑」這種吃緊的邊界比較，
        // 反而會被最後一位有效位的捨入方向翻面（已用 float32 逐步驗證過）。1.8²+2.4²＝9.0 這組
        // 分量在 float32 底下剛好整除、不會被捨入誤差干擾，仍然刻意避開軸對齊（角度約 53°）。
        [Test]
        public void IsInsideCircle_IncludesTheBoundary_AndExcludesAPointJustOutside()
        {
            const float cx = 2.3f, cz = -1.7f, radius = 3f;

            Assert.IsTrue(ElementGeometry.IsInsideCircle(cx, cz, cx, cz, radius), "圓心本身必須算在內");

            // 3-4-5 三角形分量：1.8² + 2.4² = 9.0，恰為半徑 3 的平方，float32 下精確整除。
            Assert.IsTrue(ElementGeometry.IsInsideCircle(cx + 1.8f, cz + 2.4f, cx, cz, radius),
                "邊界語意含邊界（<=），距圓心恰為半徑的點必須算在內");

            // 同方向等比例放大到 3.1（1.8×3.1/3、2.4×3.1/3 = 1.86、2.48）。
            Assert.IsFalse(ElementGeometry.IsInsideCircle(cx + 1.86f, cz + 2.48f, cx, cz, radius),
                "距圓心 0.1m 超出半徑的點必須排除在外");
        }

        // V2-b IsInsideSector_AcceptsOneDegreeInside_AndRejectsOneDegreeOutside
        [Test]
        public void IsInsideSector_AcceptsOneDegreeInside_AndRejectsOneDegreeOutside()
        {
            ElementTuning tuning = new ElementTuning();
            const float apexX = 0.4f, apexZ = 0.9f, facingDegrees = 37f, distance = 4.0f;
            float totalAngle = tuning.FirestormAngleDegrees; // 讀 tuning，60 被突變成別的值時這條要紅
            float range = tuning.FirestormRangeMeters;

            float dirX = (float)Math.Cos(facingDegrees * Math.PI / 180.0);
            float dirZ = (float)Math.Sin(facingDegrees * Math.PI / 180.0);

            foreach (float offset in new[] { 29f, -29f })
            {
                PointAtAngle(apexX, apexZ, facingDegrees + offset, distance, out float x, out float z);
                Assert.IsTrue(ElementGeometry.IsInsideSector(x, z, apexX, apexZ, dirX, dirZ, range, totalAngle),
                    $"偏 {offset}° 應落在半角 30° 之內");
            }

            foreach (float offset in new[] { 31f, -31f })
            {
                PointAtAngle(apexX, apexZ, facingDegrees + offset, distance, out float x, out float z);
                Assert.IsFalse(ElementGeometry.IsInsideSector(x, z, apexX, apexZ, dirX, dirZ, range, totalAngle),
                    $"偏 {offset}° 應落在半角 30° 之外");
            }
        }

        // V2-c IsInsideSector_AcceptsAtRange_AndRejectsZeroPointOneMetreBeyond
        [Test]
        public void IsInsideSector_AcceptsAtRange_AndRejectsZeroPointOneMetreBeyond()
        {
            ElementTuning tuning = new ElementTuning();
            const float apexX = 0.4f, apexZ = 0.9f, facingDegrees = 37f, offsetDegrees = 10f;
            float totalAngle = tuning.FirestormAngleDegrees;
            float range = tuning.FirestormRangeMeters; // 讀 tuning，6 被突變成別的值時這條要紅

            float dirX = (float)Math.Cos(facingDegrees * Math.PI / 180.0);
            float dirZ = (float)Math.Sin(facingDegrees * Math.PI / 180.0);

            PointAtAngle(apexX, apexZ, facingDegrees + offsetDegrees, 6.000f, out float atRangeX, out float atRangeZ);
            Assert.IsTrue(ElementGeometry.IsInsideSector(atRangeX, atRangeZ, apexX, apexZ, dirX, dirZ, range, totalAngle),
                "恰在射程上的點必須算在內（含邊界）");

            PointAtAngle(apexX, apexZ, facingDegrees + offsetDegrees, 6.100f, out float beyondX, out float beyondZ);
            Assert.IsFalse(ElementGeometry.IsInsideSector(beyondX, beyondZ, apexX, apexZ, dirX, dirZ, range, totalAngle),
                "超出射程 0.1m 的點必須排除在外");
        }

        // V2-d IsInsideSector_WithAZeroLengthDirection_ReturnsFalse
        [Test]
        public void IsInsideSector_WithAZeroLengthDirection_ReturnsFalse()
        {
            bool result = ElementGeometry.IsInsideSector(1f, 1f, 0f, 0f, 0f, 0f, 6f, 60f);
            Assert.IsFalse(result, "方向向量長度為零時一律 false，不得 true 也不得 NaN");
            Assert.IsFalse(float.IsNaN(result ? 1f : 0f));
        }

        // V2-e CollectInsideCircle_ReturnsIndicesInAscendingOrder_AndStopsAtBufferCapacity
        [Test]
        public void CollectInsideCircle_ReturnsIndicesInAscendingOrder_AndStopsAtBufferCapacity()
        {
            // 半徑 5，8 個點：索引 0,2,4,5,6,7 在圓內（6 個），1,3 在圓外，全部離邊界 >0.5m 沒有精度風險。
            float[] xs = { 1f, 10f, 2f, -20f, 0f, -3f, 4f, 0f };
            float[] zs = { 1f, 10f, 0f, 0f, 3f, -3f, 0f, -4.5f };
            int[] outIndices = new int[4];

            int written = ElementGeometry.CollectInsideCircle(xs, zs, xs.Length, 0f, 0f, 5f, outIndices);

            Assert.AreEqual(4, written, "buffer 只有 4 格，寫滿就該停");
            Assert.AreEqual(0, outIndices[0]);
            Assert.AreEqual(2, outIndices[1]);
            Assert.AreEqual(4, outIndices[2]);
            Assert.AreEqual(5, outIndices[3]);
        }
    }
}
