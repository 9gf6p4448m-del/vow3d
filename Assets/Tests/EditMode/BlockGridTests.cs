using System;
using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // 0.5m 阻擋網格（PHASE2_BATCH2_PLAN.md §5 V2 a～e、n）。全部用 -20/-20 原點、80×80、格寬 0.5 的網格，
    // 與 NavGridTuning 的預設值一致。
    public sealed class BlockGridTests
    {
        private static BlockGrid NewGrid() => new BlockGrid(-20f, -20f, 0.5f, 80, 80);

        [Test]
        public void RegistrationAnchor_BoxSpansExpectedCells_AndRotatesWithNormal()
        {
            // a. 牆心 (0,0)、法線 (0,1)、半寬 2、半厚 0.3、外擴 0.35 → x 格 35~44、z 格 38~41，共 40 格。
            BlockGrid grid = NewGrid();
            grid.StampBox(0f, 0f, 0f, 1f, 2f, 0.3f, 0.35f, 1);

            Assert.AreEqual(40, grid.BlockedCount);
            for (int cx = 35; cx <= 44; cx++)
            for (int cz = 38; cz <= 41; cz++)
                Assert.IsTrue(grid.IsBlocked(cx, cz), $"({cx},{cz}) 應在牆的登記範圍內");

            // 四個角格
            Assert.IsTrue(grid.IsBlocked(35, 38));
            Assert.IsTrue(grid.IsBlocked(35, 41));
            Assert.IsTrue(grid.IsBlocked(44, 38));
            Assert.IsTrue(grid.IsBlocked(44, 41));

            // 緊鄰外圈：一格都不該被登記
            Assert.IsFalse(grid.IsBlocked(34, 38));
            Assert.IsFalse(grid.IsBlocked(45, 38));
            Assert.IsFalse(grid.IsBlocked(35, 37));
            Assert.IsFalse(grid.IsBlocked(35, 42));
            Assert.IsFalse(grid.IsBlocked(44, 37));
            Assert.IsFalse(grid.IsBlocked(44, 42));

            // 法線改 (1,0)：仍 40 格，但寬厚軸互換 —— x 格 38~41、z 格 35~44。
            BlockGrid rotated = NewGrid();
            rotated.StampBox(0f, 0f, 1f, 0f, 2f, 0.3f, 0.35f, 1);
            Assert.AreEqual(40, rotated.BlockedCount);
            for (int cx = 38; cx <= 41; cx++)
            for (int cz = 35; cz <= 44; cz++)
                Assert.IsTrue(rotated.IsBlocked(cx, cz));
            Assert.IsFalse(rotated.IsBlocked(37, 35));
            Assert.IsFalse(rotated.IsBlocked(42, 35));
        }

        // r1 對抗審查 M1（§6 R6）：撤銷不對稱時計數必須夾在 0 並回報，不得留下「存在但不擋路」的負計數格。
        // 修復前實跑：+1、−1、−1 之後看起來正常（BlockedCount=0），但下一面**真的牆** +1 上去仍然是
        // 「不擋路、Version 不翻轉」——壞了不會叫。
        // §6 R9：DDA 走訪在「線段兩端點都恰好落在格角」時，有可能永遠對不上終點格而一路走出格點外
        // （界外一律 Blocked → 空場也被判成沒有視線）。這是實作者在第一輪修復中發現並修掉的缺陷，
        // 不在 r1 findings 之列，所以補一條直接的回歸測試：空格點上這些線段（與其反向）一律有視線。
        [Test]
        public void LineOfSight_OnAnEmptyGrid_CornerAlignedSegments_AreAlwaysClear()
        {
            BlockGrid grid = NewGrid(); // 原點 −20、格寬 0.5：整數與 .5 座標都恰好落在格角上
            float[,] segments =
            {
                { 0f, 0f, 10f, 10f },      // 45°，每一步都精準命中格角
                { -5f, 3f, 7f, 3f },       // 水平
                { 2f, -8f, 2f, 9f },       // 垂直
                { 0f, 0f, -10f, 9f },      // 非 45° 的斜線（第一輪實測就是這一族在修復前誤判）
                { -6f, -4f, 9f, 8f },
            };

            for (int i = 0; i < segments.GetLength(0); i++)
            {
                float x0 = segments[i, 0], z0 = segments[i, 1], x1 = segments[i, 2], z1 = segments[i, 3];
                Assert.IsTrue(grid.HasLineOfSight(x0, z0, x1, z1),
                    $"空格點上 ({x0},{z0})→({x1},{z1}) 必須有視線");
                Assert.IsTrue(grid.HasLineOfSight(x1, z1, x0, z0),
                    $"空格點上 ({x1},{z1})→({x0},{z0})（反向）必須有視線");
            }
        }

        // §6 R9 的另一半：提前跳出之後，終點格自己仍然要被檢查過。
        [Test]
        public void LineOfSight_WithTheDestinationCellBlocked_IsBlocked_EvenWhenBothEndsSitOnCellCorners()
        {
            BlockGrid grid = NewGrid();
            grid.TryWorldToCell(-10f, 9f, out int destCx, out int destCz);
            grid.CellCenter(destCx, destCz, out float bx, out float bz);
            grid.StampBox(bx, bz, 1f, 0f, 0.24f, 0.24f, 0f, 1); // 嚴格小於半格：只封鎖終點那一格
            Assert.AreEqual(1, grid.BlockedCount, "前置條件：只封鎖了終點格");

            Assert.IsFalse(grid.HasLineOfSight(0f, 0f, -10f, 9f), "終點格被擋住時不得回報有視線");
        }

        // §6 R2：界外的目的地要夾進格點再照常解析（夾到最外圈格心，TryWorldToCell 必定成功）。
        [Test]
        public void ClampToGrid_BringsOutOfRangePointsBackOntoTheGrid_AndLeavesInsidePointsAlone()
        {
            BlockGrid grid = NewGrid(); // 原點 −20、格寬 0.5、80×80 → 格心範圍 [−19.75, 19.75]

            grid.ClampToGrid(0f, 20f, out float x1, out float z1);
            Assert.AreEqual(0f, x1, 1e-5f);
            Assert.AreEqual(19.75f, z1, 1e-5f);
            Assert.IsTrue(grid.TryWorldToCell(x1, z1, out _, out _), "夾完必須落在格點內");

            grid.ClampToGrid(-25f, -1000f, out float x2, out float z2);
            Assert.AreEqual(-19.75f, x2, 1e-5f);
            Assert.AreEqual(-19.75f, z2, 1e-5f);
            Assert.IsTrue(grid.TryWorldToCell(x2, z2, out _, out _));

            grid.ClampToGrid(3.14f, -7.5f, out float x3, out float z3);
            Assert.AreEqual(3.14f, x3, 1e-5f, "界內的點不得被動到");
            Assert.AreEqual(-7.5f, z3, 1e-5f, "界內的點不得被動到");
        }

        [Test]
        public void StampBox_NegativeReferenceCounts_AreClampedAtZero_AndReported()
        {
            BlockGrid grid = NewGrid();
            const float cx = 0f, cz = 0f, nx = 0f, nz = 1f, halfWidth = 2f, halfThickness = 0.3f, inflate = 0.35f;

            grid.StampBox(cx, cz, nx, nz, halfWidth, halfThickness, inflate, 1);
            grid.StampBox(cx, cz, nx, nz, halfWidth, halfThickness, inflate, -1);
            Assert.AreEqual(0, grid.BlockedCount);
            Assert.AreEqual(0, grid.NegativeStampCount, "到這裡為止都是對稱的，不該記到任何負計數");

            grid.StampBox(cx, cz, nx, nz, halfWidth, halfThickness, inflate, -1); // 多撤了一次
            Assert.AreEqual(0, grid.BlockedCount);
            Assert.AreEqual(40, grid.NegativeStampCount, "多撤的那一次應該被夾回 0 並逐格記一筆");

            int versionBefore = grid.Version;
            grid.StampBox(cx, cz, nx, nz, halfWidth, halfThickness, inflate, 1); // 下一面真的牆
            Assert.AreEqual(40, grid.BlockedCount, "計數被夾回 0 之後，下一面真的牆必須照樣擋路");
            Assert.IsTrue(grid.IsBlocked(40, 40), "牆心那格必須是 Blocked");
            Assert.Greater(grid.Version, versionBefore, "格子翻轉了，Version 必須跟著動（否則整合場不會重建）");
        }

        [Test]
        public void FortyFiveDegreeWall_TangentEndpointsBlocked_PerpendicularOffsetNot()
        {
            // b. 法線 45°（(√2/2, -√2/2)）時，牆沿切線方向的兩端點所在格 Blocked；
            // 若整片牆是「沒轉的軸對齊包圍盒」，切線方向 2m 外沿法線方向再偏移的點理論上也會落在包圍盒內，
            // 但真正的旋轉矩形不涵蓋那裡 —— 這就是本測試要抓的差異。
            const float inv = 0.70710678f;
            BlockGrid grid = NewGrid();
            grid.StampBox(0f, 0f, inv, -inv, 2f, 0.3f, 0f, 1);

            grid.TryWorldToCell(1.41421356f, 1.41421356f, out int e1x, out int e1z);
            grid.TryWorldToCell(-1.41421356f, -1.41421356f, out int e2x, out int e2z);
            Assert.IsTrue(grid.IsBlocked(e1x, e1z), "牆的切線端點所在格必須 Blocked");
            Assert.IsTrue(grid.IsBlocked(e2x, e2z), "牆的切線端點所在格必須 Blocked");

            grid.TryWorldToCell(1.41421356f, -1.41421356f, out int px, out int pz);
            Assert.IsFalse(grid.IsBlocked(px, pz), "垂直（法線）方向 2m 外不在旋轉矩形內，不該被誤判成軸對齊包圍盒命中");
        }

        [Test]
        public void ReferenceCounting_OverlapRelease_VersionOnlyFlipsOnTransition()
        {
            // c. 兩面重疊的牆（用同一組參數登記兩次模擬）：撤一面仍 Blocked，都撤才清空；
            // Version 只在「格子翻轉」時 +1，同一組參數再登記一次不算翻轉。
            BlockGrid grid = NewGrid();
            const float cx = 0f, cz = 0f, nx = 0f, nz = 1f, halfWidth = 1.5f, halfThickness = 0.3f, inflate = 0.35f;

            grid.StampBox(cx, cz, nx, nz, halfWidth, halfThickness, inflate, 1);
            int versionAfterFirst = grid.Version;
            Assert.Greater(versionAfterFirst, 0, "從全空到有牆，至少有格子翻轉");
            grid.TryWorldToCell(cx, cz, out int probeCx, out int probeCz);
            Assert.IsTrue(grid.IsBlocked(probeCx, probeCz));

            grid.StampBox(cx, cz, nx, nz, halfWidth, halfThickness, inflate, 1); // 第二面牆，完全重疊
            Assert.AreEqual(versionAfterFirst, grid.Version, "重疊格從 1→2，沒有翻轉，Version 不變");
            Assert.IsTrue(grid.IsBlocked(probeCx, probeCz));

            grid.StampBox(cx, cz, nx, nz, halfWidth, halfThickness, inflate, -1); // 撤一面
            Assert.AreEqual(versionAfterFirst, grid.Version, "重疊格從 2→1，沒有翻轉，Version 不變");
            Assert.IsTrue(grid.IsBlocked(probeCx, probeCz), "還有一面牆在，格子仍要是 Blocked");

            grid.StampBox(cx, cz, nx, nz, halfWidth, halfThickness, inflate, -1); // 都撤
            Assert.Greater(grid.Version, versionAfterFirst, "1→0 是翻轉，Version 要再 +1");
            Assert.AreEqual(0, grid.BlockedCount);
        }

        [Test]
        public void StampBoxNearBoundary_DoesNotThrow_PartialRegistration_AndOutOfRangeIsBlocked()
        {
            // d. 牆心在界外邊緣：不丟例外，界內部分有登記；界外座標 IsBlocked 一律 true。
            BlockGrid grid = NewGrid();
            Assert.DoesNotThrow(() => grid.StampBox(19.9f, 0f, 1f, 0f, 2f, 0.3f, 0.35f, 1));
            Assert.Greater(grid.BlockedCount, 0, "界內部分應該有登記");

            Assert.IsTrue(grid.IsBlocked(-1, 0));
            Assert.IsTrue(grid.IsBlocked(80, 0));
        }

        [Test]
        public void LineOfSight_NoWallIsClear()
        {
            BlockGrid grid = NewGrid();
            Assert.IsTrue(grid.HasLineOfSight(-5f, -5f, 5f, 5f));
        }

        [Test]
        public void LineOfSight_CrossingAWallIsBlocked()
        {
            BlockGrid grid = NewGrid();
            grid.StampBox(5f, 5f, 0f, 1f, 5f, 1f, 0f, 1); // x∈[0,10], z∈[4,6] 的橫向厚牆
            Assert.IsFalse(grid.HasLineOfSight(0f, 5f, 10f, 5f));
        }

        [Test]
        public void LineOfSight_GrazingOnlyTheCornerOfABlockedCell_IsBlocked()
        {
            // e. supercover：45° 直線精準穿過格角時，「只在對角處被擦到」的那兩個非路徑格也算經過。
            // 路線從 (0.1,0.1) 到 (2.1,2.1)：每一步都精準命中格角，第一個角落 (1,1) 的其中一個
            // 「只被擦到」的鄰格是 (42,41)（cell center (1.25,0.75)），這格若被 Blocked，視線必須是 false。
            // 純 Bresenham（不處理 supercover）會直接跳過這格，因而錯判成 true。
            BlockGrid grid = NewGrid();
            grid.CellCenter(42, 41, out float bx, out float bz);
            grid.StampBox(bx, bz, 1f, 0f, 0.24f, 0.24f, 0f, 1); // 嚴格小於半格，保證只封鎖這一格

            Assert.AreEqual(1, grid.BlockedCount, "前置條件：只封鎖了那一格擦角格");
            Assert.IsFalse(grid.HasLineOfSight(0.1f, 0.1f, 2.1f, 2.1f),
                "45° 直線精準擦過的格角格若被擋，視線必須判定為被擋（supercover）");
        }

        [Test]
        public void LineOfSight_SameCell_IsAlwaysClearEvenIfBlocked()
        {
            // 兩端點同格：trivially true —— 即使那一格本身是 Blocked。
            BlockGrid grid = NewGrid();
            grid.CellCenter(40, 40, out float bx, out float bz);
            grid.StampBox(bx, bz, 1f, 0f, 0.24f, 0.24f, 0f, 1);
            Assert.IsTrue(grid.IsBlocked(40, 40));

            Assert.IsTrue(grid.HasLineOfSight(bx - 0.1f, bz - 0.1f, bx + 0.1f, bz + 0.1f));
        }

        [Test]
        public void TryFindNearestFree_MatchesIndependentBruteForceScan()
        {
            // n. 與獨立寫在測試裡的暴力掃描比對（不呼叫 TryFindNearestFree 本身）。
            BlockGrid grid = NewGrid();
            grid.StampBox(0f, 0f, 0f, 1f, 3f, 0.3f, 0.35f, 1);
            grid.StampBox(-5f, -5f, 1f, 0f, 2f, 0.3f, 0.35f, 1);

            const float queryX = 0.2f, queryZ = 0.1f; // 落在牆內，逼著往外找
            const int radius = 20;

            Assert.IsTrue(grid.TryFindNearestFree(queryX, queryZ, radius, out int cx, out int cz));

            grid.TryWorldToCell(queryX, queryZ, out int ocx, out int ocz);
            int minCx = Math.Max(0, ocx - radius), maxCx = Math.Min(grid.Columns - 1, ocx + radius);
            int minCz = Math.Max(0, ocz - radius), maxCz = Math.Min(grid.Rows - 1, ocz + radius);

            int expectedCx = -1, expectedCz = -1;
            double bestDistSq = double.MaxValue;
            for (int gz = minCz; gz <= maxCz; gz++)
            for (int gx = minCx; gx <= maxCx; gx++)
            {
                if (grid.IsBlocked(gx, gz)) continue;
                grid.CellCenter(gx, gz, out float ccx, out float ccz);
                double dx = ccx - queryX, dz = ccz - queryZ;
                double distSq = dx * dx + dz * dz;
                if (expectedCx != -1 && !(distSq < bestDistSq)) continue;
                bestDistSq = distSq;
                expectedCx = gx;
                expectedCz = gz;
            }

            Assert.AreEqual(expectedCx, cx);
            Assert.AreEqual(expectedCz, cz);
        }

        [Test]
        public void TryFindNearestFree_TiesBreakByIndex_SmallerIndexWins()
        {
            // n. 同距離取索引較小者：只封鎖查詢格自己，四個正交鄰格距離全部相等（一格寬）。
            // Index = cz*Columns+cx 以 cz 為主鍵，最小的是 (cx, cz-1)。
            BlockGrid grid = NewGrid();
            const int ocx = 40, ocz = 40;
            grid.CellCenter(ocx, ocz, out float qx, out float qz);
            grid.StampBox(qx, qz, 1f, 0f, 0.2f, 0.2f, 0f, 1);

            Assert.IsTrue(grid.TryFindNearestFree(qx, qz, 5, out int cx, out int cz));
            Assert.AreEqual(ocx, cx);
            Assert.AreEqual(ocz - 1, cz);
        }

        [Test]
        public void CircleOverlapsBox_InsideAndDistanceThresholds()
        {
            // n. 圓心在盒內 true；離盒邊 radius-ε true；radius+ε false。
            const float halfWidth = 1f, halfThickness = 0.5f;
            Assert.IsTrue(BlockGrid.CircleOverlapsBox(0f, 0f, 0.1f, 0f, 0f, 0f, 1f, halfWidth, halfThickness));

            const float radius = 0.2f;
            Assert.IsTrue(BlockGrid.CircleOverlapsBox(1f + radius - 0.01f, 0f, radius, 0f, 0f, 0f, 1f, halfWidth, halfThickness),
                "離盒邊 radius-ε 應仍算重疊");
            Assert.IsFalse(BlockGrid.CircleOverlapsBox(1f + radius + 0.01f, 0f, radius, 0f, 0f, 0f, 1f, halfWidth, halfThickness),
                "離盒邊 radius+ε 不該算重疊");
        }

        [Test]
        public void CircleOverlapsBox_FortyFiveDegreeCornerOutside_IsFalse()
        {
            // n. 45° 盒的角落外：正方形（halfWidth=halfThickness=1）轉 45° 後，一個角落落在世界座標 (0, √2)；
            // 沿世界 z 軸再往外 0.5m 的點，最近點就是角落本身，距離 0.5 > radius 0.3，不該重疊。
            const float inv = 0.70710678f;
            const float diag = 1.41421356f;
            Assert.IsFalse(BlockGrid.CircleOverlapsBox(0f, diag + 0.5f, 0.3f, 0f, 0f, inv, inv, 1f, 1f));
        }
    }
}
