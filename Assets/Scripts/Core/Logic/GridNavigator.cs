using System;

namespace Vow.Core.Logic
{
    public enum SteerMode
    {
        Direct,
        Follow,
        Stuck,

        // r1 對抗審查 M4：原本 Stuck 身兼兩種語意，呼叫端一視同仁退回 NavMesh 速度＝無聲回到 v0.3.2 的頂牆。
        // GoalBlocked＝解析出來的 goal 格被新的一面牆蓋住了（重解析還沒輪到的窗口），呼叫端應當幀重新解析；
        // Stuck＝連逃脫格都找不到（搜尋半徑內全是 Blocked），那才是真的沒招。
        GoalBlocked
    }

    // HeroLocomotion 唯一會碰的入口：目標解析（走不到就退到最近可達點）＋逐幀轉向（有視線就照舊、被擋才繞牆）。
    // 不依賴 UnityEngine、不用 Linq、建構後執行期零配置（內部兩個 FlowField 只在建構時配置一次）。
    public sealed class GridNavigator
    {
        // r1 對抗審查 R1：候選帶的浮點比較容差。兩側平手（例如格心恰好落在 dMin＋一格對角線上）時，
        // 1 ULP 的差別會讓替代點跳到另一格；用固定的 0.1mm 讓「≤」在邊界上是決定性的包含。
        private const double CandidateSlackEpsilon = 1e-4;

        private readonly BlockGrid _grid;
        private readonly NavGridTuning _tuning;

        // 以「使用者指定的目的地格」為源：只用來回答「從英雄那裡走不走得到」。
        private readonly FlowField _goalField;

        // 只在目的地走不到時才用：以 from 所在連通區為源，探出整個連通區供「最近可達點」比對，
        // 同時提供 R1 需要的「從起點出發的路徑成本」。
        private readonly FlowField _componentField;

        // r1 對抗審查 M3：Steer 用的是「解析後實際要走的目標」為源的場。修復前它與上面的 _goalField 是同一份，
        // 目的地走不到時會在「目的地格」與「替代點格」之間每 0.1s 來回重建整場 Dijkstra。
        private readonly FlowField _steerField;

        public GridNavigator(BlockGrid grid, NavGridTuning tuning)
        {
            _grid = grid;
            _tuning = tuning;
            _goalField = new FlowField(grid);
            _componentField = new FlowField(grid);
            _steerField = new FlowField(grid);
        }

        public BlockGrid Grid => _grid;

        // r1 對抗審查 M5：把 tuning 讓呼叫端讀得到，HeroLocomotion 不必自己複製一份搜尋半徑常數。
        public NavGridTuning Tuning => _tuning;

        // 累計重建整合場的次數（唯讀，供測試檢驗 R5／M3：沒有牆擋視線時不得碰整合場、
        // 目的地走不到時不得每輪重建）。
        public int BuildCount { get; private set; }

        // r2 對抗審查 N1（§6 R11 V11-a）：替代點解析時每檢查一格 +1（唯讀累計，語意同 BuildCount）。
        // 時間量測不當及格線（§4-9），所以拿「掃了幾格」當代理指標——它是決定性的。
        public int ScannedCellCount { get; private set; }

        // 替代點解析真的發生（substituted==true）的累計次數。Unity 端的零配置量測窗口用它斷言
        // 「這條路徑真的被行使過」（§6 R11 V11-f 的活性）。
        public int SubstitutedCount { get; private set; }

