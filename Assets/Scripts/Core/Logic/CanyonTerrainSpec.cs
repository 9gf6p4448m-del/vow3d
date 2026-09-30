using System;

namespace Vow.Core.Logic
{
    public enum CliffSegmentKind { FullEdge = 0, RampEdgeEnd = 1, RampSideWall = 2 }

    // v0.14.0 深淵峽谷的地形事實唯一來源（docs/V0140_CANYON_PLAN.md §2、§5.1、§7.1，2026-09-30 凍結）。
    // 層別、斜坡矩形、崖壁線段、地熱點、可走鄰接、HeightAt／ClassAt／IsSameFloor 全部從這裡讀；
    // 蓋格、場景碰撞體、巨獸路線、可達性測試都不得另外手抄座標。零 UnityEngine、建構後查詢零配置。
    //
    // 斜坡支援任意落差（使用者 2026-09-30 工程裁定）：每條斜坡的高度由兩端塊的層高決定，坡度＝落差／長度。
    // 崖壁線段一律由資料產生：相鄰兩塊高度不同且不是斜坡的共用邊＝完整崖壁；斜坡邊留置中開口、兩端各一段；
    // 每條坡道兩條長邊＝側牆。
    public sealed class CanyonTerrainSpec : ITerrainQuery
    {
        public const int VentCount = 2;

        // §2.1 逐塊層高：0,1,4＝谷底 −1；2,3,5,6＝崖台 +1；其餘中層 0。
        private static readonly float[] V0140TileHeights =
        {
            -1f, -1f, 1f, 1f, -1f, 1f, 1f,
            0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
        };
        // §2.2 6 處斜坡（低端塊 → 高端塊）：R0 9→2、R1 11→3、R2 15→5、R3 17→6、R4 1→7、R5 4→13。
        private static readonly int[] V0140RampLowTiles = { 9, 11, 15, 17, 1, 4 };
        private static readonly int[] V0140RampHighTiles = { 2, 3, 5, 6, 7, 13 };

        // §2.4 地熱點（G0 東北、G1 西南＝G0 的 180° 旋轉）。
        private static readonly float[] VentPadXs = { 2.09375f, -2.09375f };
        private static readonly float[] VentPadZs = { 2.0625f, -2.0625f };
        private static readonly float[] VentLandingXs = { 4.59375f, -4.59375f };
        private static readonly float[] VentLandingZs = { 2.0703125f, -2.0703125f };

        // 靜態唯讀實例：與 CaptureBoardSpec.V0140Canyon.Terrain 是同一個物件（以公開建構子建出）。
        public static CanyonTerrainSpec V0140 => (CanyonTerrainSpec)CaptureBoardSpec.V0140Canyon.Terrain;

        internal static CanyonTerrainSpec CreateV0140(CaptureBoardSpec board)
        {
            return new CanyonTerrainSpec(board, V0140TileHeights, V0140RampLowTiles, V0140RampHighTiles);
        }

        private readonly CaptureBoardSpec _board;
        private readonly float[] _tileHeights;

        private readonly int _rampCount;
        private readonly int[] _rampLow;
        private readonly int[] _rampHigh;
        private readonly float[] _rampCx;
        private readonly float[] _rampCz;
        private readonly float[] _rampAx;      // 長軸單位向量（低端 → 高端）
        private readonly float[] _rampAz;
        private readonly float[] _rampH0;      // 低端塊層高
        private readonly float[] _rampH1;      // 高端塊層高
        private readonly float[] _rampEdgeX0;  // 斜坡所在的共用邊（整條）
        private readonly float[] _rampEdgeZ0;
        private readonly float[] _rampEdgeX1;
        private readonly float[] _rampEdgeZ1;
        private readonly float _rampHalfLength;
        private readonly float _rampHalfWidth;

        // §5.2「崖壁格」＝點到任一崖壁線段的水平距離 ≤ NavGridTuning.BodyRadius（與蓋格外擴量相同）。
        private readonly double _cliffCellRadius;

