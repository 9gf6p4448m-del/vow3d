namespace Vow.Input
{
    public enum LabActionButton
    {
        None = 0,
        Attack = 1,
        Dash = 2,
        Weapon = 3   // v0.16.0 武器灰盒：循環切換武器
    }

    // camera-lab 第三人稱按鈕事件的可選出口。TouchGestureRouter 只在 sink 同時實作本介面時才送出，
    // 既有 ITouchGestureSink 與其所有實作（含測試替身）一個字都不用改。
    public interface IActionButtonSink
    {
        void OnActionButtonPressed(LabActionButton button);
    }

    // ATK／DASH 兩顆圓鈕的螢幕幾何（Unity 螢幕座標：原點左下、像素）。不依賴 UnityEngine，畫面與路由共用這一份。
    // 直徑與離邊標準沿用 RuneButtonLayout：ATK 在符印鈕左側、DASH 在符印鈕正上方。
    // v0.16.0 武器切換鈕（WPN）在 ATK 正上方、DASH 左側（與兩鈕各隔一個間距），不另佔更高的位置。
    // 需要畫面高度約 ≥ 符印鈕邊距＋2×直徑＋間距（15+16+3+16mm）＋8px；更矮的畫面不支援（原退位分支會壓到搖桿區，已移除）。
    public struct LabActionButtonLayout
    {
        public const float GapMillimeters = 3f;

        public ScreenRegion Attack;
        public ScreenRegion Dash;
        public ScreenRegion Weapon;
        // 覆審 r1 M2（主對話裁定 A）：三選一天賦盤顯示時 WPN 讓開——不畫、不收路由；關閉即恢復原位。
        public bool WeaponVisible;

        public static LabActionButtonLayout Compute(float screenWidth, float screenHeight, float pixelsPerMillimeter)
        {
            return Compute(screenWidth, screenHeight, pixelsPerMillimeter, false);
        }

        public static LabActionButtonLayout Compute(float screenWidth, float screenHeight, float pixelsPerMillimeter,
            bool talentPanelVisible)
        {
            ScreenRegion rune = RuneButtonLayout.Compute(screenWidth, screenHeight, pixelsPerMillimeter).Button;
            float diameter = rune.XMax - rune.XMin;
            float gap = GapMillimeters * pixelsPerMillimeter;

            LabActionButtonLayout layout;
            layout.Attack = new ScreenRegion(rune.XMin - gap - diameter, rune.YMin, rune.XMin - gap, rune.YMax);
            layout.Dash = new ScreenRegion(rune.XMin, rune.YMax + gap, rune.XMax, rune.YMax + gap + diameter);
            layout.Weapon = new ScreenRegion(layout.Attack.XMin, layout.Dash.YMin, layout.Attack.XMax, layout.Dash.YMax);
            layout.WeaponVisible = !talentPanelVisible;
            return layout;
        }

        public LabActionButton Hit(float x, float y)
        {
            if (Attack.Contains(x, y)) return LabActionButton.Attack;
            if (Dash.Contains(x, y)) return LabActionButton.Dash;
            if (WeaponVisible && Weapon.Contains(x, y)) return LabActionButton.Weapon;
            return LabActionButton.None;
        }
    }
}
