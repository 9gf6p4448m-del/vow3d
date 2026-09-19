using System;

namespace Vow.Core.Logic
{
    // 以目的地為源的整合場（Dijkstra，反向擴散）：8 鄰接，直走成本 10、斜走 14，斜走時兩個正交鄰格
    // 任一 Blocked 就禁止切角。陣列與二元堆在建構時一次配置好，Build 之後執行期零配置（PHASE2_BATCH2_PLAN.md §2）。
    public sealed class FlowField
    {
        private static readonly int[] NeighborDx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] NeighborDz = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private static readonly int[] NeighborCost = { 10, 10, 10, 10, 14, 14, 14, 14 };

        private readonly BlockGrid _grid;
        private readonly int _columns;
        private readonly int _rows;
        private readonly int[] _cost;
        private readonly bool[] _closed;

        // 二元堆（平行陣列，避免配置 struct 陣列）。每格最多被 8 個鄰格各鬆弛一次，容量抓 8×格數的上界。
        private readonly int[] _heapCost;
        private readonly int[] _heapCx;
        private readonly int[] _heapCz;
        private int _heapSize;

        public FlowField(BlockGrid grid)
        {
            _grid = grid;
            _columns = grid.Columns;
            _rows = grid.Rows;
            int cellCount = _columns * _rows;
            _cost = new int[cellCount];
            _closed = new bool[cellCount];
            int heapCapacity = cellCount * 8 + 16; // 上界：每格至多因 8 個鄰格各鬆弛一次而被推入一次，留餘裕
            _heapCost = new int[heapCapacity];
            _heapCx = new int[heapCapacity];
            _heapCz = new int[heapCapacity];

            BuiltForVersion = -1;
            GoalCx = -1;
            GoalCz = -1;
            for (int i = 0; i < cellCount; i++) _cost[i] = int.MaxValue;
        }

        public int LastBuildPopCount { get; private set; }
        public int BuiltForVersion { get; private set; }
        public int GoalCx { get; private set; }
        public int GoalCz { get; private set; }

        public void Build(int goalCx, int goalCz)
        {
            int cellCount = _columns * _rows;
            for (int i = 0; i < cellCount; i++)
            {
                _cost[i] = int.MaxValue;
                _closed[i] = false;
            }
            _heapSize = 0;
            LastBuildPopCount = 0;
            BuiltForVersion = _grid.Version;
            GoalCx = goalCx;
            GoalCz = goalCz;

            if (_grid.IsBlocked(goalCx, goalCz)) return; // 目標本身不可站立：留空場，全部視為未到達

            int goalIdx = Index(goalCx, goalCz);
            _cost[goalIdx] = 0;
            HeapPush(0, goalCx, goalCz);

            while (_heapSize > 0)
            {
                HeapPop(out int cost, out int cx, out int cz);
                int idx = Index(cx, cz);
                if (_closed[idx]) continue; // 過期的重複堆項，不計入出堆次數
                _closed[idx] = true;
                LastBuildPopCount++;

                for (int n = 0; n < 8; n++)
                {
                    int ncx = cx + NeighborDx[n];
                    int ncz = cz + NeighborDz[n];
                    if (_grid.IsBlocked(ncx, ncz)) continue;

                    bool diagonal = NeighborDx[n] != 0 && NeighborDz[n] != 0;
                    if (diagonal)
                    {
                        if (_grid.IsBlocked(cx + NeighborDx[n], cz)) continue; // 禁止切角
                        if (_grid.IsBlocked(cx, cz + NeighborDz[n])) continue;
                    }

                    int nIdx = Index(ncx, ncz);
                    if (_closed[nIdx]) continue;

                    int newCost = cost + NeighborCost[n];
                    if (newCost < _cost[nIdx])
                    {
                        _cost[nIdx] = newCost;
                        HeapPush(newCost, ncx, ncz);
                    }
                }
            }
        }

        public bool IsReached(int cx, int cz)
        {
            if (cx < 0 || cx >= _columns || cz < 0 || cz >= _rows) return false;
            return _cost[Index(cx, cz)] != int.MaxValue;
        }

        public int CostAt(int cx, int cz)
        {
            if (cx < 0 || cx >= _columns || cz < 0 || cz >= _rows) return int.MaxValue;
            return _cost[Index(cx, cz)];
        }

        // 成本嚴格遞減的合法鄰格中，成本最低者；同成本取索引較小者。已在目標格（成本 0）時無解，回傳 false。
        public bool TryGetNext(int cx, int cz, out int nx, out int nz)
        {
            nx = 0;
            nz = 0;
            if (!IsReached(cx, cz)) return false;
            int currentCost = CostAt(cx, cz);

            bool found = false;
            int bestCost = int.MaxValue;
            int bestIndex = int.MaxValue;
            int bestCx = 0, bestCz = 0;

            for (int n = 0; n < 8; n++)
            {
                int ncx = cx + NeighborDx[n];
                int ncz = cz + NeighborDz[n];
                if (_grid.IsBlocked(ncx, ncz)) continue;

                bool diagonal = NeighborDx[n] != 0 && NeighborDz[n] != 0;
                if (diagonal)
                {
                    if (_grid.IsBlocked(cx + NeighborDx[n], cz)) continue;
                    if (_grid.IsBlocked(cx, cz + NeighborDz[n])) continue;
                }

                if (!IsReached(ncx, ncz)) continue;
                int c = CostAt(ncx, ncz);
                if (c >= currentCost) continue; // 只接受嚴格更低的成本

                int idx = Index(ncx, ncz);
                if (c < bestCost || (c == bestCost && idx < bestIndex))
                {
                    found = true;
                    bestCost = c;
                    bestIndex = idx;
                    bestCx = ncx;
                    bestCz = ncz;
                }
            }

            if (!found) return false;
            nx = bestCx;
            nz = bestCz;
            return true;
        }

        private int Index(int cx, int cz) => cz * _columns + cx;

        private void HeapPush(int cost, int cx, int cz)
        {
            int i = _heapSize++;
            _heapCost[i] = cost;
            _heapCx[i] = cx;
            _heapCz[i] = cz;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_heapCost[parent] <= _heapCost[i]) break;
                Swap(parent, i);
                i = parent;
            }
        }

        private void HeapPop(out int cost, out int cx, out int cz)
        {
            cost = _heapCost[0];
            cx = _heapCx[0];
            cz = _heapCz[0];

            _heapSize--;
            _heapCost[0] = _heapCost[_heapSize];
            _heapCx[0] = _heapCx[_heapSize];
            _heapCz[0] = _heapCz[_heapSize];

            int i = 0;
            while (true)
            {
                int left = i * 2 + 1;
                int right = i * 2 + 2;
                int smallest = i;
                if (left < _heapSize && _heapCost[left] < _heapCost[smallest]) smallest = left;
                if (right < _heapSize && _heapCost[right] < _heapCost[smallest]) smallest = right;
                if (smallest == i) break;
                Swap(i, smallest);
                i = smallest;
            }
        }

        private void Swap(int a, int b)
        {
            int tc = _heapCost[a]; _heapCost[a] = _heapCost[b]; _heapCost[b] = tc;
            int tx = _heapCx[a]; _heapCx[a] = _heapCx[b]; _heapCx[b] = tx;
            int tz = _heapCz[a]; _heapCz[a] = _heapCz[b]; _heapCz[b] = tz;
        }
    }
}
