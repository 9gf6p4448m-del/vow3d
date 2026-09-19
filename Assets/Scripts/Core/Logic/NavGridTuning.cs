using System;

namespace Vow.Core.Logic
{
    // 批 2 阻擋網格／整合場的全部數值單一來源（PHASE2_BATCH2_PLAN.md §1、§4）。
    // 格點涵蓋整個 40×40m 場地（80×80＝6400 格），界外一律視為 Blocked（§4-1）。
    [Serializable]
    public sealed class NavGridTuning
    {
        // ── 網格幾何（§5 V2-a 凍結：改這些值會讓 BlockedCount==40 那條測試連帶跟著變，數字照抄不得改）──
        public float OriginX = -20f;
        public float OriginZ = -20f;
        public float CellSize = 0.5f;
        public int Columns = 80;
        public int Rows = 80;

        // 外擴量＝英雄體型（§4-2：目前只有單一體型，寫死不做多體型表）。
        public float BodyRadius = 0.35f;

        // 逃脫／最近空格搜尋半徑（格數）。取捨：用「以此為半徑的方塊內暴力掃描」而非分環提前終止，
        // 正確性優先於效能——見驗收回報「取捨」一節。半徑 20 格＝10m，覆蓋單面/多面牆的外擴區綽綽有餘；
        // 真的搜不到才判 Stuck，不會把「暫時沒展開夠遠」誤判成走不到。
        public int EscapeSearchRadiusCells = 20;

        // Follow 模式沿整合場前視、嘗試拉直路徑的最多格數（§4 Simplicity 例外表：45° 鋸齒拉直）。
        public int FollowLookaheadCells = 8;

        // 「最近可達點」第三階段的成本寬限（§6 R8／R1a）：決定了哪一側之後，為了更靠近使用者點的位置，
        // 最多願意多走的路徑成本。28＝兩個斜步（整合場斜走成本 14），約 1.4m。
        public int SubstituteCostSlack = 28;
    }
}
