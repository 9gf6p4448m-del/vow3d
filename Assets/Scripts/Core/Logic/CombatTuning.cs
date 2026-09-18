using System;

namespace Vow.Core.Logic
{
    // 手感數值的單一事實來源。本層刻意不引用 UnityEngine：同一份程式碼要能在 dotnet 測試與日後的伺服器權威模擬中重用。
    // 欄位用 public 是為了讓 Unity 的 ScriptableObject 能直接序列化（見 HeroTuningAsset）。
    [Serializable]
    public sealed class CombatTuning
    {
        // ── 普攻節奏（GDD §貳-3）──
        public float WindupSeconds = 0.25f;          // 前搖：由動畫事件 OnAttackHit() 結束，這個值只用來校正動畫播放速度
        public float CadenceWindowSeconds = 0.22f;   // 目押切後搖寬容窗口，合法範圍 0.22 ~ 0.25
        // 命中前預輸入緩衝。緩衝只在前搖期間收件，所以 WindupSeconds 必須 ≥ InputBufferSeconds；
        // 把前搖調得比緩衝還短時，超出前搖長度的那段緩衝會靜默失效。
        public float InputBufferSeconds = 0.12f;
        public float RecoverySeconds = 0.15f;        // 無微位移時的平滑收招

        // 紅線 2：攻擊週期恆定，走 A 不得縮短。GDD 未給定數值，0.8s 為灰盒暫定值（交由手感測試調整）。
        public float AttackPeriodSeconds = 0.8f;

        // 動畫事件遺失時的保險：超過此時間仍未收到 OnAttackHit() 就強制結算並回報，避免英雄永遠卡在前搖。
        public float WindupWatchdogSeconds = 0.6f;

        // 前搖進行不到這個比例就收到的命中事件視為上一刀殘留的動畫事件（前搖被打斷後混合期間可能誤觸發），直接丟棄。
        public float MinWindupFractionForHit = 0.5f;

        // ── 微位移（GDD §貳-2）──
        public int MaxCharges = 3;
        public float ChargeRecoverySeconds = 2.5f;
        public float ChainWindowSeconds = 1.0f;      // 距「上一次」滑步 1.0s 內再滑即視為連段（滾動窗口）
        public float[] DashDistances = { 1.4f, 0.9f, 0.5f };
        public float DashDurationSeconds = 0.12f;    // GDD 未給定；灰盒暫定值

        public float DashDistanceForChainIndex(int chainIndex)
        {
            if (DashDistances == null || DashDistances.Length == 0) return 0f;
            if (chainIndex < 0) chainIndex = 0;
            if (chainIndex >= DashDistances.Length) chainIndex = DashDistances.Length - 1;
            return DashDistances[chainIndex];
        }
    }
}
