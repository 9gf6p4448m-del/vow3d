namespace Vow.Core.Logic
{
    // 佔領棋盤的不可變描述（V090_ENCIRCLE_PLAN.md §2.1-1，E1～E6、E20、E27）。零 UnityEngine。
    // 兩個靜態唯讀實例：
    //   V080Seven     ＝v0.8.0 七塊佈局（資料取自 HexBoardLayout 與 CaptureTuning 的 E7/E8 預設值），包夾與狂怒關閉，
    //                   只當純邏輯回歸夾具（E27）。
    //   V090Nineteen  ＝本批十九塊佈局（R＝4.375、h＝3.7890625，平頂），包夾與狂怒開啟。
    // 全部座標都是二進位有限小數，TileAt／CircleAt 的邊界在 float32 下可精確構造。
    public sealed class CaptureBoardSpec
    {
        private readonly float[] _centerXs;
        private readonly float[] _centerZs;
        private readonly float[] _localVertexXs;   // 六個頂點相對塔心，逆時針（半平面測試用）
        private readonly float[] _localVertexZs;
        private readonly int[] _neighborStart;     // 扁平相鄰表：tile i 的鄰居在 _neighbors[_neighborStart[i] .. _neighborStart[i+1])
        private readonly int[] _neighbors;
        private readonly int[] _blueMothers;       // 依復活優先序排列
        private readonly int[] _redMothers;
        private readonly float[] _blueMotherRespawnXs;
        private readonly float[] _blueMotherRespawnZs;
        private readonly float[] _redMotherRespawnXs;
        private readonly float[] _redMotherRespawnZs;
        private readonly float _blueEdgeRespawnX;
        private readonly float _blueEdgeRespawnZ;
        private readonly float _redEdgeRespawnX;
        private readonly float _redEdgeRespawnZ;

        public int TileCount { get; }
        public float CircumRadius { get; }
        public float InRadius { get; }
        public bool EncircleEnabled { get; }
        public bool RageEnabled { get; }

        private CaptureBoardSpec(
            float circumRadius, float inRadius,
            float[] centerXs, float[] centerZs, int[][] adjacency,
            int[] blueMothers, float[] blueMotherRespawnXs, float[] blueMotherRespawnZs,
            int[] redMothers, float[] redMotherRespawnXs, float[] redMotherRespawnZs,
            float blueEdgeRespawnX, float blueEdgeRespawnZ, float redEdgeRespawnX, float redEdgeRespawnZ,
            bool encircleEnabled, bool rageEnabled)
        {
            TileCount = centerXs.Length;
            CircumRadius = circumRadius;
            InRadius = inRadius;
            _centerXs = centerXs;
            _centerZs = centerZs;

            float half = circumRadius * 0.5f;
            _localVertexXs = new[] { circumRadius, half, -half, -circumRadius, -half, half };
            _localVertexZs = new[] { 0f, inRadius, inRadius, 0f, -inRadius, -inRadius };

            _neighborStart = new int[TileCount + 1];
            int total = 0;
            for (int i = 0; i < TileCount; i++) { _neighborStart[i] = total; total += adjacency[i].Length; }
            _neighborStart[TileCount] = total;
            _neighbors = new int[total];
            for (int i = 0; i < TileCount; i++)
            {
                for (int k = 0; k < adjacency[i].Length; k++) _neighbors[_neighborStart[i] + k] = adjacency[i][k];
            }

            _blueMothers = blueMothers;
            _blueMotherRespawnXs = blueMotherRespawnXs;
            _blueMotherRespawnZs = blueMotherRespawnZs;
            _redMothers = redMothers;
            _redMotherRespawnXs = redMotherRespawnXs;
            _redMotherRespawnZs = redMotherRespawnZs;
            _blueEdgeRespawnX = blueEdgeRespawnX;
            _blueEdgeRespawnZ = blueEdgeRespawnZ;
            _redEdgeRespawnX = redEdgeRespawnX;
            _redEdgeRespawnZ = redEdgeRespawnZ;
            EncircleEnabled = encircleEnabled;
            RageEnabled = rageEnabled;
        }

        public float CenterX(int tile) => _centerXs[tile];
        public float CenterZ(int tile) => _centerZs[tile];
        public float VertexX(int tile, int vertex) => _centerXs[tile] + _localVertexXs[vertex];
        public float VertexZ(int tile, int vertex) => _centerZs[tile] + _localVertexZs[vertex];

        public int NeighborCount(int tile) => _neighborStart[tile + 1] - _neighborStart[tile];
        public int Neighbor(int tile, int k) => _neighbors[_neighborStart[tile] + k];

        // 母板塊（side＝CaptureMatchLogic.BlueFactionId／RedFactionId），rank 0 起依復活優先序。
        public int MotherCount(int side) => side == CaptureMatchLogic.BlueFactionId ? _blueMothers.Length : _redMothers.Length;
        public int MotherTile(int side, int rank) => side == CaptureMatchLogic.BlueFactionId ? _blueMothers[rank] : _redMothers[rank];
        public float MotherRespawnX(int side, int rank) => side == CaptureMatchLogic.BlueFactionId ? _blueMotherRespawnXs[rank] : _redMotherRespawnXs[rank];
        public float MotherRespawnZ(int side, int rank) => side == CaptureMatchLogic.BlueFactionId ? _blueMotherRespawnZs[rank] : _redMotherRespawnZs[rank];
        public float EdgeRespawnX(int side) => side == CaptureMatchLogic.BlueFactionId ? _blueEdgeRespawnX : _redEdgeRespawnX;
        public float EdgeRespawnZ(int side) => side == CaptureMatchLogic.BlueFactionId ? _blueEdgeRespawnZ : _redEdgeRespawnZ;

        // 所在板塊；不在任何板塊上回 -1。邊界算在內；重疊時取索引小。
        public int TileAt(float x, float z)
        {
            for (int i = 0; i < TileCount; i++)
            {
                if (ContainsPoint(_centerXs[i], _centerZs[i], x, z)) return i;
            }
            return -1;
        }

        // 所在光圈（圓心＝塔心，半徑由呼叫端傳入，單一事實來源是 CaptureTuning.CircleRadius）；
        // 不在任何光圈回 -1；邊界算在內；重疊時取索引小。
        public int CircleAt(float x, float z, float radius)
        {
            float radiusSq = radius * radius;
            for (int i = 0; i < TileCount; i++)
            {
                float dx = x - _centerXs[i];
                float dz = z - _centerZs[i];
                if (dx * dx + dz * dz <= radiusSq) return i;
            }
            return -1;
        }

        // 凸六邊形半平面測試：對每條有向邊，叉積 ≥ 0 表示在內側（含邊界）。
        private bool ContainsPoint(float cx, float cz, float x, float z)
        {
            float dx = x - cx;
            float dz = z - cz;
            for (int i = 0; i < 6; i++)
            {
                int j = i + 1 == 6 ? 0 : i + 1;
                float ex = _localVertexXs[j] - _localVertexXs[i];
                float ez = _localVertexZs[j] - _localVertexZs[i];
                float px = dx - _localVertexXs[i];
                float pz = dz - _localVertexZs[i];
                if (ex * pz - ez * px < 0f) return false;
            }
            return true;
        }

        // ── v0.8.0 七塊夾具（E27）：包夾與狂怒關閉 ──
        public static readonly CaptureBoardSpec V080Seven = BuildV080Seven();

        // ── v0.9.0 十九塊（E1～E6、E20） ──
        public static readonly CaptureBoardSpec V090Nineteen = new CaptureBoardSpec(
            4.375f, 3.7890625f,
            // E3：0 中央；1～6 中圈北起順時針；7～18 外圈北起順時針。x＝6.5625·q、z＝3.7890625·(q＋2r)
            new[] { 0f, 0f, 6.5625f, 6.5625f, 0f, -6.5625f, -6.5625f,
                    0f, 6.5625f, 13.125f, 13.125f, 13.125f, 6.5625f, 0f, -6.5625f, -13.125f, -13.125f, -13.125f, -6.5625f },
            new[] { 0f, 7.578125f, 3.7890625f, -3.7890625f, -7.578125f, -3.7890625f, 3.7890625f,
                    15.15625f, 11.3671875f, 7.578125f, 0f, -7.578125f, -11.3671875f, -15.15625f, -11.3671875f, -7.578125f, 0f, 7.578125f, 11.3671875f },
            // E5：42 條邊
            new[]
            {
                new[] { 1, 2, 3, 4, 5, 6 },
                new[] { 0, 2, 6, 7, 8, 18 },
                new[] { 0, 1, 3, 8, 9, 10 },
                new[] { 0, 2, 4, 10, 11, 12 },
                new[] { 0, 3, 5, 12, 13, 14 },
                new[] { 0, 4, 6, 14, 15, 16 },
                new[] { 0, 1, 5, 16, 17, 18 },
                new[] { 1, 8, 18 },
                new[] { 1, 2, 7, 9 },
                new[] { 2, 8, 10 },
                new[] { 2, 3, 9, 11 },
                new[] { 3, 10, 12 },
                new[] { 3, 4, 11, 13 },
                new[] { 4, 12, 14 },
                new[] { 4, 5, 13, 15 },
                new[] { 5, 14, 16 },
                new[] { 5, 6, 15, 17 },
                new[] { 6, 16, 18 },
                new[] { 1, 6, 7, 17 },
            },
            // E6／E20：藍方母板塊 13→12→14，復活點＝塔心往 −z 平移 1.5
            new[] { 13, 12, 14 },
            new[] { 0f, 6.5625f, -6.5625f },
            new[] { -16.65625f, -12.8671875f, -12.8671875f },
            // 紅方母板塊 7→18→8，復活點＝塔心往 +z 平移 1.5
            new[] { 7, 18, 8 },
            new[] { 0f, -6.5625f, 6.5625f },
            new[] { 16.65625f, 12.8671875f, 12.8671875f },
            -17f, -17f, 17f, 17f,
            true, true);

        private static CaptureBoardSpec BuildV080Seven()
        {
            var tuning = new CaptureTuning();
            float[] xs = new float[HexBoardLayout.TileCount];
            float[] zs = new float[HexBoardLayout.TileCount];
            for (int i = 0; i < HexBoardLayout.TileCount; i++)
            {
                xs[i] = HexBoardLayout.CenterX(i);
                zs[i] = HexBoardLayout.CenterZ(i);
            }
            return new CaptureBoardSpec(
                HexBoardLayout.CircumRadius, HexBoardLayout.InRadius,
                xs, zs,
                new[]
                {
                    new[] { 1, 2, 3, 4, 5, 6 },
                    new[] { 0, 2, 6 },
                    new[] { 0, 1, 3 },
                    new[] { 0, 2, 4 },
                    new[] { 0, 3, 5 },
                    new[] { 0, 4, 6 },
                    new[] { 0, 1, 5 },
                },
                new[] { HexBoardLayout.BlueBaseTile },
                new[] { tuning.BlueHomeRespawnX },
                new[] { tuning.BlueHomeRespawnZ },
                new[] { HexBoardLayout.RedBaseTile },
                new[] { tuning.RedHomeRespawnX },
                new[] { tuning.RedHomeRespawnZ },
                tuning.BlueEdgeRespawnX, tuning.BlueEdgeRespawnZ, tuning.RedEdgeRespawnX, tuning.RedEdgeRespawnZ,
                false, false);
        }
    }
}
