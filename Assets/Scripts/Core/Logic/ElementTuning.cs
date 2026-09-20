using System;

namespace Vow.Core.Logic
{
    // 元素反應（泥濘流沙／蒸氣迷霧／擴散火浪）全部數值的單一來源（PHASE2_BATCH4_PLAN.md §2）。
    // 這 20 個裁定數值不得漂移（§5 V6-d 用 grep 逐一核對）；MaxLiveZones／TargetRosterCapacity 為主對話自決。
    [Serializable]
    public sealed class ElementTuning
    {
        // 水
        public float WaterRadius = 3f;              public float WaterDurationSeconds = 6f;
        // 火
        public float FireDirectDamage = 40f;
        public float BurnRadius = 3f;               public float BurnDurationSeconds = 4f;
        public float BurnDamagePerSecond = 20f;
        // 反應區（GDD 明文）
        public float ReactionRadius = 4f;
        public float QuicksandDurationSeconds = 3.5f;   public float SteamDurationSeconds = 3f;
        // 流沙（GDD 圍欄九）
        public float RootDurationSeconds = 1.2f;    public float QuicksandSlowMultiplier = 0.65f;
        // 蒸氣（GDD 圍欄九）
        public float RevealDurationSeconds = 1.5f;  public float SteamChannelSpeedMultiplier = 0.5f;
        // 火浪
        public float FirestormAngleDegrees = 60f;   public float FirestormRangeMeters = 6f;
        public float FirestormDamageMultiplier = 1.5f;  // 40 × 1.5 ＝ 60
        // 爆沸
        public float BoilDamage = 80f;
        // 施放
        public float SkillCooldownSeconds = 5f;     public float CastDistanceMeters = 4f;
        // Combo 反饋（GDD 明文 45ms，非暫定）
        public float ComboHitstopMs = 45f;          public float ComboTrauma = 0.5f;
        // 容量
        public int MaxLiveZones = 6;                public int TargetRosterCapacity = 32;
    }
}
