namespace Vow.Core.Logic
{
    // 微位移的完整可模擬狀態：純值型別，可整包複製、回滾、重播（為 30/60 Tick 客戶端預測預留）。
    public struct CadenceSimState
    {
        public int Charges;
        public float RecoveryTimer;       // 累積中的回充時間，0 ~ ChargeRecoverySeconds
        public int ChainCount;            // 目前連段已滑幾次
        public float SinceLastDash;       // 距上一次滑步起手的秒數

        public bool DashActive;
        public float DashElapsed;
        public float DashDistance;        // 本次滑步的目標距離（已套用衰減）
        public float DashDirX;
        public float DashDirZ;
        public float DashTraveled;        // 本次滑步已走距離（閉式解，與步長無關）

        public static CadenceSimState CreateFull(CombatTuning tuning)
        {
            CadenceSimState s = default;
            s.Charges = tuning.MaxCharges;
            s.SinceLastDash = float.MaxValue;
            return s;
        }
    }

    public struct CadenceSimInput
    {
        public bool DashRequested;
        public float DirX;
        public float DirZ;
    }

    public struct CadenceSimStepResult
    {
        public bool DashStarted;          // 本步是否成功起手（扣了 1 格充能）
        public bool DashRejectedNoCharge; // 有要求但充能為 0
        public bool DashFinished;
        public float StartedDistance;
        public float DeltaX;              // 本步應施加的位移（尚未做碰撞裁切）
        public float DeltaZ;
    }

    public static class CadenceSim
    {
        // 純函數：輸出只由 (state, input, dt, tuning) 決定，不讀任何外部狀態。
        public static CadenceSimState SimulateStep(
            CadenceSimState state, CadenceSimInput input, float dt, CombatTuning tuning, out CadenceSimStepResult result)
        {
            result = default;
            if (dt < 0f) dt = 0f;

            // 1) 起手判定在時間推進之前：指令到達的當下就生效（0 幀）。
            if (input.DashRequested && !state.DashActive)
            {
                if (state.Charges <= 0)
                {
                    result.DashRejectedNoCharge = true;
                }
                else if (TryNormalize(input.DirX, input.DirZ, out float nx, out float nz))
                {
                    if (state.SinceLastDash > tuning.ChainWindowSeconds) state.ChainCount = 0;

                    state.DashDistance = tuning.DashDistanceForChainIndex(state.ChainCount);
                    state.ChainCount++;
                    state.Charges--;
                    state.SinceLastDash = 0f;
                    state.DashActive = true;
                    state.DashElapsed = 0f;
                    state.DashTraveled = 0f;
                    state.DashDirX = nx;
                    state.DashDirZ = nz;

                    result.DashStarted = true;
                    result.StartedDistance = state.DashDistance;
                }
            }

            // 2) 充能回復
            if (state.Charges < tuning.MaxCharges)
            {
                state.RecoveryTimer += dt;
                while (state.RecoveryTimer >= tuning.ChargeRecoverySeconds && state.Charges < tuning.MaxCharges)
                {
                    state.RecoveryTimer -= tuning.ChargeRecoverySeconds;
                    state.Charges++;
                }
                if (state.Charges >= tuning.MaxCharges) state.RecoveryTimer = 0f;
            }
            else
            {
                state.RecoveryTimer = 0f;
            }

            // 3) 連段計時（飽和加法，避免 float.MaxValue 溢位成 Infinity 後比較行為改變）
            if (state.SinceLastDash < float.MaxValue - dt) state.SinceLastDash += dt;

            // 4) 滑步運動學：用「已走距離的閉式解」相減求本步位移，結果與步長切法無關。
            if (state.DashActive)
            {
                state.DashElapsed += dt;
                float duration = tuning.DashDurationSeconds;
                float p = duration <= 0f ? 1f : state.DashElapsed / duration;
                if (p > 1f) p = 1f;

                float target = state.DashDistance * EaseOutCubic(p);
                float step = target - state.DashTraveled;
                state.DashTraveled = target;
                result.DeltaX = state.DashDirX * step;
                result.DeltaZ = state.DashDirZ * step;

                if (p >= 1f)
                {
                    state.DashActive = false;
                    result.DashFinished = true;
                }
            }

            return state;
        }

        public static float RecoveryNormalized(CadenceSimState state, CombatTuning tuning)
        {
            if (state.Charges >= tuning.MaxCharges) return 1f;
            if (tuning.ChargeRecoverySeconds <= 0f) return 1f;
            float n = state.RecoveryTimer / tuning.ChargeRecoverySeconds;
            return n < 0f ? 0f : (n > 1f ? 1f : n);
        }

        // 下一次滑步若現在起手會走多遠（給 HUD／預警箭頭預覽用）。
        public static float PeekNextDashDistance(CadenceSimState state, CombatTuning tuning)
        {
            int chain = state.SinceLastDash > tuning.ChainWindowSeconds ? 0 : state.ChainCount;
            return tuning.DashDistanceForChainIndex(chain);
        }

        private static float EaseOutCubic(float p)
        {
            float inv = 1f - p;
            return 1f - inv * inv * inv;
        }

        private static bool TryNormalize(float x, float z, out float nx, out float nz)
        {
            double lenSq = (double)x * x + (double)z * z;
            if (lenSq < 1e-12)
            {
                nx = 0f; nz = 0f;
                return false;
            }
            double inv = 1.0 / System.Math.Sqrt(lenSq);
            nx = (float)(x * inv);
            nz = (float)(z * inv);
            return true;
        }
    }
}
