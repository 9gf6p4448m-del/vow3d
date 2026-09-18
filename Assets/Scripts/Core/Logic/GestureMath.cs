using System;

namespace Vow.Core.Logic
{
    // 觸控手勢的純數學：物理毫米換算、八向吸附、邊緣死區。
    public static class GestureMath
    {
        public const float MillimetersPerInch = 25.4f;
        public const float FlickMinMillimeters = 3.5f;
        public const float FlickMaxMillimeters = 7.5f;
        public const float EdgeDeadzonePixels = 8f;

        // Screen.dpi 在部分 Android 機與桌機上會回 0；此時退回 fallback，不可讓手勢半徑變成 0。
        public static float MillimetersToPixels(float millimeters, float dpi, float fallbackDpi)
        {
            float effective = dpi > 0f ? dpi : fallbackDpi;
            return millimeters * effective / MillimetersPerInch;
        }

        public static float PixelsToMillimeters(float pixels, float dpi, float fallbackDpi)
        {
            float effective = dpi > 0f ? dpi : fallbackDpi;
            return effective <= 0f ? 0f : pixels * MillimetersPerInch / effective;
        }

        // 智慧吸附：輸出為 8 個主要戰術方向之一的單位向量。回傳 false 表示輸入為零向量。
        public static bool SnapToEightWay(float x, float y, out float snappedX, out float snappedY)
        {
            if ((double)x * x + (double)y * y < 1e-12)
            {
                snappedX = 0f; snappedY = 0f;
                return false;
            }

            const double step = Math.PI / 4.0;
            double angle = Math.Atan2(y, x);
            double snapped = Math.Round(angle / step) * step;
            snappedX = (float)Math.Cos(snapped);
            snappedY = (float)Math.Sin(snapped);
            return true;
        }

        // 左、右、底三邊的軟體過濾死區（GDD §貳-4）。座標原點在左下，與 Unity 螢幕座標一致。
        public static bool IsInEdgeDeadzone(float x, float y, float screenWidth, float screenHeight, float marginPixels)
        {
            return x < marginPixels || x > screenWidth - marginPixels || y < marginPixels;
        }
    }

    public enum GestureOutcome
    {
        None,
        Flick,   // 位移越過最小半徑的瞬間即判定，不等手指離開（把辨識延遲壓到最低）
        Tap      // 手指離開時位移仍未達最小半徑
    }

    // 單指手勢追蹤。值型別、無配置；輸入服務以固定長度陣列持有多指。
    //
    // 3.5mm ~ 7.5mm 的解讀：3.5mm 是觸發半徑；7.5mm 是飽和半徑（模式 B 微輪盤的外圈、模式 A 的有效行程上限）——
    // 越過觸發半徑的瞬間方向即定案，手指劃再遠也只算一次微彈。慢速拖曳（超過 maxFlickSeconds 才越過觸發半徑）不算微彈，也不算點擊。
    public struct FlickGestureTracker
    {
        public bool Active;
        public int TouchId;
        public float StartX;
        public float StartY;
        public double StartTime;
        public bool Consumed;      // 已產生過結果（Flick）或已判定為慢速拖曳
        public float FlickDirX;
        public float FlickDirY;

        public void Begin(int touchId, float x, float y, double time)
        {
            Active = true;
            TouchId = touchId;
            StartX = x;
            StartY = y;
            StartTime = time;
            Consumed = false;
            FlickDirX = 0f;
            FlickDirY = 0f;
        }

        public GestureOutcome Move(float x, float y, double time, float minRadiusPx, float maxFlickSeconds)
        {
            if (!Active || Consumed) return GestureOutcome.None;

            float dx = x - StartX;
            float dy = y - StartY;
            double distSq = (double)dx * dx + (double)dy * dy;
            if (distSq < (double)minRadiusPx * minRadiusPx) return GestureOutcome.None;

            Consumed = true;
            if (time - StartTime > maxFlickSeconds) return GestureOutcome.None;

            // 飽和：方向取自觸發當下的位移向量，之後手指再劃多遠都不影響結果。
            if (!GestureMath.SnapToEightWay(dx, dy, out FlickDirX, out FlickDirY)) return GestureOutcome.None;
            return GestureOutcome.Flick;
        }

        public GestureOutcome End(float x, float y, double time, float minRadiusPx, float maxFlickSeconds)
        {
            if (!Active) return GestureOutcome.None;

            // 最後一個取樣點可能才剛越過半徑（快速微彈常常只有 Began→Ended 兩個取樣）
            GestureOutcome outcome = Move(x, y, time, minRadiusPx, maxFlickSeconds);
            bool wasConsumed = Consumed;
            Active = false;

            if (outcome == GestureOutcome.Flick) return GestureOutcome.Flick;
            return wasConsumed ? GestureOutcome.None : GestureOutcome.Tap;
        }

        public void Cancel()
        {
            Active = false;
        }
    }

    // 模式 B 微輪盤：浮動原點，只輸出「方向」。它沒有任何通往移動指令的出口（高壓紅線：微輪盤不可用於日常走動）。
    public struct MicroVectorPip
    {
        public bool Held;
        public int TouchId;
        public float OriginX;
        public float OriginY;
        public bool HasVector;
        public float DirX;
        public float DirY;

        public void Begin(int touchId, float x, float y)
        {
            Held = true;
            TouchId = touchId;
            OriginX = x;
            OriginY = y;
            HasVector = false;
            DirX = 0f;
            DirY = 0f;
        }

        public void Move(float x, float y, float minRadiusPx)
        {
            if (!Held) return;
            float dx = x - OriginX;
            float dy = y - OriginY;
            if ((double)dx * dx + (double)dy * dy < (double)minRadiusPx * minRadiusPx)
            {
                HasVector = false;
                return;
            }
            HasVector = GestureMath.SnapToEightWay(dx, dy, out DirX, out DirY);
        }

        public void End()
        {
            Held = false;
            HasVector = false;
        }
    }
}
