using System;

namespace Vow.Core.Logic
{
    // 七態狀態機：合法轉換寫成一張表，任何不在表上的轉換一律拒絕。
    // 這是「杜絕布林變數互相覆蓋」的機制本體——CanMove／CanAttack／IsInCadenceWindow 全部由 CurrentState 推導，不另存旗標。
    public sealed class PlayerStateMachine : IPlayerStateMachine
    {
        private const int StateCount = 7;

        // AllowedMask[from] 的第 to 個 bit 為 1 表示允許 from → to。
        private static readonly int[] AllowedMask = BuildTransitionTable();

        public PlayerState CurrentState { get; private set; } = PlayerState.Idle;

        public bool CanMove => CurrentState == PlayerState.Idle
                               || CurrentState == PlayerState.Moving
                               || CurrentState == PlayerState.AttackWindup;

        public bool CanAttack => CurrentState == PlayerState.Idle || CurrentState == PlayerState.Moving;

        // AttackRelease 這個狀態「就是」目押窗口：窗口結束即離開此狀態，兩者不可能不同步。
        public bool IsInCadenceWindow => CurrentState == PlayerState.AttackRelease;

        public event Action<PlayerState, PlayerState> OnStateChanged;

        // 非法轉換的回報出口（純邏輯層不能呼叫 Debug.Log，由外層訂閱後輸出）。
        public event Action<PlayerState, PlayerState> OnTransitionRejected;

        public void ChangeState(PlayerState newState)
        {
            TryChangeState(newState);
        }

        // 死亡／回合重置專用；一般轉換表維持原樣。
        public void ResetToIdle()
        {
            PlayerState old = CurrentState;
            CurrentState = PlayerState.Idle;
            if (old != PlayerState.Idle) OnStateChanged?.Invoke(old, PlayerState.Idle);
        }

        public bool TryChangeState(PlayerState newState)
        {
            PlayerState old = CurrentState;
            if (old == newState) return true;

            if (!IsTransitionAllowed(old, newState))
            {
                OnTransitionRejected?.Invoke(old, newState);
                return false;
            }

            CurrentState = newState;
            OnStateChanged?.Invoke(old, newState);
            return true;
        }

        public static bool IsTransitionAllowed(PlayerState from, PlayerState to)
        {
            int f = (int)from;
            int t = (int)to;
            if (f < 0 || f >= StateCount || t < 0 || t >= StateCount) return false;
            return (AllowedMask[f] & (1 << t)) != 0;
        }

        private static int[] BuildTransitionTable()
        {
            int[] table = new int[StateCount];

            Allow(table, PlayerState.Idle, PlayerState.Moving, PlayerState.AttackWindup, PlayerState.CastingRune);
            Allow(table, PlayerState.Moving, PlayerState.Idle, PlayerState.AttackWindup, PlayerState.CastingRune);

            // 前搖：命中 → Release；被移動指令打斷 → Moving；目標失效 → Idle
            Allow(table, PlayerState.AttackWindup, PlayerState.AttackRelease, PlayerState.Moving, PlayerState.Idle);

            // 目押窗口只有兩個出口：切後搖滑步，或平滑收招。沒有任何通往硬直的路。
            Allow(table, PlayerState.AttackRelease, PlayerState.CadenceDashing, PlayerState.AttackRecovery);

            Allow(table, PlayerState.CadenceDashing, PlayerState.Idle, PlayerState.Moving, PlayerState.AttackWindup);
            Allow(table, PlayerState.AttackRecovery, PlayerState.Idle, PlayerState.Moving, PlayerState.AttackWindup);
            Allow(table, PlayerState.CastingRune, PlayerState.Idle, PlayerState.Moving);

            return table;
        }

        private static void Allow(int[] table, PlayerState from, params PlayerState[] targets)
        {
            for (int i = 0; i < targets.Length; i++) table[(int)from] |= 1 << (int)targets[i];
        }
    }
}
