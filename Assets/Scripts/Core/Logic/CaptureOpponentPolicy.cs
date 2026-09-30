namespace Vow.Core.Logic
{
    public struct CaptureOpponentDecision
    {
        public bool ChaseHero;
        public int TargetTile; // -1 = 原地待命

        public CaptureOpponentDecision(bool chaseHero, int targetTile)
        {
            ChaseHero = chaseHero;
            TargetTile = targetTile;
        }
    }

    // 灰盒對手的佔領模式決策（V080_CAPTURE_PLAN.md E9／E10／E24、V-A21／V-A22，2026-09-24 凍結）。零 UnityEngine。
    public static class CaptureOpponentPolicy
    {
        // ownership 用 CaptureMatchLogic 的陣營代碼（BlueFactionId/RedFactionId/NeutralFactionId），7 個元素（v0.8.0 七塊夾具）。
        public static CaptureOpponentDecision Decide(
            float opponentX, float opponentZ,
            float heroX, float heroZ, bool heroKnockedOut,
            bool wasChasingLastFrame,
            int[] ownership, CaptureTuning tuning)
        {
            return Decide(opponentX, opponentZ, heroX, heroZ, heroKnockedOut, wasChasingLastFrame,
                          ownership, tuning, CaptureBoardSpec.V080Seven);
        }

        // v0.9.0 多載（V090_ENCIRCLE_PLAN.md E19、V9-A15）：規則不變，只把板塊資料換成 spec；ownership 長度＝spec.TileCount。
        public static CaptureOpponentDecision Decide(
            float opponentX, float opponentZ,
            float heroX, float heroZ, bool heroKnockedOut,
            bool wasChasingLastFrame,
            int[] ownership, CaptureTuning tuning, CaptureBoardSpec spec)
        {
            return Decide(opponentX, opponentZ, heroX, heroZ, heroKnockedOut, wasChasingLastFrame,
                          true, ownership, tuning, spec);
        }

        // v0.12.0：正式 19 塊局由對稱視野先決定紅方是否知道英雄位置；舊規格呼叫上方多載，維持原行為。
        public static CaptureOpponentDecision Decide(
            float opponentX, float opponentZ,
            float heroX, float heroZ, bool heroKnockedOut,
            bool wasChasingLastFrame, bool heroVisible,
            int[] ownership, CaptureTuning tuning, CaptureBoardSpec spec)
        {
            bool chase = false;
            if (!heroKnockedOut && heroVisible)
            {
                float dx = heroX - opponentX;
                float dz = heroZ - opponentZ;
                float distSq = dx * dx + dz * dz;
                // 遲滯：還沒在追時用「開始追打」門檻；已經在追時用較寬的「放棄追打」門檻，兩者都含邊界。
                float thresholdSq = wasChasingLastFrame
                    ? tuning.ChaseGiveUpDistance * tuning.ChaseGiveUpDistance
                    : tuning.ChaseStartDistance * tuning.ChaseStartDistance;
                chase = distSq <= thresholdSq;
            }

            if (chase) return new CaptureOpponentDecision(true, -1);

            int target = SelectTargetTile(opponentX, opponentZ, ownership, spec);
            return new CaptureOpponentDecision(false, target);
        }

        // 不屬紅方的塊中，選對手到塔心平面距離平方最小的；平手選索引小的（迴圈以嚴格 < 比較，
        // 同分不覆寫既有的較小索引）；一塊都沒有回 -1。
        public static int SelectTargetTile(float opponentX, float opponentZ, int[] ownership)
        {
            return SelectTargetTile(opponentX, opponentZ, ownership, CaptureBoardSpec.V080Seven);
        }

        // v0.9.0 多載：同一條規則，板塊資料來自 spec（V9-A15）。
        public static int SelectTargetTile(float opponentX, float opponentZ, int[] ownership, CaptureBoardSpec spec)
        {
            int best = -1;
            float bestDistSq = 0f;
            for (int i = 0; i < spec.TileCount; i++)
            {
                if (ownership[i] == CaptureMatchLogic.RedFactionId) continue;
                float dx = spec.CenterX(i) - opponentX;
                float dz = spec.CenterZ(i) - opponentZ;
                float distSq = dx * dx + dz * dz;
                if (best == -1 || distSq < bestDistSq)
                {
                    best = i;
                    bestDistSq = distSq;
                }
            }
            return best;
        }

        // v0.14.0 多載（V0140_CANYON_PLAN.md §6 修訂框、V14-A21）：terrain 為峽谷地形時改用「走路距離」
        // （WalkNeighbor 圖上以塔心連線長為權重的最短路，平手取索引小）；terrain == null（平地夾具、舊規格）
        // 或不是 CanyonTerrainSpec 時沿用上面的直線距離規則。
        // 起點塊＝TileAt(對手)；棋盤外（-1）時取塔心平面距離平方最小的塊（嚴格 <，平手取索引小）；站在斜坡上照常取所在塊。
        public static int SelectTargetTile(float opponentX, float opponentZ, int[] ownership, CaptureBoardSpec spec,
                                           ITerrainQuery terrain)
        {
            CanyonTerrainSpec canyon = terrain as CanyonTerrainSpec;
            if (canyon == null) return SelectTargetTile(opponentX, opponentZ, ownership, spec);

            int start = spec.TileAt(opponentX, opponentZ);
            if (start < 0) start = NearestCenterTile(opponentX, opponentZ, spec);

            int chosen = -1;
            float chosenWalk = 0f;
            for (int t = 0; t < spec.TileCount; t++)
            {
                if (ownership[t] == CaptureMatchLogic.RedFactionId) continue;
                float walk = canyon.WalkDistance(start, t);
                if (chosen < 0 || walk < chosenWalk)
                {
                    chosen = t;
                    chosenWalk = walk;
                }
            }
            return chosen;
        }

        private static int NearestCenterTile(float x, float z, CaptureBoardSpec spec)
        {
            int nearest = -1;
            float nearestSq = 0f;
            for (int t = 0; t < spec.TileCount; t++)
            {
                float ex = spec.CenterX(t) - x;
                float ez = spec.CenterZ(t) - z;
                float sq = ex * ex + ez * ez;
                if (nearest < 0 || sq < nearestSq)
                {
                    nearest = t;
                    nearestSq = sq;
                }
            }
            return nearest;
        }
    }
}
