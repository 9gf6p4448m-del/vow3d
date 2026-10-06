namespace Vow.Core.Logic
{
    // camera-lab 弓蓄力（凍結驗收 vow-toolchain/acceptance-bowcharge-20261003.md；數值暫定，試玩後另開批次調）。
    // 不依賴 UnityEngine、不讀 Time：按住秒數由呼叫端傳入。
    //   p＝clamp(t / 1.0s, 0, 1)；t < 0.2s＝快速射擊，p 固定 0（＝現行弓：錐 12°、12m、倍率 1.0、不穿透）。
    //   錐半角 12 − 10p（bowline-20261003：使用者「蓄滿一條線」，滿蓄 4°→2°）、射程 12 + 4p、傷害倍率 1 + 0.8p；p == 1 才穿透。
    //   預覽：p ≥ 0.9 畫成一條細線（WeaponPreviewKind.Line），其餘仍是錐。
    public readonly struct BowShot
    {
        public readonly float Progress;
        public readonly float ConeHalfAngleDegrees;
        public readonly float RangeMeters;
        public readonly float DamageMultiplier;
        public readonly bool Pierce;

        public BowShot(float progress, float coneHalfAngleDegrees, float rangeMeters, float damageMultiplier, bool pierce)
        {
            Progress = progress;
            ConeHalfAngleDegrees = coneHalfAngleDegrees;
            RangeMeters = rangeMeters;
            DamageMultiplier = damageMultiplier;
            Pierce = pierce;
        }

        public bool IsQuick => Progress <= 0f;
    }

    public static class BowChargeLogic
    {
        public const double QuickShotSeconds = 0.2;
        public const double FullChargeSeconds = 1.0;
        public const float ConeNarrowingDegrees = 10f;   // 12° → 2°（acceptance-bowline-20261003.md）
        public const float LinePreviewProgress = 0.9f;   // p ≥ 0.9：預覽改畫細線
        public const float RangeExtensionMeters = 4f;    // 12m → 16m
        public const float DamageBonus = 0.8f;           // 1.0 → 1.8

        // 按住已越過快速射擊門檻＝進入蓄力（追加 A12：弓「蓄力時停火」也從這一刻起算，與快速射擊同一個門檻）。
        public static bool IsCharging(double heldSeconds) => heldSeconds >= QuickShotSeconds;

        public static float Progress(double heldSeconds)
        {
            if (!IsCharging(heldSeconds)) return 0f;   // 含負值與 NaN：一律快速射擊
            double p = heldSeconds / FullChargeSeconds;
            return p >= 1.0 ? 1f : (float)p;
        }

        public static BowShot Resolve(double heldSeconds)
        {
            float p = Progress(heldSeconds);
            WeaponSpec bow = WeaponSpec.Bow;
            return new BowShot(p,
                bow.ConeHalfAngleDegrees - ConeNarrowingDegrees * p,
                bow.AimRangeMeters + RangeExtensionMeters * p,
                1f + DamageBonus * p,
                p >= 1f);
        }
    }

    public enum WeaponPreviewKind
    {
        None = 0,
        Arc = 1,      // 劍：以英雄為圓心的整圈（不看準星）
        Cone = 2,     // 弓／鉤鎖：準星窄錐
        Sector = 3,   // 錘：朝準星的橫掃扇形
        Line = 4      // 弓蓄到 p ≥ 0.9：沿準星的一條細線（HalfAngleDegrees 仍是實際判定的錐半角）
    }

    // 按住 ATK 時的範圍預覽（形狀＋參數）。Standard＝None。
    // HalfAngleDegrees：Cone＝錐半角；Sector＝全角的一半；Arc＝180（整圈）。
    public readonly struct WeaponAimPreview
    {
        public readonly WeaponPreviewKind Kind;
        public readonly float HalfAngleDegrees;
        public readonly float RangeMeters;

        public WeaponAimPreview(WeaponPreviewKind kind, float halfAngleDegrees, float rangeMeters)
        {
            Kind = kind;
            HalfAngleDegrees = halfAngleDegrees;
            RangeMeters = rangeMeters;
        }

        public float FullAngleDegrees => HalfAngleDegrees * 2f;

        public static WeaponAimPreview For(WeaponId weapon, double heldSeconds)
        {
            switch (weapon)
            {
                case WeaponId.Sword:
                    return new WeaponAimPreview(WeaponPreviewKind.Arc, 180f, WeaponSpec.Sword.AimRangeMeters);
                case WeaponId.Bow:
                    BowShot shot = BowChargeLogic.Resolve(heldSeconds);
                    return new WeaponAimPreview(shot.Progress >= BowChargeLogic.LinePreviewProgress ? WeaponPreviewKind.Line : WeaponPreviewKind.Cone,
                        shot.ConeHalfAngleDegrees, shot.RangeMeters);
                case WeaponId.Hammer:   // 錘蓄力重擊（2026-10-06）：扇形隨蓄力放大到蓄滿 130°／4.5m
                    HammerStrike strike = HammerChargeLogic.Resolve(heldSeconds);
                    return new WeaponAimPreview(WeaponPreviewKind.Sector, strike.FullAngleDegrees * 0.5f, strike.RangeMeters);
                case WeaponId.Grapple:
                    return new WeaponAimPreview(WeaponPreviewKind.Cone, WeaponSpec.Grapple.ConeHalfAngleDegrees,
                        WeaponSpec.Grapple.AimRangeMeters);
                default:
                    return default;
            }
        }
    }
}
