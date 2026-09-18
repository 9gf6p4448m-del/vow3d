using System;
using UnityEngine;

namespace Vow.Core
{
    // ARCHITECTURE.md §參-2
    public interface ICadenceMover
    {
        int CurrentCharges { get; }
        int MaxCharges { get; }
        float ChargeRecoveryNormalized { get; } // 0.0 ~ 1.0 (2.5秒一格)

        // 執行位移 (內建 1.0s 窗口動能衰減：1.4m → 0.9m → 0.5m)
        bool TryExecuteCadenceDash(Vector3 worldDirection);

        event Action<int> OnChargesChanged;
        event Action<float> OnDashExecuted; // 傳出實際位移距離
        event Action OnCadenceExhausted;    // 充能歸零
    }
}
