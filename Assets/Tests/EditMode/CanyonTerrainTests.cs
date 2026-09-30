using System;
using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    // 測試端的字面值（docs/V0140_CANYON_PLAN.md §2；共同遵守第 1 條：期望值一律字面值，
    // 不讀 CanyonTerrainSpec／CanyonTuning／CaptureBoardSpec 的欄位來算期望值）。
    internal static class CanyonKit
    {
        public const float R = 4.375f;
        public const float H = 3.7890625f;

        public static readonly float[] Xs =
        {
            0f, 0f, 6.5625f, 6.5625f, 0f, -6.5625f, -6.5625f,
            0f, 6.5625f, 13.125f, 13.125f, 13.125f, 6.5625f, 0f, -6.5625f, -13.125f, -13.125f, -13.125f, -6.5625f,
        };

        public static readonly float[] Zs =
        {
            0f, 7.578125f, 3.7890625f, -3.7890625f, -7.578125f, -3.7890625f, 3.7890625f,
            15.15625f, 11.3671875f, 7.578125f, 0f, -7.578125f, -11.3671875f, -15.15625f, -11.3671875f, -7.578125f, 0f, 7.578125f, 11.3671875f,
        };

        // 平頂六角的 6 個頂點偏移（與塔心的相對位置）。
        private static readonly float[] VxOff = { R, R * 0.5f, -R * 0.5f, -R, -R * 0.5f, R * 0.5f };
        private static readonly float[] VzOff = { 0f, H, H, 0f, -H, -H };

        // §2.2 表：斜坡（低端塊, 高端塊）與中心。
        public static readonly int[] RampLow = { 9, 11, 15, 17, 1, 4 };
        public static readonly int[] RampHigh = { 2, 3, 5, 6, 7, 13 };
        public static readonly float[] RampCx = { 9.84375f, 9.84375f, -9.84375f, -9.84375f, 0f, 0f };
        public static readonly float[] RampCz = { 5.68359375f, -5.68359375f, -5.68359375f, 5.68359375f, 11.3671875f, -11.3671875f };

        // §2.3 20 段完整崖壁的共用邊。
        public static readonly int[,] FullCliffPairs =
        {
            { 0, 2 }, { 0, 3 }, { 0, 5 }, { 0, 6 }, { 1, 2 }, { 1, 6 }, { 1, 8 }, { 1, 18 }, { 4, 3 }, { 4, 5 },
            { 4, 12 }, { 4, 14 }, { 2, 8 }, { 2, 10 }, { 3, 10 }, { 3, 12 }, { 5, 14 }, { 5, 16 }, { 6, 16 }, { 6, 18 },
        };

        public static void SharedEdge(int a, int b, out float x0, out float z0, out float x1, out float z1)
        {
            x0 = z0 = x1 = z1 = float.NaN;
            int found = 0;
            for (int i = 0; i < 6; i++)
            {
                float ax = Xs[a] + VxOff[i], az = Zs[a] + VzOff[i];
                for (int j = 0; j < 6; j++)
                {
                    float bx = Xs[b] + VxOff[j], bz = Zs[b] + VzOff[j];
                    if (Math.Abs(ax - bx) > 1e-4f || Math.Abs(az - bz) > 1e-4f) continue;
                    if (found == 0) { x0 = ax; z0 = az; } else { x1 = ax; z1 = az; }
                    found++;
                }
            }
            Assert.AreEqual(2, found, "測試端：" + a + "|" + b + " 應有 2 個共同頂點");
        }

        public static double Dist(double x0, double z0, double x1, double z1)
        {
            double dx = x1 - x0, dz = z1 - z0;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        // 場地外圈 316 格（同 Phase1Bootstrap.RegisterArenaBoundary：格心落在可達範圍外的最外一圈）。
        public static BlockGrid NewArenaGrid()
        {
            var grid = new BlockGrid(-20f, -20f, 0.5f, 80, 80);
            for (int cz = 0; cz < 80; cz++)
                for (int cx = 0; cx < 80; cx++)
                    if (cx == 0 || cx == 79 || cz == 0 || cz == 79) grid.StampCell(cx, cz, +1);
            Assert.AreEqual(316, grid.BlockedCount, "測試端：場地外圈 316 格");
            return grid;
        }

        public static bool BlockedAt(BlockGrid grid, float x, float z)
        {
            grid.TryWorldToCell(x, z, out int cx, out int cz);
            return grid.IsBlocked(cx, cz);
        }
    }

    public sealed class CanyonTerrainTests
    {
        private static CaptureBoardSpec S => CaptureBoardSpec.V0140Canyon;
        private static CanyonTerrainSpec T => CanyonTerrainSpec.V0140;

        [Test]
        public void V14A01_SpecLiterals_CanyonMatchesSanctuaryGeometry_PlusTerrainAndSingleLayer()
        {
            CaptureBoardSpec f = CaptureBoardSpec.V0100Sanctuary;
            Assert.AreEqual(19, S.TileCount);
            Assert.AreEqual(f.TileCount, S.TileCount);
            Assert.AreEqual(f.CircumRadius, S.CircumRadius, 0f);
            Assert.AreEqual(f.InRadius, S.InRadius, 0f);
            int edgeEnds = 0;
            for (int i = 0; i < 19; i++)
            {
                Assert.AreEqual(f.CenterX(i), S.CenterX(i), 0f, "centerX " + i);
                Assert.AreEqual(f.CenterZ(i), S.CenterZ(i), 0f, "centerZ " + i);
                Assert.AreEqual(f.NeighborCount(i), S.NeighborCount(i), "neighborCount " + i);
                for (int k = 0; k < S.NeighborCount(i); k++) Assert.AreEqual(f.Neighbor(i, k), S.Neighbor(i, k));
                edgeEnds += S.NeighborCount(i);
            }
            Assert.AreEqual(84, edgeEnds, "42 條邊");
            for (int side = 0; side <= 1; side++)
            {
                Assert.AreEqual(f.MotherCount(side), S.MotherCount(side));
                for (int rank = 0; rank < S.MotherCount(side); rank++)
                {
                    Assert.AreEqual(f.MotherTile(side, rank), S.MotherTile(side, rank));
                    Assert.AreEqual(f.MotherRespawnX(side, rank), S.MotherRespawnX(side, rank), 0f);
                    Assert.AreEqual(f.MotherRespawnZ(side, rank), S.MotherRespawnZ(side, rank), 0f);
                }
                Assert.AreEqual(f.EdgeRespawnX(side), S.EdgeRespawnX(side), 0f);
                Assert.AreEqual(f.EdgeRespawnZ(side), S.EdgeRespawnZ(side), 0f);
            }
            Assert.AreEqual(f.EncircleEnabled, S.EncircleEnabled);
            Assert.AreEqual(f.RageEnabled, S.RageEnabled);
            Assert.AreEqual(f.SanctuaryEnabled, S.SanctuaryEnabled);
            Assert.AreEqual(f.SiegeEnabled, S.SiegeEnabled);
            Assert.AreEqual(f.TimeLimitEnabled, S.TimeLimitEnabled);
            Assert.AreEqual(f.PacedScoringEnabled, S.PacedScoringEnabled);
            Assert.AreEqual(f.TalentsEnabled, S.TalentsEnabled);

            Assert.IsTrue(S.FogEnabled);
            Assert.IsNotNull(S.Terrain);
            Assert.AreSame(S.Terrain, T);
            Assert.IsTrue(f.FogEnabled);
            Assert.IsNull(f.Terrain, "夾具不得掛地形");
            Assert.IsNull(CaptureBoardSpec.V090Nineteen.Terrain);
            Assert.IsNull(CaptureBoardSpec.V080Seven.Terrain);
            Assert.IsFalse(CaptureBoardSpec.V090Nineteen.FogEnabled);
            Assert.IsFalse(CaptureBoardSpec.V080Seven.FogEnabled);

            float[] heights = { -1f, -1f, 1f, 1f, -1f, 1f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
            for (int i = 0; i < 19; i++) Assert.AreEqual(heights[i], T.TileHeight(i), 0f, "層高 " + i);

            Assert.AreEqual(6, T.RampCount);
            int[] lows = { 9, 11, 15, 17, 1, 4 };
            int[] highs = { 2, 3, 5, 6, 7, 13 };
            for (int r = 0; r < 6; r++)
            {
                Assert.AreEqual(lows[r], T.RampLowTile(r), "ramp low " + r);
                Assert.AreEqual(highs[r], T.RampHighTile(r), "ramp high " + r);
            }
            Assert.AreEqual(3.5f, T.RampWidth, 0f);
            Assert.AreEqual(4f, T.RampLength, 0f);

            int samples = 0;
            float[] ys = { -5f, -1f, 0f, 1f, 3.3f };
            for (int i = 0; i < 80; i += 4)
            {
                for (int j = 0; j < 80; j += 4)
                {
                    float x = -19.75f + 0.5f * i;
                    float z = -19.75f + 0.5f * j;
                    Assert.AreEqual(1, T.LayerCountAt(x, z), "LayerCountAt " + x + "," + z);
                    for (int y = 0; y < ys.Length; y++) Assert.AreEqual(0, T.ResolveLayer(x, z, ys[y]));
                    samples++;
                }
            }
            Assert.AreEqual(400, samples);
        }

        [Test]
        public void V14A02_HeightTable_TileLevels_RampInterpolation_AndOffBoard()
        {
            Assert.AreEqual(-1f, T.HeightAt(0f, 0f, 0), 0f);
            Assert.AreEqual(-1f, T.HeightAt(0f, 7.578125f, 0), 0f);
            Assert.AreEqual(-1f, T.HeightAt(0f, -7.578125f, 0), 0f);
            Assert.AreEqual(1f, T.HeightAt(6.5625f, 3.7890625f, 0), 0f);
            Assert.AreEqual(1f, T.HeightAt(6.5625f, -3.7890625f, 0), 0f);
            Assert.AreEqual(1f, T.HeightAt(-6.5625f, -3.7890625f, 0), 0f);
            Assert.AreEqual(1f, T.HeightAt(-6.5625f, 3.7890625f, 0), 0f);
            for (int i = 7; i <= 18; i++) Assert.AreEqual(0f, T.HeightAt(CanyonKit.Xs[i], CanyonKit.Zs[i], 0), 0f, "塔心 " + i);
            Assert.AreEqual(0f, T.HeightAt(19f, 19f, 0), 0f, "棋盤外");
            Assert.AreEqual(-0.5f, T.HeightAt(0f, -11.3671875f, 0), 0f, "R5 中心");
            Assert.AreEqual(-1f, T.HeightAt(0f, -9.3671875f, 0), 0f, "R5 低端");
            Assert.AreEqual(0f, T.HeightAt(0f, -13.3671875f, 0), 0f, "R5 高端");
            Assert.AreEqual(-0.716796875f, T.HeightAt(1.7421875f, -10.5f, 0), 1e-5f, "R5 側邊內");
            Assert.AreEqual(-1f, T.HeightAt(1.7578125f, -10.5f, 0), 0f, "R5 側邊外");
            Assert.AreEqual(0.5f, T.HeightAt(9.84375f, 5.68359375f, 0), 1e-5f, "R0 中心");
            Assert.AreEqual(-0.5f, T.HeightAt(0f, 11.3671875f, 0), 0f, "R4 中心");
        }

        [Test]
        public void V14A03_TerrainClass_RampBeatsTile_OffBoardIsPlain()
        {
            Assert.AreEqual(TerrainClass.Canyon, T.ClassAt(0f, 0f, 0));
            Assert.AreEqual(TerrainClass.Cliff, T.ClassAt(6.5625f, 3.7890625f, 0));
            Assert.AreEqual(TerrainClass.Plain, T.ClassAt(13.125f, 0f, 0));
            Assert.AreEqual(2, T.TileAt(9.84375f, 5.68359375f), "前提：R0 中心 TileAt＝2");
            Assert.AreEqual(TerrainClass.Ramp, T.ClassAt(9.84375f, 5.68359375f, 0), "R0 中心");
            Assert.AreEqual(TerrainClass.Ramp, T.ClassAt(0f, -11.3671875f, 0), "R5 中心");
            Assert.AreEqual(TerrainClass.Plain, T.ClassAt(19f, 19f, 0));
            Assert.AreEqual(TerrainClass.Cliff, T.ClassAt(4.59375f, 2.0703125f, 0), "G0 落點");
            Assert.AreEqual(TerrainClass.Canyon, T.ClassAt(2.09375f, 2.0625f, 0), "G0 踏點");
        }

        [Test]
        public void V14A04_WalkNeighbors_TwentyTwoUndirectedEdges_Symmetric()
        {
            int[][] expected =
            {
                new[] { 1, 4 }, new[] { 0, 7 }, new[] { 3, 9 }, new[] { 2, 11 }, new[] { 0, 13 }, new[] { 6, 15 }, new[] { 5, 17 },
                new[] { 1, 8, 18 }, new[] { 7, 9 }, new[] { 2, 8, 10 }, new[] { 9, 11 }, new[] { 3, 10, 12 }, new[] { 11, 13 },
                new[] { 4, 12, 14 }, new[] { 13, 15 }, new[] { 5, 14, 16 }, new[] { 15, 17 }, new[] { 6, 16, 18 }, new[] { 7, 17 },
            };
            int total = 0;
            for (int i = 0; i < 19; i++)
            {
                Assert.AreEqual(expected[i].Length, T.WalkNeighborCount(i), "tile " + i);
                for (int k = 0; k < expected[i].Length; k++) Assert.AreEqual(expected[i][k], T.WalkNeighbor(i, k), "tile " + i + " k " + k);
                total += T.WalkNeighborCount(i);
            }
            Assert.AreEqual(44, total);
            for (int i = 0; i < 19; i++)
            {
                for (int k = 0; k < T.WalkNeighborCount(i); k++)
                {
                    int j = T.WalkNeighbor(i, k);
                    bool back = false;
                    for (int m = 0; m < T.WalkNeighborCount(j); m++) back |= T.WalkNeighbor(j, m) == i;
                    Assert.IsTrue(back, "雙向：" + i + "→" + j);
                }
            }
        }

        private static int[] BfsSteps(int start, int skipA, int skipB)
        {
            int[] depth = new int[19];
            for (int i = 0; i < 19; i++) depth[i] = -1;
            int[] queue = new int[19];
            int head = 0, tail = 0;
            depth[start] = 0;
            queue[tail++] = start;
            while (head < tail)
            {
                int cur = queue[head++];
                for (int k = 0; k < T.WalkNeighborCount(cur); k++)
                {
                    int nb = T.WalkNeighbor(cur, k);
                    if ((cur == skipA && nb == skipB) || (cur == skipB && nb == skipA)) continue;
                    if (depth[nb] >= 0) continue;
                    depth[nb] = depth[cur] + 1;
                    queue[tail++] = nb;
                }
            }
            return depth;
        }

        [Test]
        public void V14A05_TileLevelReachability_BfsStepsFromBothMothers_AndNoBridgeEdge()
        {
            int[] from13 = { 2, 3, 4, 3, 1, 3, 4, 4, 5, 4, 3, 2, 1, 0, 1, 2, 3, 4, 5 };
            int[] from7 = { 2, 1, 3, 4, 3, 4, 3, 0, 1, 2, 3, 4, 5, 4, 5, 4, 3, 2, 1 };
            int[] d13 = BfsSteps(13, -1, -1);
            for (int i = 0; i < 19; i++) Assert.AreEqual(from13[i], d13[i], "從 13 到 " + i + " 的步數");
            int[] d7 = BfsSteps(7, -1, -1);
            for (int i = 0; i < 19; i++) Assert.AreEqual(from7[i], d7[i], "從 7 到 " + i + " 的步數");

            int removals = 0;
            for (int a = 0; a < 19; a++)
            {
                for (int k = 0; k < T.WalkNeighborCount(a); k++)
                {
                    int b = T.WalkNeighbor(a, k);
                    if (b < a) continue;
                    int[] d = BfsSteps(13, a, b);
                    // 活性：這次 BFS 用的圖確實少了一條邊（BfsSteps 以同一個 skip 條件略過 a|b）。
                    int usable = 0;
                    for (int i = 0; i < 19; i++)
                        for (int m = 0; m < T.WalkNeighborCount(i); m++)
                        {
                            int j = T.WalkNeighbor(i, m);
                            if (j > i && !((i == a && j == b) || (i == b && j == a))) usable++;
                        }
                    Assert.AreEqual(21, usable, "活性：拿掉一條後剩 21 條");
                    for (int i = 0; i < 19; i++) Assert.GreaterOrEqual(d[i], 0, "拿掉 " + a + "|" + b + " 後 " + i + " 仍可達");
                    removals++;
                }
            }
            Assert.AreEqual(22, removals);
        }

        [Test]
        public void V14A06_CliffSegments_FortyFour_ByKind_EdgesOpeningsAndSideWalls()
        {
            Assert.AreEqual(44, T.CliffSegmentCount);
            int full = 0, ends = 0, walls = 0;
            for (int i = 0; i < T.CliffSegmentCount; i++)
            {
                switch (T.GetCliffSegmentKind(i))
                {
                    case CliffSegmentKind.FullEdge: full++; break;
                    case CliffSegmentKind.RampEdgeEnd: ends++; break;
                    case CliffSegmentKind.RampSideWall: walls++; break;
                }
            }
            Assert.AreEqual(20, full);
            Assert.AreEqual(12, ends);
            Assert.AreEqual(12, walls);

            // 20 段完整崖壁：端點集合＝§2.3 的 20 組共用邊（雙射，端點順序不拘）。
            bool[] used = new bool[T.CliffSegmentCount];
            for (int p = 0; p < 20; p++)
            {
                CanyonKit.SharedEdge(CanyonKit.FullCliffPairs[p, 0], CanyonKit.FullCliffPairs[p, 1],
                                     out float ex0, out float ez0, out float ex1, out float ez1);
                int matches = 0;
                for (int i = 0; i < T.CliffSegmentCount; i++)
                {
                    if (T.GetCliffSegmentKind(i) != CliffSegmentKind.FullEdge) continue;
                    T.GetCliffSegment(i, out float x0, out float z0, out float x1, out float z1);
                    bool same = Near(x0, ex0) && Near(z0, ez0) && Near(x1, ex1) && Near(z1, ez1);
                    bool swapped = Near(x0, ex1) && Near(z0, ez1) && Near(x1, ex0) && Near(z1, ez0);
                    if (!same && !swapped) continue;
                    Assert.IsFalse(used[i], "同一段被兩組共用邊對到");
                    used[i] = true;
                    matches++;
                }
                Assert.AreEqual(1, matches, "完整崖壁 " + CanyonKit.FullCliffPairs[p, 0] + "|" + CanyonKit.FullCliffPairs[p, 1]);
            }

            for (int r = 0; r < 6; r++)
            {
                int lo = CanyonKit.RampLow[r], hi = CanyonKit.RampHigh[r];
                CanyonKit.SharedEdge(lo, hi, out float px, out float pz, out float qx, out float qz);
                double edgeLen = CanyonKit.Dist(px, pz, qx, qz);
                double ux = (qx - px) / edgeLen, uz = (qz - pz) / edgeLen;

                // 斜坡邊端：兩段都在共用邊上，各有一端是共用邊頂點。
                double[] pieceLen = new double[2];
                double[] innerX = new double[2], innerZ = new double[2];
                int n = 0;
                for (int i = 0; i < T.CliffSegmentCount; i++)
                {
                    if (T.GetCliffSegmentKind(i) != CliffSegmentKind.RampEdgeEnd) continue;
                    T.GetCliffSegment(i, out float x0, out float z0, out float x1, out float z1);
                    if (Math.Abs((x0 - px) * uz - (z0 - pz) * ux) > 1e-4 || Math.Abs((x1 - px) * uz - (z1 - pz) * ux) > 1e-4) continue;
                    double t0 = (x0 - px) * ux + (z0 - pz) * uz, t1 = (x1 - px) * ux + (z1 - pz) * uz;
                    if (t0 < -1e-4 || t0 > edgeLen + 1e-4 || t1 < -1e-4 || t1 > edgeLen + 1e-4) continue;
                    Assert.Less(n, 2, "斜坡 " + r + " 的邊端超過 2 段");
                    pieceLen[n] = CanyonKit.Dist(x0, z0, x1, z1);
                    bool firstIsVertex = (Near(x0, px) && Near(z0, pz)) || (Near(x0, qx) && Near(z0, qz));
                    bool secondIsVertex = (Near(x1, px) && Near(z1, pz)) || (Near(x1, qx) && Near(z1, qz));
                    Assert.IsTrue(firstIsVertex ^ secondIsVertex, "邊端恰有一端是共用邊頂點（斜坡 " + r + "）");
                    innerX[n] = firstIsVertex ? x1 : x0;
                    innerZ[n] = firstIsVertex ? z1 : z0;
                    n++;
                }
                Assert.AreEqual(2, n, "斜坡 " + r + " 邊端段數");
                Assert.AreEqual(pieceLen[0], pieceLen[1], 1e-5, "兩段邊端等長（斜坡 " + r + "）");
                double opening = CanyonKit.Dist(innerX[0], innerZ[0], innerX[1], innerZ[1]);
                Assert.AreEqual(3.5, opening, 1e-5, "開口寬（斜坡 " + r + "）");
                Assert.AreEqual(CanyonKit.RampCx[r], (innerX[0] + innerX[1]) * 0.5, 1e-5, "開口中點 x＝斜坡中心（斜坡 " + r + "）");
                Assert.AreEqual(CanyonKit.RampCz[r], (innerZ[0] + innerZ[1]) * 0.5, 1e-5, "開口中點 z＝斜坡中心（斜坡 " + r + "）");
                Assert.AreEqual(edgeLen, pieceLen[0] + pieceLen[1] + opening, 1e-5, "邊端＋開口＝共用邊實長（斜坡 " + r + "）");

                // 側牆：兩段，長 4、與長軸平行、距長軸 1.75。
                double axLen = CanyonKit.Dist(CanyonKit.Xs[lo], CanyonKit.Zs[lo], CanyonKit.Xs[hi], CanyonKit.Zs[hi]);
                double ax = (CanyonKit.Xs[hi] - CanyonKit.Xs[lo]) / axLen, az = (CanyonKit.Zs[hi] - CanyonKit.Zs[lo]) / axLen;
                int w = 0;
                for (int i = 0; i < T.CliffSegmentCount; i++)
                {
                    if (T.GetCliffSegmentKind(i) != CliffSegmentKind.RampSideWall) continue;
                    T.GetCliffSegment(i, out float x0, out float z0, out float x1, out float z1);
                    double mx = (x0 + x1) * 0.5, mz = (z0 + z1) * 0.5;
                    if (Math.Abs(CanyonKit.Dist(mx, mz, CanyonKit.RampCx[r], CanyonKit.RampCz[r]) - 1.75) > 1e-3) continue;
                    double len = CanyonKit.Dist(x0, z0, x1, z1);
                    Assert.AreEqual(4.0, len, 1e-5, "側牆長（斜坡 " + r + "）");
                    double cross = ((x1 - x0) / len) * az - ((z1 - z0) / len) * ax;
                    Assert.AreEqual(0.0, cross, 1e-5, "側牆與長軸平行（斜坡 " + r + "）");
                    double d0 = Math.Abs((x0 - CanyonKit.RampCx[r]) * az - (z0 - CanyonKit.RampCz[r]) * ax);
                    double d1 = Math.Abs((x1 - CanyonKit.RampCx[r]) * az - (z1 - CanyonKit.RampCz[r]) * ax);
                    Assert.AreEqual(1.75, d0, 1e-5, "側牆距長軸（斜坡 " + r + "）");
                    Assert.AreEqual(1.75, d1, 1e-5, "側牆距長軸（斜坡 " + r + "）");
                    w++;
                }
                Assert.AreEqual(2, w, "斜坡 " + r + " 側牆段數");
            }
        }

        [Test]
        public void V14A07_CliffStamping_764Cells_KeyCellsFreeOrBlocked_AndFullyReversible()
        {
            var grid = new BlockGrid(-20f, -20f, 0.5f, 80, 80);
            T.StampCliffs(grid, 0.35f, +1);
            Assert.AreEqual(764, grid.BlockedCount);
            for (int i = 0; i < 19; i++) Assert.IsFalse(CanyonKit.BlockedAt(grid, CanyonKit.Xs[i], CanyonKit.Zs[i]), "塔心 " + i);
            for (int r = 0; r < 6; r++) Assert.IsFalse(CanyonKit.BlockedAt(grid, CanyonKit.RampCx[r], CanyonKit.RampCz[r]), "斜坡中心 " + r);
            Assert.IsFalse(CanyonKit.BlockedAt(grid, 2.09375f, 2.0625f), "G0 踏點");
            Assert.IsFalse(CanyonKit.BlockedAt(grid, -2.09375f, -2.0625f), "G1 踏點");
            Assert.IsFalse(CanyonKit.BlockedAt(grid, 4.59375f, 2.0703125f), "G0 落點");
            Assert.IsFalse(CanyonKit.BlockedAt(grid, -4.59375f, -2.0703125f), "G1 落點");
            for (int p = 0; p < 20; p++)
            {
                CanyonKit.SharedEdge(CanyonKit.FullCliffPairs[p, 0], CanyonKit.FullCliffPairs[p, 1],
                                     out float x0, out float z0, out float x1, out float z1);
                Assert.IsTrue(CanyonKit.BlockedAt(grid, (x0 + x1) * 0.5f, (z0 + z1) * 0.5f),
                              "完整崖壁中點 " + CanyonKit.FullCliffPairs[p, 0] + "|" + CanyonKit.FullCliffPairs[p, 1]);
            }
            T.StampCliffs(grid, 0.35f, -1);
            Assert.AreEqual(0, grid.BlockedCount);
            Assert.AreEqual(0, grid.NegativeStampCount);
        }

        private static BlockGrid NewArenaWithCliffs()
        {
            BlockGrid grid = CanyonKit.NewArenaGrid();
            T.StampCliffs(grid, 0.35f, +1);
            return grid;
        }

        [Test]
        public void V14A08_GridReachability_RealGridNavigator_AllTowerCentresFromBothSpawns()
        {
            BlockGrid grid = NewArenaWithCliffs();
            var nav = new GridNavigator(grid, new NavGridTuning());
            float[] spawnZ = { -16.65625f, 16.65625f };
            for (int s = 0; s < 2; s++)
            {
                for (int i = 0; i < 19; i++)
                {
                    nav.ResolveGoal(0f, spawnZ[s], CanyonKit.Xs[i], CanyonKit.Zs[i], out float gx, out float gz, out bool substituted);
                    Assert.IsFalse(substituted, "出生點 z=" + spawnZ[s] + " → 塔心 " + i + " 應走得到");
                }
            }

            Assert.IsFalse(grid.HasLineOfSight(0f, 0f, 6.5625f, 3.7890625f), "谷心對 2 號塔心沒有視線");
            for (int r = 0; r < 6; r++)
            {
                int lo = CanyonKit.RampLow[r], hi = CanyonKit.RampHigh[r];
                double len = CanyonKit.Dist(CanyonKit.Xs[lo], CanyonKit.Zs[lo], CanyonKit.Xs[hi], CanyonKit.Zs[hi]);
                float ax = (float)((CanyonKit.Xs[hi] - CanyonKit.Xs[lo]) / len), az = (float)((CanyonKit.Zs[hi] - CanyonKit.Zs[lo]) / len);
                float x0 = CanyonKit.RampCx[r] - ax * 1.75f, z0 = CanyonKit.RampCz[r] - az * 1.75f;
                float x1 = CanyonKit.RampCx[r] + ax * 1.75f, z1 = CanyonKit.RampCz[r] + az * 1.75f;
                Assert.IsTrue(grid.HasLineOfSight(x0, z0, x1, z1), "沿斜坡 " + r + " 長軸有視線");
            }

            var field = new FlowField(grid);
            grid.TryWorldToCell(6.5625f, 3.7890625f, out int gcx, out int gcz);
            field.Build(gcx, gcz);
            grid.TryWorldToCell(0f, 0f, out int ocx, out int ocz);
            int cost = field.CostAt(ocx, ocz);
            Assert.GreaterOrEqual(cost, 600, "谷心到 2 號要繞路");
            Assert.Less(cost, int.MaxValue, "谷心到 2 號走得到");
        }

        [Test]
        public void V14A09_HeightContinuity_EveryWalkableNeighbourPair_WithinSlopeQuarter()
        {
            BlockGrid grid = NewArenaWithCliffs();
            int[] ndx = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] ndz = { 0, 0, 1, -1, 1, -1, 1, -1 };
            int violations = 0, slopedPairs = 0;
            bool low = false, mid = false, high = false;
            string first = null;
            for (int cz = 0; cz < 80; cz++)
            {
                for (int cx = 0; cx < 80; cx++)
                {
                    if (grid.IsBlocked(cx, cz)) continue;
                    grid.CellCenter(cx, cz, out float x, out float z);
                    float h = T.HeightAt(x, z, 0);
                    if (h == -1f) low = true;
                    if (h == 0f) mid = true;
                    if (h == 1f) high = true;
                    for (int n = 0; n < 8; n++)
                    {
                        int nx = cx + ndx[n], nz = cz + ndz[n];
                        if (grid.IsBlocked(nx, nz)) continue;
                        if (ndx[n] != 0 && ndz[n] != 0 && (grid.IsBlocked(cx + ndx[n], cz) || grid.IsBlocked(cx, cz + ndz[n]))) continue;
                        grid.CellCenter(nx, nz, out float x2, out float z2);
                        float h2 = T.HeightAt(x2, z2, 0);
                        double dist = CanyonKit.Dist(x, z, x2, z2);
                        double dh = Math.Abs(h - h2);
                        if (dh != 0.0) slopedPairs++;
                        if (dh > 0.25 * dist + 1e-5)
                        {
                            violations++;
                            if (first == null) first = "(" + x + "," + z + ")h=" + h + " vs (" + x2 + "," + z2 + ")h=" + h2;
                        }
                    }
                }
            }
            Assert.AreEqual(0, violations, "第一組違反：" + first);
            Assert.GreaterOrEqual(slopedPairs, 100, "活性：斜坡上確實有連續爬升");
            Assert.IsTrue(low && mid && high, "活性：三種高度的空格都存在");
        }

        [Test]
        public void V14A15_BehemothWalkRoute_BfsSmallestIndexParent_OnlyWalkEdges()
        {
            int[] buffer = new int[19];
            AssertRoute(buffer, 0, 13, new[] { 0, 4, 13 });
            AssertRoute(buffer, 0, 7, new[] { 0, 1, 7 });
            AssertRoute(buffer, 2, 13, new[] { 2, 3, 11, 12, 13 });
            AssertRoute(buffer, 6, 7, new[] { 6, 17, 18, 7 });
        }

        private static void AssertRoute(int[] buffer, int start, int dest, int[] expected)
        {
            int n = T.FindWalkRoute(start, dest, buffer);
            string got = "";
            for (int i = 0; i < n; i++) got += (i > 0 ? "," : "") + buffer[i];
            Assert.AreEqual(expected.Length, n, start + "→" + dest + " 得 [" + got + "]");
            for (int i = 0; i < n; i++) Assert.AreEqual(expected[i], buffer[i], start + "→" + dest + " 得 [" + got + "]");
            for (int i = 0; i + 1 < n; i++)
            {
                bool adjacent = false;
                for (int k = 0; k < T.WalkNeighborCount(buffer[i]); k++) adjacent |= T.WalkNeighbor(buffer[i], k) == buffer[i + 1];
                Assert.IsTrue(adjacent, "每一步都在 WalkNeighbor 內");
            }
        }

        [Test]
        public void V14A16_SameFloor_CrossesCliffAndCliffCell_AndSameFloorEjectSearch()
        {
            Assert.IsTrue(T.IsSameFloor(0f, 0f, 0, 0f, -7.578125f, 0), "谷底對谷底");
            Assert.IsFalse(T.IsSameFloor(3.5f, 0f, 0, 5.0f, 0f, 0), "谷底對崖台（跨崖壁線）");
            Assert.IsTrue(T.IsSameFloor(0f, -9.3671875f, 0, 0f, -13.3671875f, 0), "R5 低端對高端");
            Assert.IsFalse(T.IsSameFloor(1.7421875f, -10.5f, 0, 1.7578125f, -10.5f, 0), "坡面對側邊外地面");
            Assert.IsTrue(T.IsSameFloor(3.5f, 0.5f, 0, 0f, 0f, 0), "(3.5,0.5) 距 0|2 0.5078，不是崖壁格");
            Assert.IsFalse(T.IsSameFloor(1.7421875f, -10.5f, 0, 1.5f, -10.5f, 0), "端點在崖壁格（距 R5 側牆 0.0078）");

            BlockGrid grid = NewArenaWithCliffs();
            grid.StampBox(3.625f, 0f, 1f, 0f, 2f, 0.3f, 0.35f, +1);
            Assert.IsTrue(grid.TryFindNearestFreeSameFloor(3.875f, 0f, 20, T, 0, out int cx, out int cz), "有條件搜尋找得到格子");
            grid.CellCenter(cx, cz, out float fx, out float fz);
            Assert.AreEqual(-1f, T.HeightAt(fx, fz, 0), 0f, "有條件：格心 (" + fx + "," + fz + ") 在谷底");
            Assert.IsFalse(T.CrossesCliff(3.875f, 0f, fx, fz), "有條件：不穿崖 (" + fx + "," + fz + ")");

            Assert.IsTrue(grid.TryFindNearestFree(3.875f, 0f, 20, out int ux, out int uz));
            grid.CellCenter(ux, uz, out float gx, out float gz);
            Assert.AreEqual(1f, T.HeightAt(gx, gz, 0), 0f, "活性：無條件搜尋的格心 (" + gx + "," + gz + ") 在崖台");
        }

        [Test]
        public void V14A22_ArbitraryDropRamp_TwoMetreTestRamp_InterpolatesAndWallsFromData()
        {
            float[] heights = { -1f, -1f, 1f, 1f, -1f, 1f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
            var t7 = new CanyonTerrainSpec(CaptureBoardSpec.V0140Canyon, heights,
                                           new[] { 9, 11, 15, 17, 1, 4, 0 }, new[] { 2, 3, 5, 6, 7, 13, 3 });
            Assert.AreEqual(0f, t7.HeightAt(3.28125f, -1.8945312f, 0), 1e-5f, "第 7 條斜坡中心");
            Assert.AreEqual(-0.5f, t7.HeightAt(2.415236f, -1.3945113f, 0), 1e-5f, "沿軸 −1m");
            Assert.AreEqual(0.5f, t7.HeightAt(4.147264f, -2.3945513f, 0), 1e-5f, "沿軸 +1m");
            Assert.AreEqual(TerrainClass.Ramp, t7.ClassAt(3.28125f, -1.8945312f, 0));
            Assert.IsTrue(t7.IsSameFloor(1.7657257f, -1.0194964f, 0, 4.7967744f, -2.769566f, 0), "沿軸 ±1.75 同樓地板");
            Assert.IsFalse(t7.IsSameFloor(2.415236f, -1.3945113f, 0, 1.0401813f, -3.7760496f, 0), "坡面對側牆外谷底");

            Assert.IsTrue(t7.CrossesCliff(2.415236f, -1.3945113f, 1.0401813f, -3.7760496f), "活性：第 7 條側牆有反應");
            Assert.AreEqual(-1f, T.HeightAt(3.28125f, -1.8945312f, 0), 0f, "活性：本批 V0140 在該點是谷底");
            Assert.IsTrue(T.CrossesCliff(1.7657257f, -1.0194964f, 4.7967744f, -2.769566f), "活性：本批 V0140 該對點穿 0|3 崖壁");
        }

        private static bool Near(float a, float b) => Math.Abs(a - b) <= 1e-5f;
    }
}
