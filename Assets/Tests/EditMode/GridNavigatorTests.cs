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

        // 獨立寫的暴力鬆弛法（反覆掃全場直到不再變動，不呼叫 FlowField／GridNavigator 的任何邏輯）：
        // 從 startCx/startCz 出發算出整個連通區每一格的路徑成本（直走 10、斜走 14、斜走不切角）。
        private static int[] BruteForceCosts(BlockGrid grid, int startCx, int startCz)
        {
            int columns = grid.Columns, rows = grid.Rows;
            int[] cost = new int[columns * rows];
            for (int i = 0; i < cost.Length; i++) cost[i] = int.MaxValue;
            if (grid.IsBlocked(startCx, startCz)) return cost;
            cost[startCz * columns + startCx] = 0;

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
                        int ncx = cx + dx[n], ncz = cz + dz[n];
                        if (grid.IsBlocked(ncx, ncz)) continue;
                        bool diagonal = dx[n] != 0 && dz[n] != 0;
                        if (diagonal && (grid.IsBlocked(cx + dx[n], cz) || grid.IsBlocked(cx, cz + dz[n]))) continue;

                        int nIdx = ncz * columns + ncx;
                        int next = cost[idx] + stepCost[n];
                        if (next < cost[nIdx]) { cost[nIdx] = next; changed = true; }
                    }
                }
            }
            Assert.LessOrEqual(0, guard, "暴力鬆弛法沒有在合理輪數內收斂");
            return cost;
        }

        // §6 R1 的「最近可達點」定義，用測試自己的暴力鬆弛法重寫一次（不呼叫受測物）：
        //   dMin＝起點連通區內「格心到目的地」的最小歐氏距離；
        //   候選＝距離 ≤ dMin ＋ 一格對角線（0.5×√2）的格；
        //   候選中取路徑成本最低者；同成本取離目的地較近者；再同取索引較小者。
        private static void NearestInComponent(BlockGrid grid, int startCx, int startCz, float destX, float destZ,
                                               out int bestCx, out int bestCz)
        {
            int columns = grid.Columns, rows = grid.Rows;
            int[] cost = BruteForceCosts(grid, startCx, startCz);

            double minDist = double.MaxValue;
            for (int cz = 0; cz < rows; cz++)
            for (int cx = 0; cx < columns; cx++)
            {
                if (cost[cz * columns + cx] == int.MaxValue) continue;
                grid.CellCenter(cx, cz, out float ccx, out float ccz);
                double d = Math.Sqrt((ccx - destX) * (double)(ccx - destX) + (ccz - destZ) * (double)(ccz - destZ));
                if (d < minDist) minDist = d;
            }

            double limit = minDist + 0.5 * Math.Sqrt(2.0) + 1e-4;
            bestCx = startCx;
            bestCz = startCz;
            bool found = false;
            int bestCost = int.MaxValue;
            double bestDist = double.MaxValue;
            for (int cz = 0; cz < rows; cz++)
            for (int cx = 0; cx < columns; cx++)
            {
                int c = cost[cz * columns + cx];
                if (c == int.MaxValue) continue;
                grid.CellCenter(cx, cz, out float ccx, out float ccz);
                double d = Math.Sqrt((ccx - destX) * (double)(ccx - destX) + (ccz - destZ) * (double)(ccz - destZ));
                if (d > limit) continue;
                if (found)
                {
                    if (c > bestCost) continue;
                    if (c == bestCost && !(d < bestDist)) continue;
                }
                found = true;
                bestCost = c;
                bestDist = d;
                bestCx = cx;
                bestCz = cz;
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

        // ───────────────────── §6 R1（審查 H2）：替代點優先停在英雄這一側 ─────────────────────

        // V2-p：牆心 (0,4)、法線 +Z、半寬 2、半厚 0.3、外擴 0.35
        //   → 禁區 x∈[-2.35,2.35]、z∈[3.35,4.65]；格點上 z 格 46~49，南邊第一排空格格心 z=2.75、北邊 z=5.25。
        // 四個案例的數字照 §6 R1 抄，不得放寬。
        [Test]
        public void ResolveGoal_SubstitutePoint_PrefersTheSideTheHeroIsAlreadyOn()
        {
            AssertSubstituteSide(8f, 4f, true);    // ① 英雄在北、點牆腳：兩側平手 → 停在北側
            AssertSubstituteSide(0f, 4f, false);   // ② 英雄在南、點牆腳：兩側平手 → 停在南側
            AssertSubstituteSide(8f, 3.8f, true);  // ③ 英雄在北、點稍微偏南（兩側差 0.2m，仍在一格對角線內）→ 仍停北側
            AssertSubstituteSide(8f, 3.4f, false); // ④ 英雄在北、點明顯在南面（兩側差 1.2m）→ 該繞過去，停南側
        }

        private static void AssertSubstituteSide(float heroZ, float destZ, bool expectNorthSide)
        {
            BlockGrid grid = NewGrid();
            grid.StampBox(0f, 4f, 0f, 1f, 2f, 0.3f, 0.35f, 1);
            GridNavigator nav = new GridNavigator(grid, NewTuning());

            nav.ResolveGoal(0f, heroZ, 0f, destZ, out float goalX, out float goalZ, out bool substituted);
            Assert.IsTrue(substituted, $"點在牆上（z={destZ}）必須替代");

            if (expectNorthSide)
                Assert.Greater(goalZ, 4.65f, $"英雄在 z={heroZ}、點 z={destZ}：替代點應留在牆的北側，實際 ({goalX},{goalZ})");
            else
                Assert.Less(goalZ, 3.35f, $"英雄在 z={heroZ}、點 z={destZ}：替代點應落在牆的南側，實際 ({goalX},{goalZ})");
        }

        // ───────────────────── §6 R5（審查 H4）：有視線就不碰整合場 ─────────────────────

        // V2-q：空格點連下 100 次不同目的地 → Build 次數 0；有牆但不擋視線 → 0；
        //       牆擋視線 → 第一次 1、同目的地同版本重複呼叫仍 1。
        [Test]
        public void ResolveGoal_WithLineOfSight_NeverRebuildsTheIntegrationField()
        {
            BlockGrid empty = NewGrid();
            GridNavigator emptyNav = new GridNavigator(empty, NewTuning());
            for (int i = 0; i < 100; i++)
                emptyNav.ResolveGoal(0f, 0f, -10f + i * 0.2f, 9f, out _, out _, out bool sub);
            Assert.AreEqual(0, emptyNav.BuildCount, "空格點下 100 次指令不該重建任何整合場");

            // 牆在 (0,4)，英雄與目的地都在牆的南側、視線不經過牆
            BlockGrid grid = NewGrid();
            grid.StampBox(0f, 4f, 0f, 1f, 2f, 0.3f, 0.35f, 1);
            GridNavigator nav = new GridNavigator(grid, NewTuning());
            for (int i = 0; i < 100; i++)
                nav.ResolveGoal(-8f, 0f, 8f, 0f + i * 0.01f, out _, out _, out _);
            Assert.AreEqual(0, nav.BuildCount, "牆沒有擋住視線時不該重建整合場");

            // 目的地在牆的正後方：視線被擋，這時才准 Build，而且只准一次
            nav.ResolveGoal(0f, 0f, 0f, 8f, out _, out _, out bool substituted);
            Assert.IsFalse(substituted, "牆後方走得到，不該替代");
            Assert.AreEqual(1, nav.BuildCount, "視線被擋時應該恰好重建一次");
            for (int i = 0; i < 20; i++) nav.ResolveGoal(0f, 0f, 0f, 8f, out _, out _, out _);
            Assert.AreEqual(1, nav.BuildCount, "同目的地、同格點版本重複呼叫不該再重建");
        }

        // ───────────────────── §6 R6 M3：目的地走不到時不得每輪重建 ─────────────────────

        // V2-r：目的地走不到、格點版本不變時，重複 ResolveGoal＋Steer 50 輪，Build 次數在第一輪之後不再增加。
        // 鑑別力：光是「目的地走不到」還不夠——若英雄對替代點有視線，Steer 會走 Direct 快路、根本不建場，
        // 那麼「共用一張場來回重建」這個缺陷就不會顯現。所以另外在英雄與替代點之間斜插一面擋視線的牆，
        // 逼 Steer 真的進 Follow（＝真的會 EnsureBuilt），修復前才會每輪 +2。
        [Test]
        public void ResolveGoalAndSteer_WithAnUnreachableDestination_StopBuildingAfterTheFirstRound()
        {
            BlockGrid grid = NewGrid();
            grid.StampBox(8f, 10f, 1f, 0f, 3f, 0.3f, 0.35f, 1);
            grid.StampBox(12f, 10f, 1f, 0f, 3f, 0.3f, 0.35f, 1);
            grid.StampBox(10f, 8f, 0f, 1f, 3f, 0.3f, 0.35f, 1);
            grid.StampBox(10f, 12f, 0f, 1f, 3f, 0.3f, 0.35f, 1);
            grid.StampBox(5f, 5f, 0.70710678f, 0.70710678f, 4f, 0.3f, 0.35f, 1); // 擋視線用的斜牆（兩端仍繞得過）

            GridNavigator nav = new GridNavigator(grid, NewTuning());
            const float fromX = 0f, fromZ = 0f, destX = 10f, destZ = 10f;

            nav.ResolveGoal(fromX, fromZ, destX, destZ, out float goalX, out float goalZ, out bool substituted);
            SteerMode mode = nav.Steer(fromX, fromZ, goalX, goalZ, out _, out _);
            Assert.IsTrue(substituted, "前置條件：目的地必須是走不到的");
            Assert.AreEqual(SteerMode.Follow, mode,
                "前置條件：Steer 必須真的進 Follow（會建場），否則這條測試對「共用一張場」沒有鑑別力");
            int afterFirstRound = nav.BuildCount;
            Assert.Greater(afterFirstRound, 0, "第一輪本來就該建場，否則這條斷言沒有鑑別力");

            for (int i = 0; i < 50; i++)
            {
                nav.ResolveGoal(fromX, fromZ, destX, destZ, out float gx, out float gz, out _);
                nav.Steer(fromX, fromZ, gx, gz, out _, out _);
            }
            Assert.AreEqual(afterFirstRound, nav.BuildCount,
                "格點版本沒變、英雄沒動，卻仍在「目的地格」與「替代點格」之間來回重建整合場");
        }

        // ───────────────────── §6 R6 M4：goal 格被蓋住不是「沒招」 ─────────────────────

        [Test]
        public void Steer_WhenTheResolvedGoalCellGetsCovered_ReportsGoalBlocked_NotStuck()
        {
            BlockGrid grid = NewGrid();
            GridNavigator nav = new GridNavigator(grid, NewTuning());

            // 先在空場解析出一個 goal，再讓一面新牆蓋住它（＝追擊最長 0.1s 的重解析窗口裡會發生的事）
            nav.ResolveGoal(-8f, 0f, 0f, 0f, out float goalX, out float goalZ, out bool substituted);
            Assert.IsFalse(substituted);
            grid.StampBox(0f, 0f, 0f, 1f, 2f, 0.3f, 0.35f, 1);
            grid.TryWorldToCell(goalX, goalZ, out int goalCx, out int goalCz);
            Assert.IsTrue(grid.IsBlocked(goalCx, goalCz), "前置條件：goal 格要真的被蓋住");

            SteerMode mode = nav.Steer(-8f, 0f, goalX, goalZ, out _, out _);
            Assert.AreEqual(SteerMode.GoalBlocked, mode,
                "goal 格被蓋住要回 GoalBlocked（呼叫端當幀重新解析），不得與「連逃脫格都找不到」的 Stuck 混為一談");
        }

        // ───────────────────── §6 R7：拉直路徑的鑑別力 ─────────────────────

        // V2-s：V4-a 的幾何（英雄 (0,0)、牆心 (0,4) 法線 +Z、半寬 2／半厚 0.3、外擴 0.35、目的地 (0,8)）。
        // 幾何最短繞行 9.48414m（(0,0)→(2.35,3.35)→(2.35,4.65)→(0,8)）；
        // 前視 8 格的實走長度必須 ≤ 9.48414 × 1.06 = 10.0532m，前視 1 格（退化成 45° 鋸齒）必須超過這個上限。
        [Test]
        public void Steer_LookaheadStraightensThePath_WhileLookaheadOneZigzagsPastTheBudget()
        {
            const double shortest = 9.48414;
            const double budget = shortest * 1.06;

            double straightened = WalkLength(8);
            double zigzag = WalkLength(1);

            Assert.LessOrEqual(straightened, budget,
                $"前視 8 格的實走長度 {straightened:F4}m 超過 9.48414×1.06 = {budget:F4}m：路徑沒有被拉直");
            Assert.Greater(zigzag, budget,
                $"對照失敗：前視 1 格也只走了 {zigzag:F4}m，代表這條上限對「有沒有拉直」沒有鑑別力");
        }

        // 每步 0.1m 沿 Steer 方向前進，回傳走到目的地 0.1m 內的實走長度（走不到回 double.MaxValue）。
        private static double WalkLength(int lookaheadCells)
        {
            BlockGrid grid = NewGrid();
            grid.StampBox(0f, 4f, 0f, 1f, 2f, 0.3f, 0.35f, 1);
            NavGridTuning tuning = new NavGridTuning { FollowLookaheadCells = lookaheadCells };
            GridNavigator nav = new GridNavigator(grid, tuning);

            const float destX = 0f, destZ = 8f;
            const double step = 0.1;
            nav.ResolveGoal(0f, 0f, destX, destZ, out float goalX, out float goalZ, out bool substituted);
            Assert.IsFalse(substituted, "前置條件：牆後方走得到");

            float x = 0f, z = 0f;
            double travelled = 0.0;
            for (int i = 0; i < 400; i++)
            {
                double remaining = Math.Sqrt((x - destX) * (double)(x - destX) + (z - destZ) * (double)(z - destZ));
                if (remaining <= step) return travelled + remaining;

                SteerMode mode = nav.Steer(x, z, goalX, goalZ, out float dirX, out float dirZ);
                Assert.AreNotEqual(SteerMode.Stuck, mode, $"第 {i} 步卡死（前視 {lookaheadCells} 格）");
                x += (float)(dirX * step);
                z += (float)(dirZ * step);
                travelled += step;
                grid.TryWorldToCell(x, z, out int cx, out int cz);
                Assert.IsFalse(grid.IsBlocked(cx, cz), $"第 {i} 步踏進 Blocked 格（前視 {lookaheadCells} 格）");
            }
            return double.MaxValue;
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

            // 幾何最短繞行（繞右側；牆體外擴 0.35 後的包圍：左壁 x∈[-2.65,-1.35]、右壁 x∈[1.35,2.65]，
            // 兩壁 z∈[-6.35,0.35]；底 x∈[-2.35,2.35]、z∈[-6.65,-5.35]）：
            // r1 對抗審查 L1（§6 R7）：原先的 15.48m 少算了右壁的**內**上角——(0,-3) 直接拉到 (2.65,0.35)
            // 這條線在 x=1.35 處 z=-1.29，落在右壁的 z∈[-6.35,0.35] 之內，會穿牆。真正的最短折線多一個轉角：
            // (0,-3)→右壁內上角 (1.35,0.35)：√(1.35²+3.35²) = 3.6118m
            // →右壁外上角 (2.65,0.35)：                       1.3000m
            // →右壁外下角 (2.65,-6.35)：                      6.7000m
            // →目標 (0,-10)：√(2.65²+3.65²) =                 4.5106m（此線在 x=2.35 處 z=-6.76，低於底牆外角 -6.65，不碰底牆）
            const double shortestDetourLength = 3.6118 + 1.30 + 6.70 + 4.5106; // = 16.12m
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
#if UNITY_5_3_OR_NEWER
        // Unity 的 Mono 上 GC.GetAllocatedBytesForCurrentThread() 恆回 0：零配置斷言會空轉成綠、正向對照必紅。
        // 這組量測只在 dotnet（verify.sh）下有鑑別力；Unity 下的零配置由 PlayMode 的 Profiler 探針負責（ZeroAllocationTests，V4-i）。
        [Ignore("dotnet only: GC.GetAllocatedBytesForCurrentThread is always 0 on Unity Mono; Unity-side coverage is the PlayMode profiler probe")]
#endif
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
#if UNITY_5_3_OR_NEWER
        // Unity 的 Mono 上 GC.GetAllocatedBytesForCurrentThread() 恆回 0：零配置斷言會空轉成綠、正向對照必紅。
        // 這組量測只在 dotnet（verify.sh）下有鑑別力；Unity 下的零配置由 PlayMode 的 Profiler 探針負責（ZeroAllocationTests，V4-i）。
        [Ignore("dotnet only: GC.GetAllocatedBytesForCurrentThread is always 0 on Unity Mono; Unity-side coverage is the PlayMode profiler probe")]
#endif
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
#if UNITY_5_3_OR_NEWER
        // Unity 的 Mono 上 GC.GetAllocatedBytesForCurrentThread() 恆回 0：零配置斷言會空轉成綠、正向對照必紅。
        // 這組量測只在 dotnet（verify.sh）下有鑑別力；Unity 下的零配置由 PlayMode 的 Profiler 探針負責（ZeroAllocationTests，V4-i）。
        [Ignore("dotnet only: GC.GetAllocatedBytesForCurrentThread is always 0 on Unity Mono; Unity-side coverage is the PlayMode profiler probe")]
#endif
        public void AllocationMeasurement_PositiveControl_DetectsARealAllocation()
        {
            // o. 正向對照：證明量測手法真的抓得到配置，不是量測方式本身失靈而一直讀到 0。
            long before = GC.GetAllocatedBytesForCurrentThread();
            int[] leak = new int[16];
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.Greater(after, before, "new int[16] 應該讓量到的配置量增加");
            GC.KeepAlive(leak);
        }

        [Test]
#if !UNITY_5_3_OR_NEWER
        // dotnet 下這個前提本來就不成立（計數器正常運作），所以只在 Unity 裡跑。
        [Ignore("unity only: this test pins down the Unity-Mono premise that the three [Ignore] above rely on")]
#endif
        // r1 對抗審查 L3（§6 R7）：上面三個 [Ignore] 的理由是「Unity 的 Mono 上
        // GC.GetAllocatedBytesForCurrentThread() 恆回 0」，先前沒有任何實測佐證。這條就是那份佐證：
        // 在 Unity 裡對一次真實配置量測，若計數器哪天被修好、量得到了，這條會紅——提醒把那三個放回來。
        public void AllocationMeasurement_OnUnityMono_StillReportsZeroForARealAllocation()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            int[] leak = new int[4096];
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(before, after,
                "Unity 的 GC.GetAllocatedBytesForCurrentThread 已經量得到配置了（" + before + " → " + after +
                "）：GridNavigatorTests 裡三個 [Ignore] 的前提不再成立，請把它們放回 Unity 端執行");
            GC.KeepAlive(leak);
        }
    }
}
