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
        // 「距圓心恰好等於半徑」要在每個執行環境都恰好相等，圓心、分量、半徑就得全是二進位有限小數：
        // 3-4-5 的一半（1.5／2.0／2.5）配圓心 (2.25, -1.75)，加、減、平方都沒有捨入，1.5²＋2.0²＝6.25＝2.5²。
        // 前一版用 (2.3, -1.7)＋(1.8, 2.4)、半徑 3：先加再減之後分量已不是 1.8／2.4，
        // .NET 8 判成界內、Unity Mono 判成界外（vow-toolchain/p2b4-edit-base.xml）。仍刻意避開軸對齊（約 53°）。
        [Test]
        public void IsInsideCircle_IncludesTheBoundary_AndExcludesAPointJustOutside()
        {
            const float cx = 2.25f, cz = -1.75f, radius = 2.5f;

            Assert.IsTrue(ElementGeometry.IsInsideCircle(cx, cz, cx, cz, radius), "圓心本身必須算在內");

            Assert.IsTrue(ElementGeometry.IsInsideCircle(cx + 1.5f, cz + 2.0f, cx, cz, radius),
                "邊界語意含邊界（<=），距圓心恰為半徑的點必須算在內");

            // 同方向等比例放大到 2.6（1.5×2.6/2.5、2.0×2.6/2.5 = 1.56、2.08）。
            Assert.IsFalse(ElementGeometry.IsInsideCircle(cx + 1.56f, cz + 2.08f, cx, cz, radius),
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
