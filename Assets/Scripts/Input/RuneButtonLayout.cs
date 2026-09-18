using Vow.Core.Logic;

namespace Vow.Input
{
    // 右下角符印按鈕與其取消區的螢幕幾何（Unity 螢幕座標：原點左下、單位像素）。不依賴 UnityEngine，可在 dotnet 下測試。
    // 畫面（RuneButtonView）與觸控分流（InputRoutingManager）都從這裡取值——兩邊各算一次遲早會對不上。
    //
    // 取消區的位置是被拖曳手勢逼出來的：拖曳原點是「手指按下的位置」，往任何方向拉滿（飽和行程）都必須還放得出牆。
    // 所以取消區的下緣要離按鈕上緣至少一個飽和行程再多一點；緊貼按鈕的話，往螢幕上方（＝英雄前方）拉到一半就會被判成取消。
    public struct RuneButtonLayout
    {
        public const float ButtonDiameterMillimeters = 16f;
        public const float EdgeMarginMillimeters = 4f;         // 離螢幕邊：避開系統手勢列，也讓拇指不必摳著邊框
        public const float CancelClearanceMillimeters = 4f;    // 取消區下緣與「拉滿仍安全」範圍之間的間隔
        public const float CancelZoneHeightMillimeters = 12f;
        private const float MinEdgeMarginPixels = GestureMath.EdgeDeadzonePixels + 8f;

        public ScreenRegion Button;
        public ScreenRegion CancelZone;

        public static RuneButtonLayout Compute(float screenWidth, float screenHeight, float pixelsPerMillimeter, float saturationPixels)
        {
            float margin = EdgeMarginMillimeters * pixelsPerMillimeter;
            if (margin < MinEdgeMarginPixels) margin = MinEdgeMarginPixels;
            float diameter = ButtonDiameterMillimeters * pixelsPerMillimeter;

            RuneButtonLayout layout;
            layout.Button = new ScreenRegion(screenWidth - margin - diameter, margin, screenWidth - margin, margin + diameter);

            float cancelBottom = layout.Button.YMax + saturationPixels + CancelClearanceMillimeters * pixelsPerMillimeter;
            float cancelTop = cancelBottom + CancelZoneHeightMillimeters * pixelsPerMillimeter;
            if (cancelTop > screenHeight) cancelTop = screenHeight;
            layout.CancelZone = new ScreenRegion(layout.Button.XMin, cancelBottom, layout.Button.XMax, cancelTop);
            return layout;
        }
    }
}