        // 目的地那格非 Blocked 且從 (fromX,fromZ) 走得到 → 原樣回傳、substituted=false。
        // 否則回傳「最近可達點」的格心、substituted=true。
        //
        // r1 對抗審查 H2（§6 R1）：舊定義只比「格心離目的地的歐氏距離」，完全不看英雄在哪——英雄站在牆北
        // 點牆腳，會被送到牆南，得繞過整面 5m 寬的牆才走得到。新定義：
        //   dMin＝起點連通區內「格心到目的地」的最小歐氏距離；
        //   候選＝距離 ≤ dMin ＋ 一格對角線（格寬×√2）的格；
        //   候選中取「從起點出發的路徑成本最低」者；同成本取離目的地較近者；再同取索引較小者。
        // 路徑成本直接讀 _componentField（以英雄為源的 Dijkstra），不另外再掃一次。
        //
        // from 自己落在 Blocked 格（貼牆站在外擴區）時，以離它最近的空格當起點判斷連通性。
        public void ResolveGoal(float fromX, float fromZ, float destX, float destZ,
                                out float goalX, out float goalZ, out bool substituted)
        {
            _grid.TryWorldToCell(fromX, fromZ, out int fromCx, out int fromCz);
            int connCx = fromCx;
            int connCz = fromCz;
            if (_grid.IsBlocked(fromCx, fromCz) &&
                _grid.TryFindNearestFree(fromX, fromZ, _tuning.EscapeSearchRadiusCells, out int freeCx, out int freeCz))
            {
                connCx = freeCx;
                connCz = freeCz;
            }

            _grid.TryWorldToCell(destX, destZ, out int destCx, out int destCz);
            bool destBlocked = _grid.IsBlocked(destCx, destCz);

            if (!destBlocked)
            {
                // r1 對抗審查 H4（§6 R5）：有視線就必然走得到，連整合場都不必碰。
                // 修復前這裡無條件跑一次全場 Dijkstra，實測中位數 0.630ms——使用者的症狀是「點一下卡一下」。
                if (_grid.HasLineOfSight(fromX, fromZ, destX, destZ))
                {
                    goalX = destX;
                    goalZ = destZ;
                    substituted = false;
                    return;
                }

                EnsureBuilt(_goalField, destCx, destCz);
                if (_goalField.IsReached(connCx, connCz))
                {
                    goalX = destX;
                    goalZ = destZ;
                    substituted = false;
                    return;
                }
            }

            EnsureBuilt(_componentField, connCx, connCz);

            // 第一遍：連通區內「格心到目的地」的最小歐氏距離（R1 的 dMin）。
            double minDistSq = double.MaxValue;
            for (int cz = 0; cz < _grid.Rows; cz++)
            {
                for (int cx = 0; cx < _grid.Columns; cx++)
                {
                    ScannedCellCount++;
                    if (!_componentField.IsReached(cx, cz)) continue;
                    _grid.CellCenter(cx, cz, out float cxWorld, out float czWorld);
                    double dx = cxWorld - destX;
                    double dz = czWorld - destZ;
                    double distSq = dx * dx + dz * dz;
                    if (distSq < minDistSq) minDistSq = distSq;
                }
            }

            // N10（§6 R11，記錄不修）：連通區完全空（英雄格 Blocked 且搜尋半徑內無空格）時 found 會留 false，
            // 回傳的是 connCx/connCz 的格心——那可能是一個 Blocked 格。本作牆寬 4m、場地 40m 排不出這個盤面。
            int bestCx = connCx;
            int bestCz = connCz;
            bool found = false;
            int bestCost = int.MaxValue;
            double bestDistSq = double.MaxValue;
            if (minDistSq == double.MaxValue)
            {
                // 連通區一格都沒有：候選帶無從算起，直接走 N10 那條退路。
                _grid.CellCenter(bestCx, bestCz, out goalX, out goalZ);
                substituted = true;
                SubstitutedCount++;
                return;
            }

            double slack = Math.Sqrt(minDistSq) + _tuning.CellSize * Math.Sqrt(2.0) + CandidateSlackEpsilon;
            double slackSq = slack * slack;

            // r2 對抗審查 N1（§6 R11 V11-a）：候選帶 B 的定義是「格心離目的地 ≤ slack」，所以 B 必然落在
            // 「以目的地為心、半邊長 slack」的方塊內——方塊外的格在下面兩遍都會被 distSq > slackSq 濾掉，
            // 掃它們只是白跑。修復前這兩遍各掃完整的 80×80，一次解析＝三趟 6400 格（實測 0.92ms）。
            // 邊界各外擴一格吸收浮點量化誤差；語意與掃全場完全相同（V11-b 的 300 盤差分測試釘住這一點）。
            _grid.TryWorldToCell((float)(destX - slack), (float)(destZ - slack), out int bandMinCx, out int bandMinCz);
            _grid.TryWorldToCell((float)(destX + slack), (float)(destZ + slack), out int bandMaxCx, out int bandMaxCz);
            bandMinCx--; bandMinCz--; bandMaxCx++; bandMaxCz++;
            if (bandMinCx < 0) bandMinCx = 0;
            if (bandMinCz < 0) bandMinCz = 0;
            if (bandMaxCx > _grid.Columns - 1) bandMaxCx = _grid.Columns - 1;
            if (bandMaxCz > _grid.Rows - 1) bandMaxCz = _grid.Rows - 1;

            // 第二遍（§6 R8／R1a 階段 2）：候選帶 B 內取路徑成本最低者 W——**只用來決定停在哪一側**。
            // 同成本取離目的地較近者；再同取索引較小者（掃描順序 cz 大迴圈、cx 小迴圈＝索引遞增，
            // 只在嚴格更優時換人，所以平手時留的是索引最小那格）。
            for (int cz = bandMinCz; cz <= bandMaxCz; cz++)
            {
                for (int cx = bandMinCx; cx <= bandMaxCx; cx++)
                {
                    ScannedCellCount++;
                    if (!_componentField.IsReached(cx, cz)) continue;
                    _grid.CellCenter(cx, cz, out float cxWorld, out float czWorld);
                    double dx = cxWorld - destX;
                    double dz = czWorld - destZ;
                    double distSq = dx * dx + dz * dz;
                    if (distSq > slackSq) continue;

                    int cost = _componentField.CostAt(cx, cz);
                    if (found)
                    {
                        if (cost > bestCost) continue;
                        if (cost == bestCost && !(distSq < bestDistSq)) continue;
                    }
                    found = true;
                    bestCost = cost;
                    bestDistSq = distSq;
                    bestCx = cx;
                    bestCz = cz;
                }
            }

            // 第三遍（§6 R8／R1a 階段 3）：只用「成本最低」會把停點往英雄方向多拉一格，離使用者點的位置
            // 白白遠 0.5m。所以在「路徑成本 ≤ cost(W)＋SubstituteCostSlack」的候選裡改取**離目的地最近**者，
            // 同距離取成本低者、再同取索引小者。語意＝為了更靠近你點的位置，最多願意多走約 1.4m。
            int costLimit = bestCost + _tuning.SubstituteCostSlack;
            for (int cz = bandMinCz; cz <= bandMaxCz; cz++)
            {
                for (int cx = bandMinCx; cx <= bandMaxCx; cx++)
                {
                    ScannedCellCount++;
                    if (!_componentField.IsReached(cx, cz)) continue;
                    int cost = _componentField.CostAt(cx, cz);
                    if (cost > costLimit) continue;
                    _grid.CellCenter(cx, cz, out float cxWorld, out float czWorld);
                    double dx = cxWorld - destX;
                    double dz = czWorld - destZ;
                    double distSq = dx * dx + dz * dz;
                    if (distSq > slackSq) continue;

                    if (distSq > bestDistSq) continue;
                    if (distSq == bestDistSq && cost >= bestCost) continue;
                    bestCost = cost;
                    bestDistSq = distSq;
                    bestCx = cx;
                    bestCz = cz;
                }
            }

            _grid.CellCenter(bestCx, bestCz, out goalX, out goalZ);
            substituted = true;
            SubstitutedCount++;
        }

