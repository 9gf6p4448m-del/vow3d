using Vow.Core.Logic;

namespace Vow.Input
{
    public enum LabActionButton
    {
        None = 0,
        Attack = 1,
        Dash = 2
    }

    // camera-lab 第三人稱按鈕事件的可選出口。TouchGestureRouter 只在 sink 同時實作本介面時才送出，
    // 既有 ITouchGestureSink 與其所有實作（含測試替身）一個字都不用改。
    public interface IActionButtonSink
    {
        void OnActionButtonPressed(LabActionButton button);
    }

    // ATK／DASH 兩顆圓鈕的螢幕幾何（Unity 螢幕座標：原點左下、像素）。不依賴 UnityEngine，畫面與路由共用這一份。
    // 直徑與離邊標準沿用 RuneButtonLayout：ATK 在符印鈕左側、DASH 在符印鈕正上方；DASH 放不下時退到 ATK 左側。
    public struct LabActionButtonLayout
    {
        public const float GapMillimeters = 3f;

        public ScreenRegion Attack;
        public ScreenRegion Dash;

        public static LabActionButtonLayout Compute(float screenWidth, float screenHeight, float pixelsPerMillimeter)
        {
            ScreenRegion rune = RuneButtonLayout.Compute(screenWidth, screenHeight, pixelsPerMillimeter).Button;
            float diameter = rune.XMax - rune.XMin;
            float gap = GapMillimeters * pixelsPerMillimeter;

            LabActionButtonLayout layout;
            layout.Attack = new ScreenRegion(rune.XMin - gap - diameter, rune.YMin, rune.XMin - gap, rune.YMax);
            layout.Dash = new ScreenRegion(rune.XMin, rune.YMax + gap, rune.XMax, rune.YMax + gap + diameter);
            if (layout.Dash.YMax > screenHeight - GestureMath.EdgeDeadzonePixels)
                layout.Dash = new ScreenRegion(layout.Attack.XMin - gap - diameter, rune.YMin, layout.Attack.XMin - gap, rune.YMax);
            return layout;
        }

        public LabActionButton Hit(float x, float y)
        {
            if (Attack.Contains(x, y)) return LabActionButton.Attack;
            if (Dash.Contains(x, y)) return LabActionButton.Dash;
            return LabActionButton.None;
        }
    }
}
