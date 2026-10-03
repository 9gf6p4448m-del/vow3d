using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // 弓拖曳瞄準凍結驗收 B1（純邏輯）。凍結檔：vow-toolchain/acceptance-bowaim-20261003.md。
    // 預期值逐一手算（不呼叫被測公式）：offset＝clamp((dx − sign(dx)·2) / 13, −1, 1) × 40°；|dx| ≤ 2mm＝0。
    public sealed class BowAimLogicTests
    {
        private const float Tol = 1e-4f;

        [Test] // B1：死區、線性段、上限、負向
        public void B1_OffsetDegrees_DeadZoneLinearAndClamp()
        {
            float[] dx = { 0f, 1.9f, 2f, 2.1f, 8.5f, 15f, 30f, -15f, -30f };
            float[] expected =
            {
                0f, 0f, 0f,
                0.1f / 13f * 40f,   // 0.307692…
                20f,                // (8.5 − 2) / 13 × 40
                40f, 40f, -40f, -40f
            };
            for (int i = 0; i < dx.Length; i++)
                Assert.AreEqual(expected[i], BowAimLogic.OffsetDegrees(dx[i]), Tol, "dx=" + dx[i] + "mm");
            Assert.AreEqual(-20f, BowAimLogic.OffsetDegrees(-8.5f), Tol, "負向對稱");
            Assert.AreEqual(-0.1f / 13f * 40f, BowAimLogic.OffsetDegrees(-2.1f), Tol, "負向剛出死區");
            Assert.AreEqual(0f, BowAimLogic.OffsetDegrees(-2f), Tol, "負向死區邊界");
        }

        [Test] // B1：鏡頭追蹤步進——限速、不過衝、跨 ±180° 走最短方向
        public void B1_StepYawToward_SpeedLimitedNoOvershootShortestPath()
        {
            Assert.AreEqual(9f, BowAimLogic.StepYawToward(0f, 60f, 90f, 0.1f), Tol, "差 60°、dt 0.1 → 只轉 9°");
            Assert.AreEqual(5f, BowAimLogic.StepYawToward(0f, 5f, 90f, 0.1f), Tol, "差 5°、dt 0.1 → 轉 5° 不過衝");
            Assert.AreEqual(359f, BowAimLogic.StepYawToward(350f, 10f, 90f, 0.1f), Tol, "350°→10°：往 + 方向（最短 +20°），一步 9°");
            Assert.AreEqual(10f, BowAimLogic.StepYawToward(350f, 10f, 90f, 1f), Tol, "350°→10°：總共 +20° 抵達、不過衝");
            Assert.AreEqual(351f, BowAimLogic.StepYawToward(0f, 300f, 90f, 0.1f), Tol, "0°→300°：最短 −60°，一步 −9°");
            Assert.AreEqual(350f, BowAimLogic.StepYawToward(10f, 350f, 90f, 1f), Tol, "10°→350°：−20° 抵達");
            Assert.AreEqual(-20f, BowAimLogic.DeltaDegrees(10f, 350f), Tol);
            Assert.AreEqual(20f, BowAimLogic.DeltaDegrees(350f, 10f), Tol);
            Assert.AreEqual(30f, BowAimLogic.StepYawToward(30f, 90f, 90f, 0f), Tol, "dt 0 不動");
        }
    }
}
