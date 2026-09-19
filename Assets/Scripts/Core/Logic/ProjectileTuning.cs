using System;

namespace Vow.Core.Logic
{
    // 友軍彈道、測試砲台與破牆護盾的數值單一事實來源（PHASE2_BATCH3_PLAN.md §4-10）。
    // 全部標【試玩必調】：GDD 未給定，2026-09-19 使用者裁定先用暫定值。
    // 刻意不重複 RuneTuning 已有的數字——敵方牆的壽命讀 RuneTuning.WallLifespanSeconds、
    // 生成距離讀 RuneTuning.QuickCastDistance，避免兩份數值各自漂移。
    [Serializable]
    public sealed class ProjectileTuning
    {
        // ── 測試砲台（除錯設施，預設關閉；理由見 §4-7）──
        public float TurretFireIntervalSeconds = 0.25f;   // 0.25s/發：射速定得更慢會讓第 6 發的 ×0.85 永遠看不到（§4-4⑤）

        // ── 子彈 ──
        public float BulletSpeed = 20f;                   // m/s
        public float BulletDamage = 20f;
        public float BulletMaxRange = 30f;
        public float BulletLifespanSeconds = 1.5f;
        public int BulletPoolSize = 4;
        public int SweepHitBufferSize = 8;                // 單幀掃掠命中緩衝
        public int PenetratedWallBufferSize = 8;          // 單發子彈的「已穿透牆」名冊

        // ── 破牆護盾（GDD §參-2：近戰砸碎敵方／中立石牆）──
        public float ShieldAmount = 150f;
        public float ShieldDurationSeconds = 2.5f;

        // ── 除錯用敵方石牆池（獨立於玩家名冊，§4-6）──
        public int EnemyWallPoolSize = 2;

        // ── 點擊射線 ──
        public int TapHitBufferSize = 16;                 // 溢位會讓最近的合法命中被丟掉，所以要夠大並在溢位時告警
    }
}