        // Direct＝到 goal 有視線（呼叫端沿用 NavMesh 的 desiredVelocity）。
        // Follow＝沿整合場往前看最多 N 格，取「從實際位置仍有視線」的最遠格心；
        //         所在格 Blocked／未到達時，方向指向最近的已到達空格（先走出外擴區）。
        // Stuck ＝連逃脫格都找不到。
        public SteerMode Steer(float x, float z, float goalX, float goalZ, out float dirX, out float dirZ)
        {
            dirX = 0f;
            dirZ = 0f;

            if (_grid.HasLineOfSight(x, z, goalX, goalZ))
            {
                SetDirectionTo(x, z, goalX, goalZ, out dirX, out dirZ);
                return SteerMode.Direct;
            }

            _grid.TryWorldToCell(goalX, goalZ, out int goalCx, out int goalCz);
            // M4：goal 格被蓋住（新牆立在上一次解析出來的目標上）——這不是「沒招」，
            // 呼叫端要當幀拿原始目的地重新解析，不得退回頂牆。
            if (_grid.IsBlocked(goalCx, goalCz)) return SteerMode.GoalBlocked;

            EnsureBuilt(_steerField, goalCx, goalCz);

            _grid.TryWorldToCell(x, z, out int cx, out int cz);
            if (_grid.IsBlocked(cx, cz) || !_steerField.IsReached(cx, cz))
            {
                if (!TryFindNearestReached(x, z, out int escapeCx, out int escapeCz)) return SteerMode.Stuck;
                _grid.CellCenter(escapeCx, escapeCz, out float ex, out float ez);
                SetDirectionTo(x, z, ex, ez, out dirX, out dirZ);
                return SteerMode.Follow;
            }

            int curCx = cx;
            int curCz = cz;
            int chosenCx = cx;
            int chosenCz = cz;
            bool any = false;
            for (int i = 0; i < _tuning.FollowLookaheadCells; i++)
            {
                if (!_steerField.TryGetNext(curCx, curCz, out int nx, out int nz)) break;
                curCx = nx;
                curCz = nz;
                _grid.CellCenter(curCx, curCz, out float candX, out float candZ);
                bool visible = _grid.HasLineOfSight(x, z, candX, candZ);
                if (!visible)
                {
                    if (!any)
                    {
                        // 連整合場給的下一格都看不到（貼著轉角站）：至少先走這一步，不判死
                        chosenCx = curCx;
                        chosenCz = curCz;
                        any = true;
                    }
                    break;
                }
                chosenCx = curCx;
                chosenCz = curCz;
                any = true;
            }

            _grid.CellCenter(chosenCx, chosenCz, out float tx, out float tz);
            SetDirectionTo(x, z, tx, tz, out dirX, out dirZ);
            return SteerMode.Follow;
        }

