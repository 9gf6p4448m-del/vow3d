using Vow.Core.Logic;

namespace Vow.Input
{
    // 右下角符印按鈕的螢幕幾何（Unity 螢幕座標：原點左下、單位像素）。不依賴 UnityEngine，可在 dotnet 下測試。
    // 畫面（RuneButtonView）與觸控分流（InputRoutingManager）都從這裡取值——兩邊各算一次遲早會對不上。
    //
    // 沒有取消區：使用者 2026-09-19 試玩後裁定，取消只留「滑回按下的位置放手」（虛影會變色提示），畫面上不出現任何取消 UI。
    public struct RuneButtonLayout
    {
        public const float ButtonDiameterMillimeters = 16f;
        // 離螢幕邊＝拇指拉滿的行程（RuneTuning.DragSaturationMillimeters＝14mm）＋1mm 餘裕：拖曳原點是手指按下的位置，
        // 邊距比行程短的話，往右／往下拖的手指還沒離開取消半徑就撞到邊框，放手變成取消（2026-09-19 試玩回饋，原為 4mm）。
        public const float EdgeMarginMillimeters = 15f;
        private const float MinEdgeMarginPixels = GestureMath.EdgeDeadzonePixels + 8f;

        public ScreenRegion Button;

        public static RuneButtonLayout Compute(float screenWidth, float screenHeight, float pixelsPerMillimeter)
        {
            float margin = EdgeMarginMillimeters * pixelsPerMillimeter;
            if (margin < MinEdgeMarginPixels) margin = MinEdgeMarginPixels;
            float diameter = ButtonDiameterMillimeters * pixelsPerMillimeter;

            RuneButtonLayout layout;
            layout.Button = new ScreenRegion(screenWidth - margin - diameter, margin, screenWidth - margin, margin + diameter);
            return layout;
        }
    }
}
