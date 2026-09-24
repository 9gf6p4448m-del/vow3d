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
        // ownership 用 CaptureMatchLogic 的陣營代碼（BlueFactionId/RedFactionId/NeutralFactionId），7 個元素。
        public static CaptureOpponentDecision Decide(
            float opponentX, float opponentZ,
            float heroX, float heroZ, bool heroKnockedOut,
            bool wasChasingLastFrame,
            int[] ownership, CaptureTuning tuning)
        {
            bool chase = false;
            if (!heroKnockedOut)
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

            int target = SelectTargetTile(opponentX, opponentZ, ownership);
            return new CaptureOpponentDecision(false, target);
        }

        // 不屬紅方的塊中，選對手到塔心平面距離平方最小的；平手選索引小的（迴圈以嚴格 < 比較，
        // 同分不覆寫既有的較小索引）；一塊都沒有回 -1。
        public static int SelectTargetTile(float opponentX, float opponentZ, int[] ownership)
        {
            int best = -1;
            float bestDistSq = 0f;
            for (int i = 0; i < HexBoardLayout.TileCount; i++)
            {
                if (ownership[i] == CaptureMatchLogic.RedFactionId) continue;
                float dx = HexBoardLayout.CenterX(i) - opponentX;
                float dz = HexBoardLayout.CenterZ(i) - opponentZ;
                float distSq = dx * dx + dz * dz;
                if (best == -1 || distSq < bestDistSq)
                {
                    best = i;
                    bestDistSq = distSq;
                }
            }
            return best;
        }
    }
}
