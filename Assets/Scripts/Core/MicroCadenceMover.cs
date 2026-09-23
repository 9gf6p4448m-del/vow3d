using System;
using UnityEngine;
using Vow.Core.Logic;

namespace Vow.Core
{
    // ICadenceMover 的 Unity 端實作：狀態與運動學全部委託給純函數 CadenceSim.SimulateStep，
    // 本類別只負責把每步算出的位移交給 HeroLocomotion 落地（含實體 Collider 裁切），以及對外發事件。
    // 日後做 30/60 Tick 客戶端預測時，伺服器與客戶端重播的是同一個 SimulateStep。
    [RequireComponent(typeof(HeroLocomotion))]
    public sealed class MicroCadenceMover : MonoBehaviour, ICadenceMover
    {
        private HeroLocomotion _locomotion;
        private CombatTuning _tuning;
        private CadenceSimState _state;

        public int CurrentCharges => _state.Charges;
        public int MaxCharges => _tuning != null ? _tuning.MaxCharges : 0;
        public float ChargeRecoveryNormalized => _tuning != null ? CadenceSim.RecoveryNormalized(_state, _tuning) : 0f;
        public bool IsDashing => _state.DashActive;
        public float NextDashDistance => _tuning != null ? CadenceSim.PeekNextDashDistance(_state, _tuning) : 0f;

        public event Action<int> OnChargesChanged;
        public event Action<float> OnDashExecuted;
        public event Action OnCadenceExhausted;

        public void Configure(CombatTuning tuning)
        {
            _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
            _locomotion = GetComponent<HeroLocomotion>();
            _state = CadenceSimState.CreateFull(tuning);
            OnChargesChanged?.Invoke(_state.Charges);
        }

        public void ResetForRound()
        {
            if (_tuning == null) return;
            _state = CadenceSimState.CreateFull(_tuning);
            OnChargesChanged?.Invoke(_state.Charges);
        }

        public bool TryExecuteCadenceDash(Vector3 worldDirection)
        {
            if (_tuning == null) return false;

            CadenceSimInput input = default;
            input.DashRequested = true;
            input.DirX = worldDirection.x;
            input.DirZ = worldDirection.z;

            // dt = 0：指令到達當下立即起手，不等下一幀
            _state = CadenceSim.SimulateStep(_state, input, 0f, _tuning, out CadenceSimStepResult result);

            if (result.DashRejectedNoCharge)
            {
                OnCadenceExhausted?.Invoke();
                return false;
            }
            if (!result.DashStarted) return false;

            _locomotion.FaceDirection(new Vector3(_state.DashDirX, 0f, _state.DashDirZ));
            OnChargesChanged?.Invoke(_state.Charges);
            OnDashExecuted?.Invoke(result.StartedDistance);
            if (_state.Charges == 0) OnCadenceExhausted?.Invoke();
            return true;
        }

        // 由 HeroController.Update 統一驅動。
        public void Step(float dt)
        {
            if (_tuning == null) return;

            int chargesBefore = _state.Charges;
            _state = CadenceSim.SimulateStep(_state, default, dt, _tuning, out CadenceSimStepResult result);

            if (result.DeltaX != 0f || result.DeltaZ != 0f)
                _locomotion.ApplyDisplacement(new Vector3(result.DeltaX, 0f, result.DeltaZ));

            if (_state.Charges != chargesBefore) OnChargesChanged?.Invoke(_state.Charges);
        }
    }
}
