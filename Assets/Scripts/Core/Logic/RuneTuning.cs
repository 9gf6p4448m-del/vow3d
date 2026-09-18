using System;

namespace Vow.Core.Logic
{
    // 地脈符印與石牆數值的單一事實來源（GDD §參）。標「暫定」者 GDD 未給定，2026-09-19 使用者裁定先用暫定值、試玩即改。
    [Serializable]
    public sealed class RuneTuning
    {
        // ── 施法 ──
        public float CooldownSeconds = 8f;             // 暫定
        public float QuickCastDistance = 4f;           // 極速石牆：正前方 4 米
        public float DragMinDistance = 2f;             // 暫定：拖曳剛越過門檻時的落點距離
        public float DragMaxDistance = 8f;             // 暫定：拇指拉滿時的落點距離
        public float DragSaturationMillimeters = 14f;  // 暫定：拇指拉滿的物理行程（起點為微彈的 3.5mm 門檻）

        // ── 石牆 ──
        public float WallLifespanSeconds = 5f;
        public float WallMaxHealth = 300f;             // 暫定
        public float WallWidth = 4f;                   // 暫定
        public float WallThickness = 0.6f;             // 暫定
        public float WallHeight = 2f;                  // 暫定
        public int TeamWallCap = 2;                    // 全隊同時最多 2 面，第 3 面成形時最早的坍塌

        // ── 友軍彈道穿透（GDD §參-2 與 §捌 合併解讀：前 5 發不衰減、第 6~10 發固定 ×0.85、每發都損耗石牆）──
        public int MaxPenetrations = 10;
        public int UndecayedPenetrations = 5;
        public float DecayedDamageMultiplier = 0.85f;
        public float PenetrationHealthFraction = 0.10f;
        public float PenetrationLifespanCost = 0.5f;
    }
}
