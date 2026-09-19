using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // 整合場 Dijkstra（PHASE2_BATCH2_PLAN.md §5 V2 f、g、h）。暴力鬆弛法獨立寫在這個檔案裡，
    // 不呼叫 FlowField 本身的任何邏輯，純粹用來對照。
    public sealed class FlowFieldTests
    {
        private static BlockGrid NewGrid() => new BlockGrid(-20f, -20f, 0.5f, 80, 80);

        // 獨立的暴力鬆弛法：反覆對全場每格的 8 條邊做鬆弛，直到一整輪掃描都沒有變動為止。
        // 邊的規則（直走 10、斜走 14、斜走時兩個正交鄰格皆非 Blocked 才准）刻意跟 FlowField 分開重寫一次，
        // 不共用同一段程式碼，才有鑑別力。
        private static int[] BruteForceRelax(BlockGrid grid, int goalCx, int goalCz)
        {
            int columns = grid.Columns;
            int rows = grid.Rows;
            int[] cost = new int[columns * rows];
            for (int i = 0; i < cost.Length; i++) cost[i] = int.MaxValue;
            if (grid.IsBlocked(goalCx, goalCz)) return cost;
            cost[goalCz * columns + goalCx] = 0;

            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
            int[] stepCost = { 10, 10, 10, 10, 14, 14, 14, 14 };

            bool changed = true;
            int guard = 500;
            while (changed && guard-- > 0)
            {
                changed = false;
                for (int cz = 0; cz < rows; cz++)
                for (int cx = 0; cx < columns; cx++)
                {
                    int idx = cz * columns + cx;
                    if (cost[idx] == int.MaxValue) continue;
                    for (int n = 0; n < 8; n++)
                    {
                        int ncx = cx + dx[n];
                        int ncz = cz + dz[n];
                        if (grid.IsBlocked(ncx, ncz)) continue;
                        bool diagonal = dx[n] != 0 && dz[n] != 0;
                        if (diagonal && (grid.IsBlocked(cx + dx[n], cz) || grid.IsBlocked(cx, cz + dz[n]))) continue;

                        int nIdx = ncz * columns + ncx;
                        int newCost = cost[idx] + stepCost[n];
                        if (newCost < cost[nIdx])
                        {
                            cost[nIdx] = newCost;
                            changed = true;
                        }
                    }
                }
            }
            Assert.LessOrEqual(0, guard, "暴力鬆弛法沒有在合理輪數內收斂，測試佈局可能太複雜");
            return cost;
        }

        private static void AssertMatchesBruteForce(BlockGrid grid, int goalCx, int goalCz)
        {
            FlowField field = new FlowField(grid);
            field.Build(goalCx, goalCz);
            int[] expected = BruteForceRelax(grid, goalCx, goalCz);

            for (int cz = 0; cz < grid.Rows; cz++)
            for (int cx = 0; cx < grid.Columns; cx++)
            {
                int idx = cz * grid.Columns + cx;
                Assert.AreEqual(expected[idx], field.CostAt(cx, cz), $"({cx},{cz}) 的成本與暴力鬆弛法不一致");
                Assert.AreEqual(expected[idx] != int.MaxValue, field.IsReached(cx, cz), $"({cx},{cz}) 的可達性與暴力鬆弛法不一致");
            }
        }

        [Test]
        public void CostAt_MatchesBruteForce_SingleWallLayout()
        {
            BlockGrid grid = NewGrid();
            grid.StampBox(0f, 0f, 0f, 1f, 3f, 0.3f, 0.35f, 1);
            AssertMatchesBruteForce(grid, 60, 40);
        }

        [Test]
        public void CostAt_MatchesBruteForce_LShapeLayout()
        {
            BlockGrid grid = NewGrid();
            // L 形：一段沿 z 軸（x=0，z∈[-10,0]），一段沿 x 軸（z=0，x∈[0,10]），在 (0,0) 轉角相接。
            grid.StampBox(0f, -5f, 1f, 0f, 5f, 0.3f, 0.35f, 1);
            grid.StampBox(5f, 0f, 0f, 1f, 5f, 0.3f, 0.35f, 1);
            AssertMatchesBruteForce(grid, 60, 55);
        }

        [Test]
        public void CostAt_MatchesBruteForce_UShapeLayout_AndCostsAreWorseThanNoWall()
        {
            BlockGrid grid = NewGrid();
            // U 形陷阱：左、右、底三面牆圍出一個開口朝 +z 的杯子，(0,-3) 在杯子裡。
            grid.StampBox(-2f, -3f, 1f, 0f, 3f, 0.3f, 0.35f, 1); // 左壁（法線指向 +x，沿 z 方向延伸）
            grid.StampBox(2f, -3f, 1f, 0f, 3f, 0.3f, 0.35f, 1);  // 右壁
            grid.StampBox(0f, -6f, 0f, 1f, 2f, 0.3f, 0.35f, 1);  // 底

            // 目標放在底牆「外側」（比底牆更負的 z）：直線會直接撞底牆，非繞不可 —— 才是貨真價實的陷阱，
            // 不是「往開口方向直走就到」的假陷阱。
            const int goalCx = 40, goalCz = 20; // 世界座標約 (0, -10)
            AssertMatchesBruteForce(grid, goalCx, goalCz);

            FlowField withWall = new FlowField(grid);
            withWall.Build(goalCx, goalCz);

            BlockGrid empty = NewGrid();
            FlowField withoutWall = new FlowField(empty);
            withoutWall.Build(goalCx, goalCz);

            grid.TryWorldToCell(0f, -3f, out int insideCx, out int insideCz);
            Assert.Greater(withWall.CostAt(insideCx, insideCz), withoutWall.CostAt(insideCx, insideCz),
                "活性：杯子裡的格子繞路後成本要比沒有牆時高");
        }

        [Test]
        public void TryGetNext_WalksToGoal_WithStrictlyDecreasingCost_NeverEnteringBlockedOrCuttingCorners()
        {
            BlockGrid grid = NewGrid();
            grid.StampBox(-2f, -3f, 1f, 0f, 3f, 0.3f, 0.35f, 1);
            grid.StampBox(2f, -3f, 1f, 0f, 3f, 0.3f, 0.35f, 1);
            grid.StampBox(0f, -6f, 0f, 1f, 2f, 0.3f, 0.35f, 1);

            const int goalCx = 40, goalCz = 20; // 世界座標約 (0, -10)：在底牆外側，強迫真的繞路
            FlowField field = new FlowField(grid);
            field.Build(goalCx, goalCz);

            grid.TryWorldToCell(0f, -3f, out int cx, out int cz);
            Assert.IsTrue(field.IsReached(cx, cz), "前置條件：起點必須走得到目標");

            int previousCost = field.CostAt(cx, cz);
            int guard = grid.Columns * grid.Rows;
            while (previousCost > 0 && guard-- > 0)
            {
                Assert.IsFalse(grid.IsBlocked(cx, cz), "路徑不得踏入 Blocked 格");
                bool ok = field.TryGetNext(cx, cz, out int nx, out int nz);
                Assert.IsTrue(ok, "成本 > 0 時必須有下一步");

                int dcx = nx - cx, dcz = nz - cz;
                bool diagonal = dcx != 0 && dcz != 0;
                if (diagonal)
                {
                    Assert.IsFalse(grid.IsBlocked(cx + dcx, cz), "斜步時兩個正交鄰格皆非 Blocked");
                    Assert.IsFalse(grid.IsBlocked(cx, cz + dcz), "斜步時兩個正交鄰格皆非 Blocked");
                }

                int nextCost = field.CostAt(nx, nz);
                Assert.Less(nextCost, previousCost, "成本必須嚴格遞減");
                previousCost = nextCost;
                cx = nx;
                cz = nz;
            }

            Assert.AreEqual(0, previousCost, "應該要走到成本為 0 的目標格");
            Assert.AreEqual(goalCx, cx);
            Assert.AreEqual(goalCz, cz);
        }

        [Test]
        public void LastBuildPopCount_IsSixtyFourHundred_WhenNoWalls_AndNeverExceedsCellCount()
        {
            BlockGrid emptyGrid = NewGrid();
            FlowField emptyField = new FlowField(emptyGrid);
            emptyField.Build(40, 40);
            Assert.AreEqual(6400, emptyField.LastBuildPopCount);

            BlockGrid wallGrid = NewGrid();
            wallGrid.StampBox(0f, 0f, 0f, 1f, 5f, 0.3f, 0.35f, 1);
            FlowField wallField = new FlowField(wallGrid);
            wallField.Build(60, 40);

            Assert.LessOrEqual(wallField.LastBuildPopCount, 6400);
            Assert.LessOrEqual(wallField.LastBuildPopCount, 6400 - wallGrid.BlockedCount,
                "出堆次數不能超過非 Blocked 格的總數");
        }
    }
}
