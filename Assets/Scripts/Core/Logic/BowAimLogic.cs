using System;

namespace Vow.Core.Logic
{
    // camera-lab 弓「按住拖曳瞄準」（手勢操作第一批；凍結驗收 vow-toolchain/acceptance-bowaim-20261003.md；數值暫定，試玩後另開批次調）。
    // 不依賴 UnityEngine、不讀 Time。位移單位＝名目 mm（與塑牆拖曳同一把尺：GestureMath.MillimetersToPixels(Screen.dpi)）。
    //   |dx| ≤ 2mm＝死區（offset 0）；超過死區 offset＝clamp((dx − sign(dx)·2) / 13, −1, 1) × 40°（15mm 拉滿 ±40°）。
    //   鏡頭追蹤：每次最多轉 90°/s × dt，走最短方向、不過衝。
    public static class BowAimLogic
    {
        public const float DeadZoneMillimeters = 2f;
        public const float FullOffsetMillimeters = 15f;          // 拉滿時的手指水平位移
        public const float MaxOffsetDegrees = 40f;
        public const float CameraTrackDegreesPerSecond = 90f;

        // 手指相對按下點的水平位移（名目 mm，向右為正）→ 出手方向相對按下時準星的偏角（度，向右＝yaw 增加為正）。
        public static float OffsetDegrees(float dxMillimeters)
        {
            if (float.IsNaN(dxMillimeters)) return 0f;
            float magnitude = Math.Abs(dxMillimeters);
            if (magnitude <= DeadZoneMillimeters) return 0f;
            float t = (magnitude - DeadZoneMillimeters) / (FullOffsetMillimeters - DeadZoneMillimeters);
            if (t > 1f) t = 1f;
            return Math.Sign(dxMillimeters) * t * MaxOffsetDegrees;
        }

        // 兩個 yaw 的最短有號差（target − current），範圍 (−180, 180]。
        public static float DeltaDegrees(float currentDegrees, float targetDegrees)
        {
            double d = (targetDegrees - currentDegrees) % 360.0;
            if (d > 180.0) d -= 360.0;
            else if (d <= -180.0) d += 360.0;
            return (float)d;
        }

        // 0 ≤ 結果 < 360。
        public static float NormalizeDegrees(float degrees)
        {
            double d = degrees % 360.0;
            if (d < 0.0) d += 360.0;
            return d >= 360.0 ? 0f : (float)d;
        }

        // 鏡頭 yaw 朝 target 以最大 maxDegreesPerSecond 追一步（dt 秒）。走最短方向、到了就停（不過衝）；dt ≤ 0 或 NaN 不動。
        public static float StepYawToward(float currentDegrees, float targetDegrees, float maxDegreesPerSecond, float deltaSeconds)
        {
            if (!(deltaSeconds > 0f) || !(maxDegreesPerSecond > 0f)) return NormalizeDegrees(currentDegrees);
            float delta = DeltaDegrees(currentDegrees, targetDegrees);
            float maxStep = maxDegreesPerSecond * deltaSeconds;
            if (delta > maxStep) delta = maxStep;
            else if (delta < -maxStep) delta = -maxStep;
            return NormalizeDegrees(currentDegrees + delta);
        }
    }
}
