using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    // V9-A01～V9-A05、V9-A15～V9-A17：新數值、19 塊幾何與相鄰表、開局與復活點表、對手選點、HUD 字串表、7 塊夾具
    // （V090_ENCIRCLE_PLAN.md §3-A，凍結；期望值一律寫死字面值）。
    public sealed class Capture19BoardTests
    {
        private const int Blue = Capture19Kit.Blue;
        private const int Red = Capture19Kit.Red;
        private const int Neutral = Capture19Kit.Neutral;

        // ── V9-A01 新數值 ──
        [Test]
        public void V9A01_CaptureTuning_HasTheFrozenRageValues()
        {
            var t = new CaptureTuning();
            Assert.AreEqual(12f, t.RageSeconds);
            Assert.AreEqual(1.15f, t.RageSpeedMultiplier);
            Assert.AreEqual(3, t.RageDeficitNumerator);
            Assert.AreEqual(20, t.RageDeficitDenominator);
        }

        // ── V9-A02 19 塊中心與場地 ──
        [Test]
        public void V9A02_NineteenCenters_FieldExtents_AndEachCircleInsideItsOwnHex()
        {
            var spec = CaptureBoardSpec.V090Nineteen;
            Assert.AreEqual(19, spec.TileCount);
            for (int k = 0; k < 19; k++)
            {
                Assert.AreEqual(Capture19Kit.CX[k], spec.CenterX(k), "塔心 x " + k);
                Assert.AreEqual(Capture19Kit.CZ[k], spec.CenterZ(k), "塔心 z " + k);
            }

            float maxAbsX = 0f, maxAbsZ = 0f;
            for (int k = 0; k < 19; k++)
            {
                for (int v = 0; v < 6; v++)
                {
                    float ax = System.Math.Abs(spec.VertexX(k, v));
                    float az = System.Math.Abs(spec.VertexZ(k, v));
                    Assert.LessOrEqual(ax, 19.0f, "頂點 |x| ≤ 19");
                    Assert.LessOrEqual(az, 19.0f, "頂點 |z| ≤ 19");
                    if (ax > maxAbsX) maxAbsX = ax;
                    if (az > maxAbsZ) maxAbsZ = az;
                }
            }
            Assert.AreEqual(17.5f, maxAbsX, "max|x|");
            Assert.AreEqual(18.9453125f, maxAbsZ, "max|z|");

            for (int k = 0; k < 19; k++)
            {
                float cx = Capture19Kit.CX[k], cz = Capture19Kit.CZ[k];
                Assert.AreEqual(k, spec.TileAt(cx + 2.5f, cz), "光圈東緣在自己的六角內 " + k);
                Assert.AreEqual(k, spec.TileAt(cx - 2.5f, cz), "光圈西緣在自己的六角內 " + k);
                Assert.AreEqual(k, spec.TileAt(cx, cz + 2.5f), "光圈北緣在自己的六角內 " + k);
                Assert.AreEqual(k, spec.TileAt(cx, cz - 2.5f), "光圈南緣在自己的六角內 " + k);
            }
        }

        // E5 字面相鄰表。
        private static readonly int[][] ExpectedNeighbors =
        {
            new[] { 1, 2, 3, 4, 5, 6 },
            new[] { 0, 2, 6, 7, 8, 18 },
            new[] { 0, 1, 3, 8, 9, 10 },
            new[] { 0, 2, 4, 10, 11, 12 },
            new[] { 0, 3, 5, 12, 13, 14 },
            new[] { 0, 4, 6, 14, 15, 16 },
            new[] { 0, 1, 5, 16, 17, 18 },
            new[] { 1, 8, 18 },
            new[] { 1, 2, 7, 9 },
            new[] { 2, 8, 10 },
            new[] { 2, 3, 9, 11 },
            new[] { 3, 10, 12 },
            new[] { 3, 4, 11, 13 },
            new[] { 4, 12, 14 },
            new[] { 4, 5, 13, 15 },
            new[] { 5, 14, 16 },
            new[] { 5, 6, 15, 17 },
            new[] { 6, 16, 18 },
            new[] { 1, 6, 7, 17 },
        };

        private static bool IsNeighbor(CaptureBoardSpec spec, int a, int b)
        {
            for (int k = 0; k < spec.NeighborCount(a); k++) if (spec.Neighbor(a, k) == b) return true;
            return false;
        }

        // ── V9-A03 相鄰表 ──
        [Test]
        public void V9A03_Adjacency_MatchesTheFortyTwoEdgeTable()
        {
            var spec = CaptureBoardSpec.V090Nineteen;
            int degreeSum = 0, deg6 = 0, deg4 = 0, deg3 = 0;
            for (int i = 0; i < 19; i++)
            {
                int n = spec.NeighborCount(i);
                var actual = new int[n];
                for (int k = 0; k < n; k++) actual[k] = spec.Neighbor(i, k);
                CollectionAssert.AreEquivalent(ExpectedNeighbors[i], actual, "鄰居集合 " + i);
                CollectionAssert.AllItemsAreUnique(actual, "鄰居不重複 " + i);
                Assert.IsFalse(IsNeighbor(spec, i, i), "沒有自環 " + i);
                for (int k = 0; k < n; k++) Assert.IsTrue(IsNeighbor(spec, actual[k], i), "對稱 " + i + "-" + actual[k]);
                degreeSum += n;
                if (n == 6) deg6++; else if (n == 4) deg4++; else if (n == 3) deg3++;
            }
            Assert.AreEqual(42, degreeSum / 2, "邊數");
            Assert.AreEqual(0, degreeSum % 2);
            Assert.AreEqual(7, deg6, "6 度");
            Assert.AreEqual(6, deg4, "4 度");
            Assert.AreEqual(6, deg3, "3 度");
        }

        // ── V9-A04 點→板塊／光圈邊界（含邊界，重疊取索引小） ──
        [Test]
        public void V9A04_TileAtAndCircleAt_BoundaryInclusive_SmallestIndexOnOverlap()
        {
            var spec = CaptureBoardSpec.V090Nineteen;
            Assert.AreEqual(0, spec.TileAt(0f, 3.7890625f));
            Assert.AreEqual(1, spec.TileAt(0f, 3.796875f));
            Assert.AreEqual(0, spec.TileAt(4.375f, 0f));
            Assert.AreEqual(2, spec.TileAt(4.3828125f, 0f));
            Assert.AreEqual(3, spec.TileAt(4.3828125f, -0.0078125f));
            Assert.AreEqual(9, spec.TileAt(17.5f, 7.578125f));
            Assert.AreEqual(-1, spec.TileAt(17.5078125f, 7.578125f));
            Assert.AreEqual(13, spec.TileAt(0f, -18.9453125f));
            Assert.AreEqual(-1, spec.TileAt(0f, -18.953125f));

            Assert.AreEqual(0, spec.CircleAt(2.5f, 0f, 2.5f));
            Assert.AreEqual(-1, spec.CircleAt(2.5078125f, 0f, 2.5f));
            Assert.AreEqual(13, spec.CircleAt(0f, -17.65625f, 2.5f));
            Assert.AreEqual(-1, spec.CircleAt(0f, -17.6640625f, 2.5f));
            Assert.AreEqual(12, spec.CircleAt(6.5625f, -13.8671875f, 2.5f));
            Assert.AreEqual(-1, spec.CircleAt(6.5625f, -13.875f, 2.5f));
        }

        // ── V9-A05 母板塊、開局、復活點表 ──
        [Test]
        public void V9A05_MotherTiles_KickoffOwnership_AndRespawnTable()
        {
            var l = Capture19Kit.NewActive19();
            int[] expected =
            {
                Neutral, Neutral, Neutral, Neutral, Neutral, Neutral, Neutral,
                Red, Red, Neutral, Neutral, Neutral, Blue, Blue, Blue, Neutral, Neutral, Neutral, Red,
            };
            for (int i = 0; i < 19; i++) Assert.AreEqual(expected[i], l.OwnerOf(i), "開局歸屬 " + i);
            Assert.AreEqual(0, l.BlueScore);
            Assert.AreEqual(0, l.RedScore);

            var spec = CaptureBoardSpec.V090Nineteen;
            Assert.AreEqual(3, spec.MotherCount(Blue));
            Assert.AreEqual(3, spec.MotherCount(Red));
            CollectionAssert.AreEqual(new[] { 13, 12, 14 },
                new[] { spec.MotherTile(Blue, 0), spec.MotherTile(Blue, 1), spec.MotherTile(Blue, 2) }, "藍方優先序");
            CollectionAssert.AreEqual(new[] { 7, 18, 8 },
                new[] { spec.MotherTile(Red, 0), spec.MotherTile(Red, 1), spec.MotherTile(Red, 2) }, "紅方優先序");

            float[] blueX = { 0f, 6.5625f, -6.5625f };
            float[] blueZ = { -16.65625f, -12.8671875f, -12.8671875f };
            float[] redX = { 0f, -6.5625f, 6.5625f };
            float[] redZ = { 16.65625f, 12.8671875f, 12.8671875f };
            for (int r = 0; r < 3; r++)
            {
                Assert.AreEqual(blueX[r], spec.MotherRespawnX(Blue, r), "藍方復活點 x " + r);
                Assert.AreEqual(blueZ[r], spec.MotherRespawnZ(Blue, r), "藍方復活點 z " + r);
                Assert.AreEqual(redX[r], spec.MotherRespawnX(Red, r), "紅方復活點 x " + r);
                Assert.AreEqual(redZ[r], spec.MotherRespawnZ(Red, r), "紅方復活點 z " + r);
            }
            Assert.AreEqual(-17f, spec.EdgeRespawnX(Blue));
            Assert.AreEqual(-17f, spec.EdgeRespawnZ(Blue));
            Assert.AreEqual(17f, spec.EdgeRespawnX(Red));
            Assert.AreEqual(17f, spec.EdgeRespawnZ(Red));
        }

        // ── V9-A15 對手選目標（19 塊） ──
        [Test]
        public void V9A15_OpponentTargetSelection_NineteenTiles_SmallestIndexOnExactTie()
        {
            var spec = CaptureBoardSpec.V090Nineteen;
            int[] a = Capture19Kit.Board(new[] { 12, 13, 14 }, new[] { 7, 8, 18 });
            Assert.AreEqual(1, CaptureOpponentPolicy.SelectTargetTile(0f, 16.65625f, a, spec), "(a)");
            int[] b = Capture19Kit.Board(new[] { 12, 13, 14 }, new[] { 7, 8, 18, 1 });
            Assert.AreEqual(2, CaptureOpponentPolicy.SelectTargetTile(0f, 7.578125f, b, spec), "(b) 2、6 精確平手取小");
            int[] c = Capture19Kit.Board(new[] { 12, 13, 14 }, new[] { 7, 8, 18, 1, 2 });
            Assert.AreEqual(6, CaptureOpponentPolicy.SelectTargetTile(0f, 7.578125f, c, spec), "(c)");
            int[] d = Capture19Kit.Board(new[] { 12, 13, 14 }, new[] { 7, 8, 18, 1, 2, 6 });
            Assert.AreEqual(0, CaptureOpponentPolicy.SelectTargetTile(0f, 7.578125f, d, spec), "(d)");
            var e = new int[19];
            for (int i = 0; i < 19; i++) e[i] = Red;
            Assert.AreEqual(-1, CaptureOpponentPolicy.SelectTargetTile(0f, 7.578125f, e, spec), "(e)");

            var tuning = new CaptureTuning();
            var chase = CaptureOpponentPolicy.Decide(0f, 0f, 6.0f, 0f, false, false, a, tuning, spec);
            Assert.IsTrue(chase.ChaseHero, "英雄 (6.0,0) → 追");
            var noChase = CaptureOpponentPolicy.Decide(0f, 0f, 6.0625f, 0f, false, false, a, tuning, spec);
            Assert.IsFalse(noChase.ChaseHero, "英雄 (6.0625,0) → 不追");
        }

        // ── V9-A16 HUD 字串表 ──
        [Test]
        public void V9A16_ScoreTableTo1037_AndRageLabelsRoundUp()
        {
            Assert.AreEqual("1012", CaptureHudLabels.Score(1012));
            Assert.AreEqual("1013", CaptureHudLabels.Score(1013));
            Assert.AreEqual("1037", CaptureHudLabels.Score(1037));
            Assert.AreEqual("RAGE 12", CaptureHudLabels.Rage(12.0f));
            Assert.AreEqual("RAGE 12", CaptureHudLabels.Rage(11.75f));
            Assert.AreEqual("RAGE 11", CaptureHudLabels.Rage(11.0f));
            Assert.AreEqual("RAGE 1", CaptureHudLabels.Rage(0.25f));
            Assert.IsTrue(ReferenceEquals(CaptureHudLabels.Score(1037), CaptureHudLabels.Score(1037)));
            Assert.IsTrue(ReferenceEquals(CaptureHudLabels.Rage(11.75f), CaptureHudLabels.Rage(11.75f)));
        }

        // ── V9-A17 7 塊夾具＝v0.8.0 規則 ──
        [Test]
        public void V9A17_SevenTileFixture_KeepsV080Rules_NoEncircleNoRage()
        {
            Assert.IsFalse(CaptureBoardSpec.V080Seven.EncircleEnabled);
            Assert.IsFalse(CaptureBoardSpec.V080Seven.RageEnabled);
            Assert.AreEqual(7, CaptureBoardSpec.V080Seven.TileCount);

            // VA09 守塔組盤面：雙方都在 4 號（v0.8.0 藍方基地，塔心 (0,−12.125)）20 個 tick，之後只剩對手。
            var l = new CaptureMatchLogic(new CaptureTuning());
            Assert.IsTrue(l.TryEnterCaptureMode());
            Assert.IsTrue(l.TryStart());
            for (int t = 1; t <= 20; t++) l.Tick(0.25f, 0f, -12.125f, 0f, -12.125f);
            for (int t = 21; t <= 33; t++) l.Tick(0.25f, 1000f, 1000f, 0f, -12.125f);
            Assert.AreEqual(0, l.OwnerOf(4), "t33 4 號仍藍");
            l.Tick(0.25f, 1000f, 1000f, 0f, -12.125f); // t34
            Assert.AreEqual(1, l.FlipCount, "活性：t34 翻塊");
            Assert.AreEqual(1, l.OwnerOf(4), "t34 4 號翻紅後仍是紅（夾具不跑包夾）");
            for (int t = 35; t <= 40; t++) l.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(1, l.OwnerOf(4), "t40 4 號仍是紅");
            Assert.AreEqual(0, l.RedNeutralizedCount);
            Assert.AreEqual(0f, l.BlueRageRemaining);
        }
    }
}
