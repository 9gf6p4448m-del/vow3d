using System;

namespace Vow.Core.Logic
{
    // 0.5m 阻擋網格：符印石牆／測試牆存活期間登記自己的 OBB，其餘時間格點全空（PHASE2_BATCH2_PLAN.md §2）。
    // 逐格「引用計數」而非 bool，讓兩面重疊的牆各自撤銷時不會誤判成空格；界外一律視為 Blocked。
    // 不依賴 UnityEngine、不用 Linq、建構後執行期零配置。
    public sealed class BlockGrid
    {
        private readonly float _originX;
        private readonly float _originZ;
        private readonly float _cellSize;
        private readonly int _columns;
        private readonly int _rows;
        private readonly int[] _refCount; // 逐格引用計數；>0 視為 Blocked

        public BlockGrid(float originX, float originZ, float cellSize, int columns, int rows)
        {
            _originX = originX;
            _originZ = originZ;
            _cellSize = cellSize;
            _columns = columns;
            _rows = rows;
            _refCount = new int[columns * rows];
        }

        public int Columns => _columns;
        public int Rows => _rows;

        // 只有「某格的 Blocked 狀態翻轉」才 +1；同一格的計數在 1↔2 之間變動不算翻轉。
        public int Version { get; private set; }

        public int BlockedCount { get; private set; }

        // r1 對抗審查 M1：撤銷不對稱（撤兩次、或撤了從沒登記過的格）而被夾回 0 的次數。
        // 正常運作恆為 0；>0 代表某條離場路徑重複撤銷，是「壞了要叫」用的。
        public int NegativeStampCount { get; private set; }

        public bool IsBlocked(int cx, int cz)
        {
            if (cx < 0 || cx >= _columns || cz < 0 || cz >= _rows) return true; // 界外一律 Blocked
            return _refCount[Index(cx, cz)] > 0;
        }

        public bool TryWorldToCell(float x, float z, out int cx, out int cz)
        {
            cx = FloorToInt((x - _originX) / _cellSize);
            cz = FloorToInt((z - _originZ) / _cellSize);
            return cx >= 0 && cx < _columns && cz >= 0 && cz < _rows;
        }

        public void CellCenter(int cx, int cz, out float x, out float z)
        {
            x = _originX + (cx + 0.5f) * _cellSize;
            z = _originZ + (cz + 0.5f) * _cellSize;
        }

        // 把一個世界座標夾進格點涵蓋範圍（夾到最外圈格心，保證 TryWorldToCell 必定成功）。
        // r1 對抗審查 H3（§6 R2）：界外的目的地不得讓整趟導航退回 Phase 1——地板與格點都恰好 ±20，
        // 點在最外那條線上就會踩到；夾進來之後照常解析，繞牆行為不因為多按了半公尺而消失。
        public void ClampToGrid(float x, float z, out float clampedX, out float clampedZ)
        {
            clampedX = Clamp(x, _originX + _cellSize * 0.5f, _originX + (_columns - 0.5f) * _cellSize);
            clampedZ = Clamp(z, _originZ + _cellSize * 0.5f, _originZ + (_rows - 0.5f) * _cellSize);
        }

        // 牆的 OBB（中心、法線、半寬＝沿牆方向、半厚＝沿法線方向）各向外擴 inflate 後，
        // 「格子方塊與它相交」（精確 SAT，不是格心取樣）的每一格計數 += delta。界外部分直接裁掉、不丟例外。
        public void StampBox(float centerX, float centerZ, float normalX, float normalZ,
                             float halfWidth, float halfThickness, float inflate, int delta)
        {
            double len = Math.Sqrt((double)normalX * normalX + (double)normalZ * normalZ);
            float nx, nz;
            if (len > 1e-9)
            {
                nx = (float)(normalX / len);
                nz = (float)(normalZ / len);
            }
            else
            {
                nx = 0f;
                nz = 1f; // 退化法線的防禦性預設，正常呼叫端永遠給單位向量
            }
            float tx = -nz;
            float tz = nx;

            float halfW = halfWidth + inflate;
            float halfT = halfThickness + inflate;
            if (halfW < 0f) halfW = 0f;
            if (halfT < 0f) halfT = 0f;

            float extentX = halfW * Abs(tx) + halfT * Abs(nx);
            float extentZ = halfW * Abs(tz) + halfT * Abs(nz);

            int cxMin = FloorToInt((centerX - extentX - _originX) / _cellSize);
            int cxMax = FloorToInt((centerX + extentX - _originX) / _cellSize);
            int czMin = FloorToInt((centerZ - extentZ - _originZ) / _cellSize);
            int czMax = FloorToInt((centerZ + extentZ - _originZ) / _cellSize);

            if (cxMin < 0) cxMin = 0;
            if (czMin < 0) czMin = 0;
            if (cxMax > _columns - 1) cxMax = _columns - 1;
            if (czMax > _rows - 1) czMax = _rows - 1;

            float half = _cellSize * 0.5f;
            for (int cz = czMin; cz <= czMax; cz++)
            {
                for (int cx = cxMin; cx <= cxMax; cx++)
                {
                    CellCenter(cx, cz, out float ccx, out float ccz);
                    if (!CellOverlapsObb(ccx, ccz, half, centerX, centerZ, nx, nz, halfW, halfT)) continue;

                    int idx = Index(cx, cz);
                    int before = _refCount[idx];
                    int after = before + delta;
                    _refCount[idx] = after;

                    // M1：計數不得為負。不夾的話那一格會變成「存在但不擋路」——下一面真的牆 +1 上去仍然是 0，
                    // Blocked 狀態不翻轉、Version 不動、整合場不重建，壞了不會叫（審查 M1 實跑）。
                    if (after < 0)
                    {
                        after = 0;
                        _refCount[idx] = 0;
                        NegativeStampCount++;
                    }

                    bool wasBlocked = before > 0;
                    bool isBlocked = after > 0;
                    if (wasBlocked == isBlocked) continue;

                    Version++;
                    BlockedCount += isBlocked ? 1 : -1;
                }
            }
        }

