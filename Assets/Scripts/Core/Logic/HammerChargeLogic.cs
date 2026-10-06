namespace Vow.Core.Logic
{
    // camera-lab 錘蓄力重擊（凍結驗收 vow-toolchain/acceptance-hammer-20261006.md 含修訂一；數值在 WeaponSpec，暫定、試玩即改）。
    // 不依賴 UnityEngine、不讀 Time：按住秒數由呼叫端傳入。比照 BowChargeLogic：
    //   t < 0.2s＝快速橫掃，p＝0（＝現行錘：100°／3.5m／1 倍／冷卻 0.8s）；其餘 p＝clamp(t / 1.2s, 0, 1)。
    //   全角 100→130°、半徑 3.5→4.5m、傷害倍率 1.0→1.6、冷卻 0.8→1.2s 皆隨 p 線性；前搖固定 0.25s。
    public readonly struct HammerStrike
    {
        public readonly float Progress;
        public readonly float FullAngleDegrees;
        public readonly float RangeMeters;
        public readonly float DamageMultiplier;
        public readonly float CooldownSeconds;
        public readonly float WindupSeconds;

        public HammerStrike(float progress, float fullAngleDegrees, float rangeMeters, float damageMultiplier,
            float cooldownSeconds, float windupSeconds)
        {
            Progress = progress;
            FullAngleDegrees = fullAngleDegrees;
            RangeMeters = rangeMeters;
            DamageMultiplier = damageMultiplier;
            CooldownSeconds = cooldownSeconds;
            WindupSeconds = windupSeconds;
        }

        public bool IsQuick => Progress <= 0f;
    }

    public static class HammerChargeLogic
    {
        // 按住已越過快速門檻＝進入蓄力。
        public static bool IsCharging(double heldSeconds) => heldSeconds >= WeaponSpec.HammerQuickSweepSeconds;

        public static float Progress(double heldSeconds)
        {
            if (!IsCharging(heldSeconds)) return 0f;   // 含負值與 NaN：一律快速橫掃
            double p = heldSeconds / WeaponSpec.HammerFullChargeSeconds;
            return p >= 1.0 ? 1f : (float)p;
        }

        public static HammerStrike Resolve(double heldSeconds)
        {
            float p = Progress(heldSeconds);
            WeaponSpec h = WeaponSpec.Hammer;
            return new HammerStrike(p,
                h.SweepFullAngleDegrees + (WeaponSpec.HammerChargedSweepFullAngleDegrees - h.SweepFullAngleDegrees) * p,
                h.SweepRangeMeters + (WeaponSpec.HammerChargedSweepRangeMeters - h.SweepRangeMeters) * p,
                1f + (WeaponSpec.HammerChargedDamageMultiplier - 1f) * p,
                h.SweepCooldownSeconds + (WeaponSpec.HammerChargedCooldownSeconds - h.SweepCooldownSeconds) * p,
                h.SweepWindupSeconds);
        }
    }
}
