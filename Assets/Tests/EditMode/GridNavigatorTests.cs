using System;
using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // HeroLocomotion 唯一會碰的入口：目標解析＋逐幀轉向（PHASE2_BATCH2_PLAN.md §5 V2 i～o）。
    public sealed class GridNavigatorTests
    {
        private static BlockGrid NewGrid() => new BlockGrid(-20f, -20f, 0.5f, 80, 80);
        private static NavGridTuning NewTuning() => new NavGridTuning();

        // 獨立寫的 BFS 連通區掃描（不呼叫 FlowField／GridNavigator 的任何邏輯）：
        // 從 startCx/startCz 出發，收集整個連通區（8 鄰接、斜走不切角），回傳其中離 (destX,destZ) 歐氏距離
        // 最近的格；同距離取索引較小者。
        private static void NearestInComponent(BlockGrid grid, int startCx, int startCz, float destX, float destZ,
                                               out int bestCx, out int bestCz)
        {
            int columns = grid.Columns, rows = grid.Rows;
            bool[] visited = new bool[columns * rows];
            int[] queueCx = new int[columns * rows];
            int[] queueCz = new int[columns * rows];
            int head = 0, tail = 0;

            visited[startCz * columns + startCx] = true;
            queueCx[tail] = startCx;
            queueCz[tail] = startCz;
            tail++;

            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dz = { 0, 0, 1, -1, 1, -1, 1, -1 };

            bestCx = startCx;
            bestCz = startCz;
            double bestDistSq = double.MaxValue;
            bool found = false;

            while (head < tail)
            {
                int cx = queueCx[head];
                int cz = queueCz[head];
                head++;

                grid.CellCenter(cx, cz, out float ccx, out float ccz);
                double ddx = ccx - destX, ddz = ccz - destZ;
                double distSq = ddx * ddx + ddz * ddz;
                int idx = cz * columns + cx;
                int bestIdx = bestCz * columns + bestCx;
                if (!found || distSq < bestDistSq - 1e-9)
                {
                    found = true;
                    bestDistSq = distSq;
                    bestCx = cx;
                    bestCz = cz;
                }
                else if (Math.Abs(distSq - bestDistSq) <= 1e-9 && idx < bestIdx)
                {
                    bestCx = cx;
                    bestCz = cz;
                }

                for (int n = 0; n < 8; n++)
                {
                    int ncx = cx + dx[n];
                    int ncz = cz + dz[n];
                    if (grid.IsBlocked(ncx, ncz)) continue;
                    bool diagonal = dx[n] != 0 && dz[n] != 0;
                    if (diagonal && (grid.IsBlocked(cx + dx[n], cz) || grid.IsBlocked(cx, cz + dz[n]))) continue;

                    int nIdx = ncz * columns + ncx;
                    if (visited[nIdx]) continue;
                    visited[nIdx] = true;
                    queueCx[tail] = ncx;
                    queueCz[tail] = ncz;
                    tail++;
                }
            }
        }

        [Test]
        public void ResolveGoal_DestinationSealedByWalls_SubstitutesNearestReachableCell_ThenOriginalOnceOpened()
        {
            // i. 用四面互相重疊的牆圍出一個完全封閉的房間，目的地在房間正中央，起點在房間外。
            // 四面牆彼此重疊、interior 開口刻意留 2.7m（遠大於一格 0.5m），避免拆牆後開口被
            // 網格量化（partial overlap 整格算 Blocked）吃掉，變成「拆了也還是走不到」的假陽性。
            BlockGrid grid = NewGrid();
            grid.StampBox(8f, 10f, 1f, 0f, 3f, 0.3f, 0.35f, 1);   // 西牆：x∈[7.35,8.65]，z∈[7,13]
            grid.StampBox(12f, 10f, 1f, 0f, 3f, 0.3f, 0.35f, 1);  // 東牆：x∈[11.35,12.65]，z∈[7,13]
            grid.StampBox(10f, 8f, 0f, 1f, 3f, 0.3f, 0.35f, 1);   // 南牆：z∈[7.35,8.65]，x∈[7,13]
            grid.StampBox(10f, 12f, 0f, 1f, 3f, 0.3f, 0.35f, 1);  // 北牆：z∈[11.35,12.65]，x∈[7,13]

            GridNavigator nav = new GridNavigator(grid, NewTuning());
            const float fromX = 0f, fromZ = 0f, destX = 10f, destZ = 10f;

            nav.ResolveGoal(fromX, fromZ, destX, destZ, out float goalX, out float goalZ, out bool substituted);
            Assert.IsTrue(substituted, "目的地被圍死，必須替代");

            grid.TryWorldToCell(fromX, fromZ, out int fromCx, out int fromCz);
            NearestInComponent(grid, fromCx, fromCz, destX, destZ, out int expectedCx, out int expectedCz);
            grid.TryWorldToCell(goalX, goalZ, out int gotCx, out int gotCz);
            Assert.AreEqual(expectedCx, gotCx);
            Assert.AreEqual(expectedCz, gotCz);

            // 拆掉北牆（開一個口）：現在走得到了，substituted=false、回傳原目的地。
            grid.StampBox(10f, 12f, 0f, 1f, 3f, 0.3f, 0.35f, -1);
            nav.ResolveGoal(fromX, fromZ, destX, destZ, out float goalX2, out float goalZ2, out bool substituted2);
            Assert.IsFalse(substituted2, "拆牆後應該走得到，不該再替代");
            Assert.AreEqual(destX, goalX2, 1e-5f);
            Assert.AreEqual(destZ, goalZ2, 1e-5f);
        }

        [Test]
        public void ResolveGoal_DestinationInsideAWall_BehavesLikeSealedCase()
        {
            // j. 目的地直接點在牆腳（Blocked 格）上：同 i 前半，substituted=true，回傳連通區內最近格。
            BlockGrid grid = NewGrid();
            grid.StampBox(0f, 0f, 0f, 1f, 3f, 0.3f, 0.35f, 1);

            GridNavigator nav = new GridNavigator(grid, NewTuning());
            const float fromX = -10f, fromZ = -10f, destX = 0f, destZ = 0f; // 目的地正中在牆上

            grid.TryWorldToCell(destX, destZ, out int destCx, out int destCz);
            Assert.IsTrue(grid.IsBlocked(destCx, destCz), "前置條件：目的地要真的落在 Blocked 格");

            nav.ResolveGoal(fromX, fromZ, destX, destZ, out float goalX, out float goalZ, out bool substituted);
            Assert.IsTrue(substituted);

            grid.TryWorldToCell(fromX, fromZ, out int fromCx, out int fromCz);
            NearestInComponent(grid, fromCx, fromCz, destX, destZ, out int expectedCx, out int expectedCz);
            grid.TryWorldToCell(goalX, goalZ, out int gotCx, out int gotCz);
            Assert.AreEqual(expectedCx, gotCx);
            Assert.AreEqual(expectedCz, gotCz);
        }

        [Test]
        public void Steer_StartingInsideTheInflateZone_IsNotStuck_AndPointsTowardNearestFreeCell()
        {
            // k. 起點在 Blocked 格（貼牆的外擴區）：Steer 不是 Stuck，方向與「指向最近空格」的內積 > 0；
            // ResolveGoal 不因此誤判為走不到。
            BlockGrid grid = NewGrid();
            grid.StampBox(0f, 0f, 0f, 1f, 2f, 0.3f, 0.35f, 1);
            GridNavigator nav = new GridNavigator(grid, NewTuning());

            // 牆登記範圍的角格 (44,38)：核心厚度只到 0.3，這格幾乎必然只是外擴造成的 Blocked。
            grid.CellCenter(44, 38, out float fromX, out float fromZ);
            Assert.IsTrue(grid.IsBlocked(44, 38), "前置條件：起點格本身要是 Blocked");

            const float destX = 15f, destZ = 15f;
            nav.ResolveGoal(fromX, fromZ, destX, destZ, out float goalX, out float goalZ, out bool substituted);
            Assert.IsFalse(substituted, "起點卡在外擴區不該讓 ResolveGoal 誤判成走不到");

            SteerMode mode = nav.Steer(fromX, fromZ, goalX, goalZ, out float dirX, out float dirZ);
            Assert.AreNotEqual(SteerMode.Stuck, mode);

            Assert.IsTrue(grid.TryFindNearestFree(fromX, fromZ, 20, out int freeCx, out int freeCz));
            grid.CellCenter(freeCx, freeCz, out float freeX, out float freeZ);
            double refDx = freeX - fromX, refDz = freeZ - fromZ;
            double refLen = Math.Sqrt(refDx * refDx + refDz * refDz);
            double dot = dirX * (refDx / refLen) + dirZ * (refDz / refLen);
            Assert.Greater(dot, 0.0, "方向要與「指向最近空格」大致同向");
        }

        [Test]
        public void Steer_WithClearLineOfSight_ReturnsDirect()
        {
            // l. 有視線 → Direct。
            BlockGrid grid = NewGrid();
            GridNavigator nav = new GridNavigator(grid, NewTuning());
            SteerMode mode = nav.Steer(0f, 0f, 10f, 0f, out float dirX, out float dirZ);
            Assert.AreEqual(SteerMode.Direct, mode);
            Assert.AreEqual(1f, dirX, 1e-5f);
            Assert.AreEqual(0f, dirZ, 1e-5f);
        }

        [Test]
        public void Steer_UTrapSimulation_DetoursAroundTheWalls_WhileNaiveSteeringGetsStuck()
        {
            // m. U 形陷阱：左、右、底三面牆圍出一個開口朝 +z 的杯子；英雄在杯內、目標在底牆外側（z 更負）。
            BlockGrid grid = NewGrid();
            grid.StampBox(-2f, -3f, 1f, 0f, 3f, 0.3f, 0.35f, 1); // 左壁：x∈[-2.65,-1.35]，z∈[-6,0]
            grid.StampBox(2f, -3f, 1f, 0f, 3f, 0.3f, 0.35f, 1);  // 右壁：x∈[1.35,2.65]，z∈[-6,0]
            grid.StampBox(0f, -6f, 0f, 1f, 2f, 0.3f, 0.35f, 1);  // 底：x∈[-2,2]，z∈[-6.65,-5.35]

            GridNavigator nav = new GridNavigator(grid, NewTuning());
            const float startX = 0f, startZ = -3f;
            const float destX = 0f, destZ = -10f;

            nav.ResolveGoal(startX, startZ, destX, destZ, out float goalX, out float goalZ, out bool substituted);
            Assert.IsFalse(substituted, "目標繞得到，不該被替代");

            // 幾何最短繞行（繞右側；牆體外擴 0.35 後的包圍：右壁 x∈[1.35,2.65]、z∈[-6.35,0.35]；底 x∈[-2.35,2.35]、z∈[-6.65,-5.35]）：
            // (0,-3)→右壁上外角 (2.65,0.35)：√(2.65²+3.35²)=4.27m
            // →右壁下外角 (2.65,-6.35)：6.70m
            // →目標 (0,-10)：√(2.65²+3.65²)=4.51m（此線在 x=2.35 處 z=-6.76，低於底牆外角 -6.65，不碰底牆）
            // 覆審更正：原先填的是一條留了安全邊界的 22m 可行路徑（上界），會把 1.5 倍上限實質放寬成 2.1 倍。
            const double shortestDetourLength = 4.27 + 6.70 + 4.51; // = 15.48m
            const double stepDistance = 0.1;
            int maxSteps = (int)Math.Ceiling(1.5 * shortestDetourLength / stepDistance);

            float steeredX = startX, steeredZ = startZ;
            bool steeredArrived = false;
            for (int i = 0; i < maxSteps; i++)
            {
                SteerMode mode = nav.Steer(steeredX, steeredZ, goalX, goalZ, out float dirX, out float dirZ);
                Assert.AreNotEqual(SteerMode.Stuck, mode, $"第 {i} 步不該卡死");

                float nextX = steeredX + dirX * (float)stepDistance;
                float nextZ = steeredZ + dirZ * (float)stepDistance;
                grid.TryWorldToCell(nextX, nextZ, out int nextCx, out int nextCz);
                Assert.IsFalse(grid.IsBlocked(nextCx, nextCz), $"第 {i} 步不得踏入 Blocked 格");
                steeredX = nextX;
                steeredZ = nextZ;

                double distToGoal = Math.Sqrt((steeredX - destX) * (steeredX - destX) + (steeredZ - destZ) * (steeredZ - destZ));
                if (distToGoal <= 0.3)
                {
                    steeredArrived = true;
                    break;
                }
            }
            Assert.IsTrue(steeredArrived, $"沿 Steer 方向走，應該在 {maxSteps} 步內抵達目標 0.3m 內");

            // 對照組：樸素轉向（永遠朝目標直走），撞到牆就不動（模擬 v0.3.2 的頂牆行為）。
            float naiveX = startX, naiveZ = startZ;
            bool naiveArrived = false;
            for (int i = 0; i < maxSteps; i++)
            {
                double dx = destX - naiveX, dz = destZ - naiveZ;
                double len = Math.Sqrt(dx * dx + dz * dz);
                if (len <= 1e-9) { naiveArrived = true; break; }
                float nextX = naiveX + (float)(dx / len * stepDistance);
                float nextZ = naiveZ + (float)(dz / len * stepDistance);
                grid.TryWorldToCell(nextX, nextZ, out int nextCx, out int nextCz);
                if (!grid.IsBlocked(nextCx, nextCz))
                {
                    naiveX = nextX;
                    naiveZ = nextZ;
                }
                // Blocked 時模擬頂牆：這一步不動。

                double distToGoal = Math.Sqrt((naiveX - destX) * (naiveX - destX) + (naiveZ - destZ) * (naiveZ - destZ));
                if (distToGoal <= 0.3)
                {
                    naiveArrived = true;
                    break;
                }
            }
            Assert.IsFalse(naiveArrived, "樸素直走轉向頂著底牆，同樣步數內不該到得了");
        }

        [Test]
        public void SteadyState_ResolveGoalAndSteer_AllocateZeroBytes_AfterWarmup()
        {
            // o. 零配置（GridNavigator 部分）：暖機後 ResolveGoal＋Steer 各跑 100 次，差值必須是 0。
            BlockGrid grid = NewGrid();
            grid.StampBox(0f, 0f, 0f, 1f, 2f, 0.3f, 0.35f, 1);
            GridNavigator nav = new GridNavigator(grid, NewTuning());
            const float fromX = -10f, fromZ = -10f, destX = 10f, destZ = 10f;

            for (int i = 0; i < 8; i++)
            {
                nav.ResolveGoal(fromX, fromZ, destX, destZ, out float wgx, out float wgz, out bool _);
                nav.Steer(fromX, fromZ, wgx, wgz, out _, out _);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                nav.ResolveGoal(fromX, fromZ, destX, destZ, out float goalX, out float goalZ, out bool _);
                nav.Steer(fromX, fromZ, goalX, goalZ, out _, out _);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(before, after, "穩態下 ResolveGoal／Steer 不得配置任何位元組");
        }

        [Test]
        public void SteadyState_BuildAndStampBox_AllocateZeroBytes_AfterWarmup()
        {
            // o. 零配置（FlowField.Build／BlockGrid.StampBox 部分）。
            BlockGrid grid = NewGrid();
            FlowField field = new FlowField(grid);
            for (int i = 0; i < 8; i++)
            {
                field.Build(40, 40);
                grid.StampBox(-15f, -15f, 1f, 0f, 0.2f, 0.2f, 0f, 1);
                grid.StampBox(-15f, -15f, 1f, 0f, 0.2f, 0.2f, 0f, -1);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                field.Build(40, 40);
                grid.StampBox(-15f, -15f, 1f, 0f, 0.2f, 0.2f, 0f, 1);
                grid.StampBox(-15f, -15f, 1f, 0f, 0.2f, 0.2f, 0f, -1);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(before, after, "穩態下 Build／StampBox 不得配置任何位元組");
        }

        [Test]
        public void AllocationMeasurement_PositiveControl_DetectsARealAllocation()
        {
            // o. 正向對照：證明量測手法真的抓得到配置，不是量測方式本身失靈而一直讀到 0。
            long before = GC.GetAllocatedBytesForCurrentThread();
            int[] leak = new int[16];
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.Greater(after, before, "new int[16] 應該讓量到的配置量增加");
            GC.KeepAlive(leak);
        }
    }
}