        // 線段經過的每一格（supercover：擦過格角也算經過）都不是 Blocked 才回傳 true。兩端點同格：trivially true。
        public bool HasLineOfSight(float x0, float z0, float x1, float z1)
        {
            double fx0 = (x0 - _originX) / _cellSize;
            double fz0 = (z0 - _originZ) / _cellSize;
            double fx1 = (x1 - _originX) / _cellSize;
            double fz1 = (z1 - _originZ) / _cellSize;

            int cx0 = (int)Math.Floor(fx0);
            int cz0 = (int)Math.Floor(fz0);
            int cx1 = (int)Math.Floor(fx1);
            int cz1 = (int)Math.Floor(fz1);

            if (cx0 == cx1 && cz0 == cz1) return true; // 同格：trivially 有視線

            double dx = fx1 - fx0;
            double dz = fz1 - fz0;

            int stepX = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int stepZ = dz > 0 ? 1 : (dz < 0 ? -1 : 0);

            double tDeltaX = dx != 0 ? Math.Abs(1.0 / dx) : double.PositiveInfinity;
            double tDeltaZ = dz != 0 ? Math.Abs(1.0 / dz) : double.PositiveInfinity;

            double boundaryX = stepX > 0 ? cx0 + 1 : cx0;
            double boundaryZ = stepZ > 0 ? cz0 + 1 : cz0;
            double tMaxX = dx != 0 ? (boundaryX - fx0) / dx : double.PositiveInfinity;
            double tMaxZ = dz != 0 ? (boundaryZ - fz0) / dz : double.PositiveInfinity;

            const double tie = 1e-9;
            int x = cx0, z = cz0;
            if (IsBlocked(x, z)) return false;

            // 走到終點格為止；每步至多推進一格（含 tie 時的角落三格），避免浮點誤差造成無限迴圈。
            int guard = (_columns + _rows) * 2 + 4;
            while ((x != cx1 || z != cz1) && guard-- > 0)
            {
                // 線段已經走完（下一個格界都落在 t>1 之外）＝終點就在這一格裡，不再往外走。
                // 兩個端點都恰好落在格角上時（例如格心對格心的整數座標），DDA 有可能永遠對不上終點格，
                // 一路走到格點外 —— 界外一律 Blocked，於是空場也被判成「沒有視線」。
                if (tMaxX > 1.0 && tMaxZ > 1.0) break;

                if (Math.Abs(tMaxX - tMaxZ) < tie && tMaxX < double.PositiveInfinity)
                {
                    // 線段精準穿過格角：連同 x-only／z-only 鄰格一起算「擦過」
                    int nx1 = x + stepX;
                    int nz1 = z + stepZ;
                    if (IsBlocked(nx1, z)) return false;
                    if (IsBlocked(x, nz1)) return false;
                    if (IsBlocked(nx1, nz1)) return false;
                    x = nx1;
                    z = nz1;
                    tMaxX += tDeltaX;
                    tMaxZ += tDeltaZ;
                }
                else if (tMaxX < tMaxZ)
                {
                    x += stepX;
                    tMaxX += tDeltaX;
                    if (IsBlocked(x, z)) return false;
                }
                else
                {
                    z += stepZ;
                    tMaxZ += tDeltaZ;
                    if (IsBlocked(x, z)) return false;
                }
            }
            // 走完了才提前跳出（上面的 t>1 或 guard）時，終點格自己還沒被檢查過。
            return !IsBlocked(cx1, cz1);
        }

