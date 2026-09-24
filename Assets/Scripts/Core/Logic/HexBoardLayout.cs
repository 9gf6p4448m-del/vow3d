namespace Vow.Core.Logic
{
    // 七塊六角形板塊的幾何（V080_CAPTURE_PLAN.md §1.2 E1～E5、E9／E10，2026-09-24 凍結，不得改動）。
    // 平頂朝向（頂點在 ±x 方向）；內切半徑改用二進位精確值 6.0625（97/16）代替 7·√3/2，
    // 讓邊界與共用邊中點能在 float32 下精確構造（理由見計畫 E2）。
    public static class HexBoardLayout
    {
        public const int TileCount = 7;
        public const float CircumRadius = 7f;
        public const float InRadius = 6.0625f;

        // 外圈相對的兩塊為基地（裁定 7；E5）。
        public const int BlueBaseTile = 4;
        public const int RedBaseTile = 1;

        // 索引：0 中央、1 北、2 東北、3 東南、4 南、5 西南、6 西北（E3）。
        private static readonly float[] CenterXs = { 0f, 0f, 10.5f, 10.5f, 0f, -10.5f, -10.5f };
        private static readonly float[] CenterZs = { 0f, 12.125f, 6.0625f, -6.0625f, -12.125f, -6.0625f, 6.0625f };

        // 六個頂點相對塔心，逆時針排列（供 ContainsPoint 的半平面測試用）；(±7,0)、(±3.5,±6.0625)。
        private static readonly float[] LocalVertexXs = { 7f, 3.5f, -3.5f, -7f, -3.5f, 3.5f };
        private static readonly float[] LocalVertexZs = { 0f, 6.0625f, 6.0625f, 0f, -6.0625f, -6.0625f };

        public static float CenterX(int tile) => CenterXs[tile];
        public static float CenterZ(int tile) => CenterZs[tile];

        public static float VertexX(int tile, int vertex) => CenterXs[tile] + LocalVertexXs[vertex];
        public static float VertexZ(int tile, int vertex) => CenterZs[tile] + LocalVertexZs[vertex];

        // 所在板塊；不在任何板塊上回 -1。邊界算在內；重疊時取索引小（迴圈由 0 往上找，第一個命中即回傳）。
        public static int TileAt(float x, float z)
        {
            for (int i = 0; i < TileCount; i++)
            {
                if (ContainsPoint(CenterXs[i], CenterZs[i], x, z)) return i;
            }
            return -1;
        }

        // 所在光圈（圓心＝塔心，半徑由呼叫端傳入——值的單一事實來源是 CaptureTuning.CircleRadius，
        // 本檔不重複定義）；不在任何光圈回 -1；邊界算在內；重疊時取索引小。
        public static int CircleAt(float x, float z, float radius)
        {
            float radiusSq = radius * radius;
            for (int i = 0; i < TileCount; i++)
            {
                float dx = x - CenterXs[i];
                float dz = z - CenterZs[i];
                if (dx * dx + dz * dz <= radiusSq) return i;
            }
            return -1;
        }

        // 凸六邊形的半平面測試：對每條有向邊 (Vi→Vi+1)，叉積 ≥ 0 表示點在該邊內側（含邊界）。
        // 全部六條都成立才算在六邊形內。座標皆為二進位有限小數，乘減不產生捨入誤差。
        private static bool ContainsPoint(float cx, float cz, float x, float z)
        {
            float dx = x - cx;
            float dz = z - cz;
            for (int i = 0; i < 6; i++)
            {
                int j = i + 1 == 6 ? 0 : i + 1;
                float ex = LocalVertexXs[j] - LocalVertexXs[i];
                float ez = LocalVertexZs[j] - LocalVertexZs[i];
                float px = dx - LocalVertexXs[i];
                float pz = dz - LocalVertexZs[i];
                float cross = ex * pz - ez * px;
                if (cross < 0f) return false;
            }
            return true;
        }
    }
}
