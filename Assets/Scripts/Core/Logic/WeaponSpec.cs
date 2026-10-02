namespace Vow.Core.Logic
{
    // camera-lab 第三人稱武器灰盒（v0.16.0，GDD §貳.4 模式 C 武器灰盒）。數值全為暫定、試玩即改。不依賴 UnityEngine。
    public enum WeaponId
    {
        Standard = 0,
        Sword = 1,
        Bow = 2,
        Hammer = 3
    }

    // 一把武器怎麼挑目標／打多遠／是否橫掃。值型別、唯讀、零配置。
    // ConeHalfAngleDegrees ≤ 0＝沒有準星錐（不看角度，直接取 AimRangeMeters 內最近者）。
    // FallbackToNearest=false＝錐外一律不挑（含正在打的目標）。
    // OverridesAttackRange=false＝英雄攻擊射程沿用 HeroTuningAsset.AttackRange（AttackRangeMeters 只是紀錄預設值）。
    public readonly struct WeaponSpec
    {
        public readonly WeaponId Id;
        public readonly float ConeHalfAngleDegrees;
        public readonly float AimRangeMeters;
        public readonly float AttackRangeMeters;
        public readonly bool OverridesAttackRange;
        public readonly bool FallbackToNearest;
        public readonly bool IsSweep;
        public readonly float SweepFullAngleDegrees;   // 全角度（ElementGeometry.IsInsideSector 的參數語意），不是半角
        public readonly float SweepRangeMeters;
        public readonly float SweepCooldownSeconds;
        public readonly float SweepWindupSeconds;

        private WeaponSpec(WeaponId id, float coneHalfAngleDegrees, float aimRangeMeters, float attackRangeMeters,
            bool overridesAttackRange, bool fallbackToNearest, bool isSweep, float sweepFullAngleDegrees,
            float sweepRangeMeters, float sweepCooldownSeconds, float sweepWindupSeconds)
        {
            Id = id;
            ConeHalfAngleDegrees = coneHalfAngleDegrees;
            AimRangeMeters = aimRangeMeters;
            AttackRangeMeters = attackRangeMeters;
            OverridesAttackRange = overridesAttackRange;
            FallbackToNearest = fallbackToNearest;
            IsSweep = isSweep;
            SweepFullAngleDegrees = sweepFullAngleDegrees;
            SweepRangeMeters = sweepRangeMeters;
            SweepCooldownSeconds = sweepCooldownSeconds;
            SweepWindupSeconds = sweepWindupSeconds;
        }

        // 預設武器＝v0.15 現行 ATK 行為：逐值取 CameraLabAim 常數，攻擊射程不覆寫（HeroTuningAsset 預設 5m）。
        public static readonly WeaponSpec Standard = new WeaponSpec(WeaponId.Standard,
            CameraLabAim.ConeHalfAngleDegrees, CameraLabAim.MaxAimDistance, 5f, false, true, false, 0f, 0f, 0f, 0f);

        // 劍：沒有準星錐，自動打 5m 內最近者；射程同預設（不覆寫）。
        public static readonly WeaponSpec Sword = new WeaponSpec(WeaponId.Sword,
            0f, 5f, 5f, false, true, false, 0f, 0f, 0f, 0f);

        // 弓：窄錐 ±12°、12m；攻擊射程覆寫為 12m（站著射）；錐外不挑＝精準代價（暫定）。
        public static readonly WeaponSpec Bow = new WeaponSpec(WeaponId.Bow,
            12f, 12f, 12f, true, false, false, 0f, 0f, 0f, 0f);

        // 錘：朝準星橫掃全角 100°、半徑 3.5m，錐內沒人也出手；冷卻＝普攻週期 0.8s、前搖 0.25s（同 CombatTuning 預設）。
        // 每個命中目標吃一次英雄 AttackDamage。不挑目標、不覆寫射程。
        public static readonly WeaponSpec Hammer = new WeaponSpec(WeaponId.Hammer,
            0f, 0f, 0f, false, false, true, 100f, 3.5f, 0.8f, 0.25f);

        public static WeaponSpec Get(WeaponId id)
        {
            switch (id)
            {
                case WeaponId.Sword: return Sword;
                case WeaponId.Bow: return Bow;
                case WeaponId.Hammer: return Hammer;
                default: return Standard;
            }
        }
    }

    // 循環切換 Standard→Sword→Bow→Hammer→Standard；預設（default）＝Standard。
    public struct WeaponSelection
    {
        public const int Count = 4;

        private int _index;

        public WeaponId CurrentId => (WeaponId)_index;
        public WeaponSpec Current => WeaponSpec.Get((WeaponId)_index);

        public WeaponId Next()
        {
            _index = (_index + 1) % Count;
            return (WeaponId)_index;
        }

        public void Reset() { _index = 0; }
    }

    public static class WeaponSweep
    {
        // 橫掃範圍：頂點＝英雄、方向＝準星水平前方；含邊界。注意 IsInsideSector 吃的是全角度。
        public static bool Contains(in WeaponSpec weapon, float apexX, float apexZ, float dirX, float dirZ, float px, float pz)
        {
            return ElementGeometry.IsInsideSector(px, pz, apexX, apexZ, dirX, dirZ,
                weapon.SweepRangeMeters, weapon.SweepFullAngleDegrees);
        }
    }

    // 橫掃節奏：TryStart 在冷卻外才成功（成功即起算冷卻與前搖）；前搖到點時 TryConsumeResolve 回 true 一次。
    public struct WeaponSweepTimer
    {
        private float _readyAt;
        private float _resolveAt;

        public bool Pending { get; private set; }

        public bool TryStart(float now, float cooldownSeconds, float windupSeconds)
        {
            if (now < _readyAt) return false;
            _readyAt = now + cooldownSeconds;
            _resolveAt = now + windupSeconds;
            Pending = true;
            return true;
        }

        public bool TryConsumeResolve(float now)
        {
            if (!Pending || now < _resolveAt) return false;
            Pending = false;
            return true;
        }

        public void Reset()
        {
            _readyAt = 0f;
            _resolveAt = 0f;
            Pending = false;
        }
    }
}