        // 以歐氏距離（到格心）找最近的非 Blocked 格；同距離取索引較小者（決定性）。
        // 在以 (x,z) 所在格為中心、半徑 maxRadiusCells 的方塊內做完整比對（正確性優先於效能，見驗收回報）。
        public bool TryFindNearestFree(float x, float z, int maxRadiusCells, out int cx, out int cz)
        {
            int ocx = FloorToInt((x - _originX) / _cellSize);
            int ocz = FloorToInt((z - _originZ) / _cellSize);

            int minCx = ocx - maxRadiusCells;
            int maxCx = ocx + maxRadiusCells;
            int minCz = ocz - maxRadiusCells;
            int maxCz = ocz + maxRadiusCells;
            if (minCx < 0) minCx = 0;
            if (minCz < 0) minCz = 0;
            if (maxCx > _columns - 1) maxCx = _columns - 1;
            if (maxCz > _rows - 1) maxCz = _rows - 1;

            cx = 0;
            cz = 0;
            bool found = false;
            double bestDistSq = double.MaxValue;

            for (int gz = minCz; gz <= maxCz; gz++)
            {
                for (int gx = minCx; gx <= maxCx; gx++)
                {
                    if (IsBlocked(gx, gz)) continue;
                    CellCenter(gx, gz, out float ccx, out float ccz);
                    double ddx = ccx - x;
                    double ddz = ccz - z;
                    double distSq = ddx * ddx + ddz * ddz;
                    if (found && !(distSq < bestDistSq)) continue;
                    found = true;
                    bestDistSq = distSq;
                    cx = gx;
                    cz = gz;
                }
            }
            return found;
        }

        // 圓（點＋半徑）對 OBB 是否有實體重疊；無 inflate，呼叫端要外擴的話自己把半徑或盒子加大再傳進來。
        public static bool CircleOverlapsBox(float px, float pz, float radius, float centerX, float centerZ,
                                             float normalX, float normalZ, float halfWidth, float halfThickness)
        {
            double len = Math.Sqrt((double)normalX * normalX + (double)normalZ * normalZ);
            float nx, nz;
            if (len > 1e-9)
            {
                nx = (float)(normalX / len);
                nz = (float)(normalZ / len);
            }
            else
            {
                nx = 0f;
                nz = 1f;
            }
            float tx = -nz;
            float tz = nx;

            float dx = px - centerX;
            float dz = pz - centerZ;
            float projT = dx * tx + dz * tz;
            float projN = dx * nx + dz * nz;

            float clampedT = Clamp(projT, -halfWidth, halfWidth);
            float clampedN = Clamp(projN, -halfThickness, halfThickness);

            float diffT = projT - clampedT;
            float diffN = projN - clampedN;
            float distSq = diffT * diffT + diffN * diffN;
            return distSq <= radius * radius;
        }

        // 軸對齊格子（半邊長 cellHalf）是否與旋轉 OBB 相交：4 軸 SAT（cell 的 x/z 兩軸 ＋ 牆的 normal/tangent 兩軸）。
        private static bool CellOverlapsObb(float cellCx, float cellCz, float cellHalf,
                                            float centerX, float centerZ, float nx, float nz,
                                            float halfWidth, float halfThickness)
        {
            float tx = -nz;
            float tz = nx;
            float dx = cellCx - centerX;
            float dz = cellCz - centerZ;

            float wallExtentX = halfWidth * Abs(tx) + halfThickness * Abs(nx);
            if (Abs(dx) > cellHalf + wallExtentX) return false;

            float wallExtentZ = halfWidth * Abs(tz) + halfThickness * Abs(nz);
            if (Abs(dz) > cellHalf + wallExtentZ) return false;

            float projN = dx * nx + dz * nz;
            float cellExtentN = cellHalf * (Abs(nx) + Abs(nz));
            if (Abs(projN) > cellExtentN + halfThickness) return false;

            float projT = dx * tx + dz * tz;
            float cellExtentT = cellHalf * (Abs(tx) + Abs(tz));
            if (Abs(projT) > cellExtentT + halfWidth) return false;

            return true;
        }

        private int Index(int cx, int cz) => cz * _columns + cx;

        private static int FloorToInt(double value) => (int)Math.Floor(value);

        private static float Abs(float v) => v < 0f ? -v : v;

        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
