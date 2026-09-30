namespace Vow.Core.Logic
{
    // v0.14.0 深淵峽谷灰盒數值（docs/V0140_CANYON_PLAN.md §7.1，2026-09-30 凍結）。零 UnityEngine。
    public sealed class CanyonTuning
    {
        // §4.1 崖台射程加成（整數百分比；range × (100 + 10) / 100f）。
        public int CliffRangePercent = 10;
        // §4.2 站在崖台上的觀看者局部視野；其他位置維持 CaptureVisibilityLogic.LocalVisionRadius（6）。
        public float CliffVisionRadius = 8f;
        // §4.4 谷底淺水：水域圓心在谷底時半徑加這麼多（與潮汐牽引相加）。
        public float CanyonWaterBonusMeters = 1f;

        // §4.5 地熱點。
        public float VentChannelSeconds = 0.6f;
        public float VentCooldownSeconds = 8f;
        public float VentFlightSeconds = 0.5f;
        public float VentPadRadius = 0.375f;

        // §2.2 斜坡矩形（長軸沿兩塔心連線）。
        public float RampWidth = 3.5f;
        public float RampLength = 4f;

        // §5.2 崖壁碰撞體厚度（Unity 端用；純邏輯的崖壁是線段）。
        public float CliffBarrierThickness = 0.1f;

        // §4.7 開火顯形。
        public float RevealSeconds = 1.5f;

        // §5.2 峽谷規格下 Lobby 的對手位置（r3，使用者裁定「甲」）。
        public float CanyonLobbyOpponentSpawnX = 4f;
        public float CanyonLobbyOpponentSpawnZ = -12.5f;
    }
}