        private readonly int[] _walkStart;
        private readonly int[] _walk;
        private readonly float[] _walkDistance;   // 全對最短走路距離（以塔心連線長為權重）
        private readonly int[] _bfsDepth;          // FindWalkRoute 的固定容量暫存
        private readonly int[] _bfsQueue;

        private readonly float[] _segX0;
        private readonly float[] _segZ0;
        private readonly float[] _segX1;
        private readonly float[] _segZ1;
        private readonly CliffSegmentKind[] _segKind;
        private readonly int[] _segRamp;          // 斜坡邊端／側牆所屬的斜坡索引；完整崖壁為 -1
        private readonly int _segCount;

        public CanyonTerrainSpec(CaptureBoardSpec board, float[] tileHeights, int[] rampLowTiles, int[] rampHighTiles)
        {
            var tuning = new CanyonTuning();
            _board = board;
            int n = board.TileCount;

            _tileHeights = new float[n];
            for (int i = 0; i < n; i++) _tileHeights[i] = tileHeights[i];

            _rampCount = rampLowTiles.Length;
            _rampLow = new int[_rampCount];
            _rampHigh = new int[_rampCount];
            _rampCx = new float[_rampCount];
            _rampCz = new float[_rampCount];
            _rampAx = new float[_rampCount];
            _rampAz = new float[_rampCount];
            _rampH0 = new float[_rampCount];
            _rampH1 = new float[_rampCount];
            _rampEdgeX0 = new float[_rampCount];
            _rampEdgeZ0 = new float[_rampCount];
            _rampEdgeX1 = new float[_rampCount];
            _rampEdgeZ1 = new float[_rampCount];
            _rampHalfLength = tuning.RampLength * 0.5f;
            _rampHalfWidth = tuning.RampWidth * 0.5f;
            _cliffCellRadius = new NavGridTuning().BodyRadius;

            for (int r = 0; r < _rampCount; r++)
            {
                int lo = rampLowTiles[r];
                int hi = rampHighTiles[r];
                _rampLow[r] = lo;
                _rampHigh[r] = hi;
                float lx = board.CenterX(lo), lz = board.CenterZ(lo);
                float hx = board.CenterX(hi), hz = board.CenterZ(hi);
                _rampCx[r] = (lx + hx) * 0.5f;
                _rampCz[r] = (lz + hz) * 0.5f;
                double ddx = (double)hx - lx;
                double ddz = (double)hz - lz;
                double len = Math.Sqrt(ddx * ddx + ddz * ddz);
                _rampAx[r] = (float)(ddx / len);
                _rampAz[r] = (float)(ddz / len);
                _rampH0[r] = _tileHeights[lo];
                _rampH1[r] = _tileHeights[hi];
                FindSharedEdge(board, lo, hi, out _rampEdgeX0[r], out _rampEdgeZ0[r], out _rampEdgeX1[r], out _rampEdgeZ1[r]);
            }

            // 可走鄰接：同層相鄰＋斜坡（雙向）。依原鄰接表順序（遞增）列出。
            _walkStart = new int[n + 1];
            int walkTotal = 0;
            for (int i = 0; i < n; i++)
            {
                _walkStart[i] = walkTotal;
                for (int k = 0; k < board.NeighborCount(i); k++)
                {
                    if (IsWalkPair(i, board.Neighbor(i, k))) walkTotal++;
                }
            }
            _walkStart[n] = walkTotal;
            _walk = new int[walkTotal];
            int w = 0;
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < board.NeighborCount(i); k++)
                {
                    int j = board.Neighbor(i, k);
                    if (IsWalkPair(i, j)) _walk[w++] = j;
                }
            }

