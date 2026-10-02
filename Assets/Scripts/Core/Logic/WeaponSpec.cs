using System;

namespace Vow.Core.Logic
{
    // camera-lab 第三人稱武器灰盒（v0.16.0，GDD §貳.4 模式 C 武器灰盒）。數值全為暫定、試玩即改。不依賴 UnityEngine。
    public enum WeaponId
    {
        Standard = 0,
        Sword = 1,
        Bow = 2,
        Hammer = 3,
        Grapple = 4
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
        // 鉤鎖（v0.17.0）：拉自己到目標前 GrappleStopMeters 處，位移歷時 GrapplePullSeconds；冷卻 GrappleCooldownSeconds。鉤本身不傷害。
        public readonly bool IsGrapple;
        public readonly float GrappleStopMeters;
        public readonly float GrapplePullSeconds;
        public readonly float GrappleCooldownSeconds;

        private WeaponSpec(WeaponId id, float coneHalfAngleDegrees, float aimRangeMeters, float attackRangeMeters,
            bool overridesAttackRange, bool fallbackToNearest, bool isSweep, float sweepFullAngleDegrees,
            float sweepRangeMeters, float sweepCooldownSeconds, float sweepWindupSeconds,
            bool isGrapple = false, float grappleStopMeters = 0f, float grapplePullSeconds = 0f, float grappleCooldownSeconds = 0f)
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
            IsGrapple = isGrapple;
            GrappleStopMeters = grappleStopMeters;
            GrapplePullSeconds = grapplePullSeconds;
            GrappleCooldownSeconds = grappleCooldownSeconds;
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

        // 鉤鎖（使用者 2026-10-02 簽准，暫定）：準星 ±20°、10m 窄錐（錐外不挑），且須視線無石牆、同一樓地板；
        // 按 ATK 把自己拉到目標前 2m（約 0.2s，逐幀走 HeroLocomotion.ApplyDisplacement 對牆裁切），抵達後接既有普攻。冷卻 4s。
        public static readonly WeaponSpec Grapple = new WeaponSpec(WeaponId.Grapple,
            20f, 10f, 5f, false, false, false, 0f, 0f, 0f, 0f, true, 2f, 0.2f, 4f);

        public static WeaponSpec Get(WeaponId id)
        {
            switch (id)
            {
                case WeaponId.Sword: return Sword;
                case WeaponId.Bow: return Bow;
                case WeaponId.Hammer: return Hammer;
                case WeaponId.Grapple: return Grapple;
                default: return Standard;
            }
        }
    }

    // 循環切換 Standard→Sword→Bow→Hammer→Grapple→Standard；預設（default）＝Standard。
    public struct WeaponSelection
    {
        public const int Count = 5;

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

        // 取消還沒結算的那一掃；冷卻照算（覆審 r1 L1：切視角不得繞過冷卻）。
        public void CancelPending() { Pending = false; }

        public void Reset()
        {
            _readyAt = 0f;
            _resolveAt = 0f;
            Pending = false;
        }
    }

    // 鉤鎖節奏與位移量：TryStart 在冷卻外、且離目標超過停點距離才成功（成功即起算冷卻）；
    // 之後每幀 Step 給出這一幀該走的水平位移（總量＝英雄→目標前 StopMeters 處，於 PullSeconds 內等速走完）。
    public struct GrappleHook
    {
        private float _readyAt;
        private float _totalX, _totalZ, _elapsed, _duration;

        public bool Pulling { get; private set; }

        // 英雄→「目標前 stopMeters 處」的水平位移；已在 stopMeters 內回 false（不需要拉）。
        public static bool ComputePull(float heroX, float heroZ, float targetX, float targetZ, float stopMeters,
            out float dx, out float dz)
        {
            double ox = targetX - heroX, oz = targetZ - heroZ;
            double distance = Math.Sqrt(ox * ox + oz * oz);
            dx = 0f;
            dz = 0f;
            if (distance <= stopMeters) return false;
            double k = (distance - stopMeters) / distance;
            dx = (float)(ox * k);
            dz = (float)(oz * k);
            return true;
        }

        public bool IsReady(float now) => now >= _readyAt;

        public bool TryStart(float now, in WeaponSpec weapon, float heroX, float heroZ, float targetX, float targetZ)
        {
            if (now < _readyAt || weapon.GrapplePullSeconds <= 0f) return false;
            if (!ComputePull(heroX, heroZ, targetX, targetZ, weapon.GrappleStopMeters, out _totalX, out _totalZ)) return false;
            _readyAt = now + weapon.GrappleCooldownSeconds;
            _elapsed = 0f;
            _duration = weapon.GrapplePullSeconds;
            Pulling = true;
            return true;
        }

        // 這一幀的位移；最後一幀回 true（拉完）。不在拉時位移為 0、回 false。
        public bool Step(float dt, out float dx, out float dz)
        {
            dx = 0f;
            dz = 0f;
            if (!Pulling) return false;
            float before = _elapsed / _duration;
            _elapsed = Math.Min(_elapsed + dt, _duration);
            float after = _elapsed / _duration;
            dx = _totalX * (after - before);
            dz = _totalZ * (after - before);
            if (_elapsed < _duration) return false;
            Pulling = false;
            return true;
        }

        // 中途取消（被牆擋住、縛足、輸入被鎖、切回俯視）；冷卻照算。
        public void Cancel() { Pulling = false; }
    }
}