        private void EnsureBuilt(FlowField field, int goalCx, int goalCz)
        {
            if (field.BuiltForVersion != _grid.Version || field.GoalCx != goalCx || field.GoalCz != goalCz)
            {
                field.Build(goalCx, goalCz);
                BuildCount++;
            }
        }

        // 以歐氏距離找「_steerField 裡已到達」的最近格；同距離取索引較小者（與 BlockGrid.TryFindNearestFree 同一決定性規則）。
        private bool TryFindNearestReached(float x, float z, out int bestCx, out int bestCz)
        {
            _grid.TryWorldToCell(x, z, out int ocx, out int ocz);
            int radius = _tuning.EscapeSearchRadiusCells;

            int minCx = ocx - radius;
            int maxCx = ocx + radius;
            int minCz = ocz - radius;
            int maxCz = ocz + radius;
            if (minCx < 0) minCx = 0;
            if (minCz < 0) minCz = 0;
            if (maxCx > _grid.Columns - 1) maxCx = _grid.Columns - 1;
            if (maxCz > _grid.Rows - 1) maxCz = _grid.Rows - 1;

            bestCx = 0;
            bestCz = 0;
            bool found = false;
            double bestDistSq = double.MaxValue;
            for (int cz = minCz; cz <= maxCz; cz++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    if (!_steerField.IsReached(cx, cz)) continue;
                    _grid.CellCenter(cx, cz, out float cxw, out float czw);
                    double dx = cxw - x;
                    double dz = czw - z;
                    double distSq = dx * dx + dz * dz;
                    if (found && !(distSq < bestDistSq)) continue;
                    found = true;
                    bestDistSq = distSq;
                    bestCx = cx;
                    bestCz = cz;
                }
            }
            return found;
        }

        private static void SetDirectionTo(float fromX, float fromZ, float toX, float toZ, out float dirX, out float dirZ)
        {
            double dx = toX - fromX;
            double dz = toZ - fromZ;
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len > 1e-9)
            {
                dirX = (float)(dx / len);
                dirZ = (float)(dz / len);
            }
            else
            {
                dirX = 0f;
                dirZ = 0f;
            }
        }
    }
}
