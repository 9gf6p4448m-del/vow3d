using System;

namespace Vow.Core.Logic
{
    public enum SteerMode
    {
        Direct,
        Follow,
        Stuck
    }

    // HeroLocomotion 唯一會碰的入口：目標解析（走不到就退到最近可達點）＋逐幀轉向（有視線就照舊、被擋才繞牆）。
    // 不依賴 UnityEngine、不用 Linq、建構後執行期零配置（內部兩個 FlowField 只在建構時配置一次）。
    public sealed class GridNavigator
    {
        private readonly BlockGrid _grid;
        private readonly NavGridTuning _tuning;

        // 以「呼叫端傳入的 goal」為源；Steer 與 ResolveGoal 的可達性檢查共用同一份，
        // 只在（goal 格、格點版本）改變時才重建（§4-4）。
        private readonly FlowField _goalField;

        // 只在目的地走不到時才用：以 from 所在連通區為源，探出整個連通區供「最近可達點」比對。
        private readonly FlowField _componentField;

        public GridNavigator(BlockGrid grid, NavGridTuning tuning)
        {
            _grid = grid;
            _tuning = tuning;
            _goalField = new FlowField(grid);
            _componentField = new FlowField(grid);
        }

        public BlockGrid Grid => _grid;

        // 目的地那格非 Blocked 且從 (fromX,fromZ) 走得到 → 原樣回傳、substituted=false。
        // 否則回傳「from 所在連通區內、格心離目的地歐氏距離最近」那一格的格心、substituted=true。
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

            int bestCx = connCx;
            int bestCz = connCz;
            bool found = false;
            double bestDistSq = double.MaxValue;
            for (int cz = 0; cz < _grid.Rows; cz++)
            {
                for (int cx = 0; cx < _grid.Columns; cx++)
                {
                    if (!_componentField.IsReached(cx, cz)) continue;
                    _grid.CellCenter(cx, cz, out float cxWorld, out float czWorld);
                    double dx = cxWorld - destX;
                    double dz = czWorld - destZ;
                    double distSq = dx * dx + dz * dz;
                    if (found && !(distSq < bestDistSq)) continue;
                    found = true;
                    bestDistSq = distSq;
                    bestCx = cx;
                    bestCz = cz;
                }
            }

            _grid.CellCenter(bestCx, bestCz, out goalX, out goalZ);
            substituted = true;
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
            if (_grid.IsBlocked(goalCx, goalCz)) return SteerMode.Stuck; // 呼叫端理應先用 ResolveGoal 避免這種 goal

            EnsureBuilt(_goalField, goalCx, goalCz);

            _grid.TryWorldToCell(x, z, out int cx, out int cz);
            if (_grid.IsBlocked(cx, cz) || !_goalField.IsReached(cx, cz))
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
                if (!_goalField.TryGetNext(curCx, curCz, out int nx, out int nz)) break;
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
            }
        }

        // 以歐氏距離找「_goalField 裡已到達」的最近格；同距離取索引較小者（與 BlockGrid.TryFindNearestFree 同一決定性規則）。
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
                    if (!_goalField.IsReached(cx, cz)) continue;
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
