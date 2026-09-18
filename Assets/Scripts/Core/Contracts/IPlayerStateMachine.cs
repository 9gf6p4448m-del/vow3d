using System;

namespace Vow.Core
{
    // ARCHITECTURE.md §參-5：英雄七態。狀態是唯一事實來源，禁止另立布林旗標描述同一件事。
    public enum PlayerState
    {
        Idle,               // 待機
        Moving,             // 點地導航步行中
        AttackWindup,       // 普攻前搖 (可被新移動指令打斷)
        AttackRelease,      // 普攻傷害判定幀 (結算傷害 + 開啟 220ms 目押切後搖窗口)
        CadenceDashing,     // 微位移中 (0 幀切除後搖，執行 1.4m/0.9m/0.5m 衰減位移)
        AttackRecovery,     // 常規普攻後搖 (0.15s 平滑收招，絕無卡刀硬直)
        CastingRune         // 符印拖曳/施法狀態 (Phase 2 才接線)
    }

    public interface IPlayerStateMachine
    {
        PlayerState CurrentState { get; }
        bool CanMove { get; }
        bool CanAttack { get; }
        bool IsInCadenceWindow { get; } // 220ms 寬容窗口

        void ChangeState(PlayerState newState);

        event Action<PlayerState, PlayerState> OnStateChanged; // (oldState, newState)
    }
}