            // 全對最短走路距離（Floyd–Warshall，double 累加後轉 float）。
            var dist = new double[n * n];
            for (int i = 0; i < n * n; i++) dist[i] = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                dist[i * n + i] = 0.0;
                for (int k = _walkStart[i]; k < _walkStart[i + 1]; k++)
                {
                    int j = _walk[k];
                    double ex = (double)board.CenterX(j) - board.CenterX(i);
                    double ez = (double)board.CenterZ(j) - board.CenterZ(i);
                    dist[i * n + j] = Math.Sqrt(ex * ex + ez * ez);
                }
            }
            for (int m = 0; m < n; m++)
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                    {
                        double via = dist[i * n + m] + dist[m * n + j];
                        if (via < dist[i * n + j]) dist[i * n + j] = via;
                    }
            _walkDistance = new float[n * n];
            for (int i = 0; i < n * n; i++) _walkDistance[i] = (float)dist[i];
            _bfsDepth = new int[n];
            _bfsQueue = new int[n];

            // 崖壁線段：完整崖壁 → 斜坡邊端 → 斜坡側牆。
            int pairCount = 0;
            for (int i = 0; i < n; i++) pairCount += board.NeighborCount(i);
            int capacity = pairCount / 2 + 4 * _rampCount;
            _segX0 = new float[capacity];
            _segZ0 = new float[capacity];
            _segX1 = new float[capacity];
            _segZ1 = new float[capacity];
            _segKind = new CliffSegmentKind[capacity];
            _segRamp = new int[capacity];
            int count = 0;

            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < board.NeighborCount(i); k++)
                {
                    int j = board.Neighbor(i, k);
                    if (j <= i) continue;
                    if (_tileHeights[i] == _tileHeights[j]) continue;
                    if (RampIndexOf(i, j) >= 0) continue;
                    FindSharedEdge(board, i, j, out float x0, out float z0, out float x1, out float z1);
                    PutSegment(count++, x0, z0, x1, z1, CliffSegmentKind.FullEdge, -1);
                }
            }

            // 斜坡邊端：共用邊（實際長度）上以斜坡中心為中點留寬 RampWidth 的開口，兩端各剩一段。
            for (int r = 0; r < _rampCount; r++)
            {
                double ex0 = _rampEdgeX0[r], ez0 = _rampEdgeZ0[r], ex1 = _rampEdgeX1[r], ez1 = _rampEdgeZ1[r];
                double edx = ex1 - ex0, edz = ez1 - ez0;
                double elen = Math.Sqrt(edx * edx + edz * edz);
                double ux = edx / elen, uz = edz / elen;
                double mx = (ex0 + ex1) * 0.5, mz = (ez0 + ez1) * 0.5;
                double half = _rampHalfWidth;
                PutSegment(count++, (float)ex0, (float)ez0, (float)(mx - ux * half), (float)(mz - uz * half),
                           CliffSegmentKind.RampEdgeEnd, r);
                PutSegment(count++, (float)(mx + ux * half), (float)(mz + uz * half), (float)ex1, (float)ez1,
                           CliffSegmentKind.RampEdgeEnd, r);
            }

            // 斜坡側牆：坡道兩條長邊（與長軸平行、距長軸 RampWidth/2、長 RampLength）。
            for (int r = 0; r < _rampCount; r++)
            {
                double ax = _rampAx[r], az = _rampAz[r];
                double px = -az, pz = ax;
                for (int s = -1; s <= 1; s += 2)
                {
                    double ox = _rampCx[r] + px * s * _rampHalfWidth;
                    double oz = _rampCz[r] + pz * s * _rampHalfWidth;
                    PutSegment(count++,
                               (float)(ox - ax * _rampHalfLength), (float)(oz - az * _rampHalfLength),
                               (float)(ox + ax * _rampHalfLength), (float)(oz + az * _rampHalfLength),
                               CliffSegmentKind.RampSideWall, r);
                }
            }

            _segCount = count;
        }

        public CaptureBoardSpec Board => _board;
        public int TileAt(float x, float z) => _board.TileAt(x, z);
        public float TileHeight(int tile) => _tileHeights[tile];

        public int RampCount => _rampCount;
        public int RampLowTile(int ramp) => _rampLow[ramp];
        public int RampHighTile(int ramp) => _rampHigh[ramp];
        public float RampCenterX(int ramp) => _rampCx[ramp];
        public float RampCenterZ(int ramp) => _rampCz[ramp];
        public float RampAxisX(int ramp) => _rampAx[ramp];
        public float RampAxisZ(int ramp) => _rampAz[ramp];
        public float RampWidth => _rampHalfWidth * 2f;
        public float RampLength => _rampHalfLength * 2f;

        public float VentPadX(int pad) => VentPadXs[pad];
        public float VentPadZ(int pad) => VentPadZs[pad];
        public float VentLandingX(int pad) => VentLandingXs[pad];
        public float VentLandingZ(int pad) => VentLandingZs[pad];

        // 板塊層級可走鄰接（同層相鄰＋斜坡，雙向）。
        public int WalkNeighborCount(int tile) => _walkStart[tile + 1] - _walkStart[tile];
        public int WalkNeighbor(int tile, int k) => _walk[_walkStart[tile] + k];

        // WalkNeighbor 圖上以塔心連線長為權重的最短路長；不可達為 +∞。
        public float WalkDistance(int fromTile, int toTile) => _walkDistance[fromTile * _board.TileCount + toTile];

        public int CliffSegmentCount => _segCount;

        public void GetCliffSegment(int i, out float x0, out float z0, out float x1, out float z1)
        {
            x0 = _segX0[i];
            z0 = _segZ0[i];
            x1 = _segX1[i];
            z1 = _segZ1[i];
        }

        public CliffSegmentKind GetCliffSegmentKind(int i) => _segKind[i];

        // ── ITerrainQuery（本批恆為單層） ──
        public int LayerCountAt(float x, float z) => 1;

        public int ResolveLayer(float x, float z, float currentY) => 0;

        // ①斜坡矩形內（|沿軸| ≤ 長/2、|橫向| ≤ 寬/2，含邊界；多條重疊取索引小）→ 沿軸線性內插；
        // ②否則所在塊的層高；③棋盤外 → 0。
        public float HeightAt(float x, float z, int layer)
        {
            int ramp = RampAt(x, z, out float t);
            if (ramp >= 0)
            {
                float h0 = _rampH0[ramp];
                float h1 = _rampH1[ramp];
                return h0 + (h1 - h0) * t;
            }
            int tile = _board.TileAt(x, z);
            return tile < 0 ? 0f : _tileHeights[tile];
        }

        // 斜坡內 → Ramp；否則依所在塊層別（低 Canyon／高 Cliff／中 Plain）；棋盤外 Plain。
        public TerrainClass ClassAt(float x, float z, int layer)
        {
            if (RampAt(x, z, out float _) >= 0) return TerrainClass.Ramp;
            int tile = _board.TileAt(x, z);
            if (tile < 0) return TerrainClass.Plain;
            return ClassOfTile(tile);
        }

        // §5.2（使用者 2026-09-30 裁定）：兩點都不在崖壁格，且水平線段 p0→p1 不穿崖。
        public bool IsSameFloor(float x0, float z0, int layer0, float x1, float z1, int layer1)
        {
            if (IsInCliffCell(x0, z0)) return false;
            if (IsInCliffCell(x1, z1)) return false;
            return !CrossesCliff(x0, z0, x1, z1);
        }

        // §5.2「崖壁格」：點到任一崖壁線段（有限線段）的水平距離 ≤ 0.35。純幾何、與格點無關。
        public bool IsInCliffCell(float x, float z)
        {
            double limitSq = _cliffCellRadius * _cliffCellRadius;
            for (int i = 0; i < _segCount; i++)
            {
                if (PointSegmentDistanceSq(x, z, _segX0[i], _segZ0[i], _segX1[i], _segZ1[i]) <= limitSq) return true;
            }
            return false;
        }

        // §9 共同遵守：線段 p0→p1（水平）與任一崖壁線段相交（含端點）即為真。
        public bool CrossesCliff(float x0, float z0, float x1, float z1)
        {
            for (int i = 0; i < _segCount; i++)
            {
                if (SegmentsIntersect(x0, z0, x1, z1, _segX0[i], _segZ0[i], _segX1[i], _segZ1[i])) return true;
            }
            return false;
        }

        // §7.2：逐段呼叫 grid.StampBox(中點, 法線, 半長, 0, inflate, delta)；進場 +1、離場 −1（引用計數）。
        public void StampCliffs(BlockGrid grid, float inflate, int delta)
        {
            for (int i = 0; i < _segCount; i++)
            {
                StampSegment(grid, _segX0[i], _segZ0[i], _segX1[i], _segZ1[i], inflate, delta);
            }
        }

        // 巨獸路線：WalkNeighbor 上的 BFS，同步數取索引小者為父。buffer 由呼叫端提供（零配置），
        // 寫入 [start, …, dest]，回傳長度；不可達或緩衝不足回 0。
        public int FindWalkRoute(int start, int dest, int[] buffer)
        {
            int n = _board.TileCount;
            if (buffer == null || start < 0 || start >= n || dest < 0 || dest >= n) return 0;
            for (int i = 0; i < n; i++) _bfsDepth[i] = -1;
            int head = 0, tail = 0;
            _bfsDepth[start] = 0;
            _bfsQueue[tail++] = start;
            while (head < tail)
            {
                int cur = _bfsQueue[head++];
                for (int k = _walkStart[cur]; k < _walkStart[cur + 1]; k++)
                {
                    int nb = _walk[k];
                    if (_bfsDepth[nb] >= 0) continue;
                    _bfsDepth[nb] = _bfsDepth[cur] + 1;
                    _bfsQueue[tail++] = nb;
                }
            }

            int depth = _bfsDepth[dest];
            if (depth < 0 || depth + 1 > buffer.Length) return 0;
            int node = dest;
            for (int d = depth; d > 0; d--)
            {
                buffer[d] = node;
                for (int k = _walkStart[node]; k < _walkStart[node + 1]; k++)
                {
                    int nb = _walk[k];
                    if (_bfsDepth[nb] == d - 1)
                    {
                        node = nb;
                        break;
                    }
                }
            }
            buffer[0] = start;
            return depth + 1;
        }

        private TerrainClass ClassOfTile(int tile)
        {
            float h = _tileHeights[tile];
            if (h < 0f) return TerrainClass.Canyon;
            if (h > 0f) return TerrainClass.Cliff;
            return TerrainClass.Plain;
        }

        private int RampAt(float x, float z, out float t)
        {
            for (int r = 0; r < _rampCount; r++)
            {
                float dx = x - _rampCx[r];
                float dz = z - _rampCz[r];
                float along = dx * _rampAx[r] + dz * _rampAz[r];
                float lateral = dz * _rampAx[r] - dx * _rampAz[r];
                if (Abs(along) <= _rampHalfLength && Abs(lateral) <= _rampHalfWidth)
                {
                    t = (along + _rampHalfLength) / (_rampHalfLength * 2f);
                    return r;
                }
            }
            t = 0f;
            return -1;
        }

        private bool IsWalkPair(int a, int b)
        {
            return _tileHeights[a] == _tileHeights[b] || RampIndexOf(a, b) >= 0;
        }

        private int RampIndexOf(int a, int b)
        {
            for (int r = 0; r < _rampCount; r++)
            {
                if ((_rampLow[r] == a && _rampHigh[r] == b) || (_rampLow[r] == b && _rampHigh[r] == a)) return r;
            }
            return -1;
        }

        private void PutSegment(int index, float x0, float z0, float x1, float z1, CliffSegmentKind kind, int ramp)
        {
            _segX0[index] = x0;
            _segZ0[index] = z0;
            _segX1[index] = x1;
            _segZ1[index] = z1;
            _segKind[index] = kind;
            _segRamp[index] = ramp;
        }

        private static void StampSegment(BlockGrid grid, float x0, float z0, float x1, float z1, float inflate, int delta)
        {
            double dx = (double)x1 - x0;
            double dz = (double)z1 - z0;
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len <= 0.0) return;
            grid.StampBox((x0 + x1) * 0.5f, (z0 + z1) * 0.5f, (float)(-dz / len), (float)(dx / len),
                          (float)(len * 0.5), 0f, inflate, delta);
        }

        // 兩塊相鄰六角的共用邊（兩個共同頂點）；依 a 的頂點序取先出現者為起點。
        private static void FindSharedEdge(CaptureBoardSpec board, int a, int b,
                                           out float x0, out float z0, out float x1, out float z1)
        {
            x0 = z0 = x1 = z1 = 0f;
            int found = 0;
            for (int i = 0; i < 6 && found < 2; i++)
            {
                float vx = board.VertexX(a, i);
                float vz = board.VertexZ(a, i);
                for (int j = 0; j < 6; j++)
                {
                    if (Abs(board.VertexX(b, j) - vx) > 1e-3f || Abs(board.VertexZ(b, j) - vz) > 1e-3f) continue;
                    if (found == 0) { x0 = vx; z0 = vz; }
                    else { x1 = vx; z1 = vz; }
                    found++;
                    break;
                }
            }
            if (found != 2) throw new ArgumentException("tiles " + a + " and " + b + " do not share an edge");
        }

        private static double PointSegmentDistanceSq(double px, double pz, double ax, double az, double bx, double bz)
        {
            double abx = bx - ax, abz = bz - az;
            double apx = px - ax, apz = pz - az;
            double lenSq = abx * abx + abz * abz;
            double t = lenSq > 0.0 ? (apx * abx + apz * abz) / lenSq : 0.0;
            if (t < 0.0) t = 0.0;
            else if (t > 1.0) t = 1.0;
            double cx = ax + abx * t - px;
            double cz = az + abz * t - pz;
            return cx * cx + cz * cz;
        }

        private static bool SegmentsIntersect(double ax, double az, double bx, double bz,
                                              double cx, double cz, double dx, double dz)
        {
            double d1 = Orient(cx, cz, dx, dz, ax, az);
            double d2 = Orient(cx, cz, dx, dz, bx, bz);
            double d3 = Orient(ax, az, bx, bz, cx, cz);
            double d4 = Orient(ax, az, bx, bz, dx, dz);
            if (((d1 > 0.0 && d2 < 0.0) || (d1 < 0.0 && d2 > 0.0)) &&
                ((d3 > 0.0 && d4 < 0.0) || (d3 < 0.0 && d4 > 0.0))) return true;
            if (d1 == 0.0 && OnSegment(cx, cz, dx, dz, ax, az)) return true;
            if (d2 == 0.0 && OnSegment(cx, cz, dx, dz, bx, bz)) return true;
            if (d3 == 0.0 && OnSegment(ax, az, bx, bz, cx, cz)) return true;
            if (d4 == 0.0 && OnSegment(ax, az, bx, bz, dx, dz)) return true;
            return false;
        }

        private static double Orient(double ax, double az, double bx, double bz, double px, double pz)
        {
            return (bx - ax) * (pz - az) - (bz - az) * (px - ax);
        }

        // 已知 p 與 a→b 共線時，p 是否落在 a、b 圍成的包圍盒內（含端點）。
        private static bool OnSegment(double ax, double az, double bx, double bz, double px, double pz)
        {
            return px >= Math.Min(ax, bx) && px <= Math.Max(ax, bx) && pz >= Math.Min(az, bz) && pz <= Math.Max(az, bz);
        }

        private static float Abs(float v) => v < 0f ? -v : v;
    }
}
