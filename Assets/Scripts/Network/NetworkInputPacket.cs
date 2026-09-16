using System;
using UnityEngine;

/// <summary>
/// 《VOW 誓約》網絡輸入數據包與預測驗證結構
/// 規則：
/// 1. 包含序列號、客戶端時間戳、微衝刺向量與目標 ID。
/// 2. 支援伺服器端 Ping 波動補償與樂觀回滾校驗。
/// </summary>
[System.Serializable]
public struct CadenceInputPacket
{
    public uint sequenceId;
    public float clientTimestamp;
    public Vector3 moveDirection;
    public bool isMicroDash;
    public int targetEntityId;

    public CadenceInputPacket(uint seq, float time, Vector3 dir, bool dash, int targetId)
    {
        sequenceId = seq;
        clientTimestamp = time;
        moveDirection = dir;
        isMicroDash = dash;
        targetEntityId = targetId;
    }
}

public enum PredictionState
{
    Unverified,
    ValidatedByServer,
    ReconciledRollback
}
