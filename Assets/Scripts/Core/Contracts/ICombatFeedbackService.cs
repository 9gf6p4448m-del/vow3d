using UnityEngine;

namespace Vow.Core
{
    // ARCHITECTURE.md §陸-2
    public interface ICombatFeedbackService
    {
        // 1. 創傷阻尼震屏 (Camera Shake)
        // trauma: 0.0 ~ 1.0，平方衰減，頻率與三軸旋轉分離，支援優先級覆蓋
        void RequestCameraShake(float trauma, float duration = 0.2f);

        // 2. 打擊頓挫幀 (Hitstop / Hit Pause)
        // 普攻/暴擊命中瞬間短暫凍結攻擊者與受擊者動畫幀 (30ms~60ms)，完美對齊 220ms 目押窗口
        void TriggerHitstop(float durationMs);

        // 3. 高對比度受擊閃白 (Screen Flash)
        // 內建防光敏癲癇 (Photosensitivity Safe) 與溫控自動降級
        void TriggerScreenFlash(Color flashColor, float durationMs = 50f);

        // 4. 地面殘留打擊貼花 (Impact Decals)
        // 從對象池取出，壽命結束後平滑溶解回收
        void SpawnGroundDecal(Vector3 worldPosition, DecalType type, float duration = 3.0f);
    }

    public enum DecalType
    {
        ScorchCrater,   // 烈焰焦痕
        FrostCrack,     // 冰霜裂紋
        VoidRupture     // 虛空地裂
    }

    // Hitstop 只凍結動畫播放速度，不動 Time.timeScale（輸入與 220ms 窗口計時必須照常走）。
    public interface IHitstopParticipant
    {
        void SetHitstopFrozen(bool frozen);
    }

    // 動畫事件 OnAttackHit() 的接收端；動畫層只依賴這個抽象。
    public interface IAttackHitReceiver
    {
        void NotifyAttackHit();
    }
}
