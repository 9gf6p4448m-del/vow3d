using Vow.Core.Logic;

namespace Vow.Input
{
    // 右下角符印按鈕與其取消區的螢幕幾何（Unity 螢幕座標：原點左下、單位像素）。不依賴 UnityEngine，可在 dotnet 下測試。
    // 畫面（RuneButtonView）與觸控分流（InputRoutingManager）都從這裡取值——兩邊各算一次遲早會對不上。
    //
    // 取消區的位置是被拖曳手勢逼出來的：拖曳原點是「手指按下的位置」，往任何方向拉滿（飽和行程）都必須還放得出牆，
    // 而且真人拉滿時幾乎都會拉過頭。所以取消區的下緣離按鈕上緣＝飽和行程＋同等長度的超拉餘裕；
    // 緊貼按鈕的話，往螢幕上方（＝英雄前方）拉到一半就會被判成取消。
    // 取消區一路延伸到螢幕上緣：想「用力往上甩掉這次施法」的手指停得再高也算取消，不會穿過一條帶子之後又變回成牆。
    public struct RuneButtonLayout
    {
        public const float ButtonDiameterMillimeters = 16f;
        public const float EdgeMarginMillimeters = 4f;            // 離螢幕邊：避開系統手勢列，也讓拇指不必摳著邊框
        public const float CancelClearanceMillimeters = 14f;      // 拉滿之後還容許拉過頭多少才算進取消區
        public const float MinCancelZoneHeightMillimeters = 8f;   // 螢幕再矮，取消區也要大到拇指放得進去
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

            // 矮螢幕：寧可犧牲超拉餘裕，也不能讓取消區被擠到消失（滑回原點仍是另一條取消路徑）。
            float highestAllowedBottom = screenHeight - MinCancelZoneHeightMillimeters * pixelsPerMillimeter;
            if (cancelBottom > highestAllowedBottom) cancelBottom = highestAllowedBottom;
            if (cancelBottom < layout.Button.YMax) cancelBottom = layout.Button.YMax;

            layout.CancelZone = new ScreenRegion(layout.Button.XMin, cancelBottom, layout.Button.XMax, screenHeight);
            return layout;
        }
    }
}
