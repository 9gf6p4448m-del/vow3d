using UnityEngine;
using Vow.Core.Logic;

namespace Vow.Core
{
    // 灰盒手感調校面板。所有數值集中在這一顆資產，測試者調參不必碰程式碼。
    [CreateAssetMenu(menuName = "VOW/Hero Tuning", fileName = "HeroTuning")]
    public sealed class HeroTuningAsset : ScriptableObject
    {
        public CombatTuning Combat = new CombatTuning();

        [Header("移動")]
        public float MoveSpeed = 5.5f;
        public float TurnSpeedDegreesPerSecond = 1080f;
        public float BodyRadius = 0.35f;

        [Header("普攻（灰盒暫定：命中幀即時結算，無彈道）")]
        public float AttackRange = 5f;
        public float AttackDamage = 60f;

        [Header("打擊反饋")]
        [Range(30f, 60f)] public float HitstopMilliseconds = 40f;
        [Range(0f, 1f)] public float BasicAttackTrauma = 0.2f;
        [Range(0f, 1f)] public float WallBreakTrauma = 0.8f;
    }
}
