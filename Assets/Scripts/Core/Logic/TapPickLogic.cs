namespace Vow.Core.Logic
{
    // 點擊挑選（§4-1）：射線的全部命中裡，跳過「己方石牆」，取剩下的最近者。
    // 「點自家牆＝點到牆後的地板」這個手感就是由這條規則產生的（使用者裁定 1）。
    // 被跳過的條件只看命中自身、與順序無關，所以呼叫端不需要先排序。
    public static class TapPickLogic
    {
        // distances／ownWall 為平行陣列。回傳「ownWall==false 的命中中距離最小者」的索引；全被跳過回 -1。
        // 同距離取索引小者（決定性）。
        public static int SelectNearestAcceptable(float[] distances, bool[] ownWall, int count)
        {
            if (distances == null || ownWall == null) return -1;
            if (count > distances.Length) count = distances.Length;
            if (count > ownWall.Length) count = ownWall.Length;

            int best = -1;
            float bestDistance = 0f;
            for (int i = 0; i < count; i++)
            {
                if (ownWall[i]) continue;
                if (best >= 0 && distances[i] >= bestDistance) continue;
                best = i;
                bestDistance = distances[i];
            }
            return best;
        }
    }
}
