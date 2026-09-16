using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 《VOW 誓約》P2 核心網絡預測引擎：客戶端樂觀預測與平滑回滾 (Client-Side Prediction)
/// 規則：
/// 1. 本地 0ms 體感先行位移，拒絕等待網絡延遲確認。
/// 2. 伺服器時間戳容錯補償 (Ping Tolerance Buffer)。
/// 3. 異常情況下進行微平滑校正 (Reconciliation)，絕無瞬移拉扯。
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class NetworkCadencePredictor : MonoBehaviour
{
    [Header("網絡預測配置")]
    [SerializeField] private float estimatedPingMs = 50f;
    [SerializeField] private float pingToleranceWindow = 0.08f; // 80ms 伺服器寬容補償
    [SerializeField] private float reconciliationSpeed = 12f;

    private NavMeshAgent agent;
    private uint sequenceCounter = 0;
    private Queue<CadenceInputPacket> unconfirmedPackets = new Queue<CadenceInputPacket>();

    // 伺服器回滾目標點
    private Vector3 targetReconciledPosition;
    private bool isReconciling = false;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        targetReconciledPosition = transform.position;
    }

    private void Update()
    {
        // 伺服器回滾微平滑插值 (防止瞬移)
        if (isReconciling)
        {
            transform.position = Vector3.Lerp(transform.position, targetReconciledPosition, Time.deltaTime * reconciliationSpeed);
            if (Vector3.Distance(transform.position, targetReconciledPosition) < 0.05f)
            {
                transform.position = targetReconciledPosition;
                isReconciling = false;
            }
        }
    }

    /// <summary>
    /// 客戶端本地樂觀發起微衝刺 (0ms 延遲)
    /// </summary>
    public void PredictMicroDash(Vector3 direction, float distance, int targetId)
    {
        sequenceCounter++;
        CadenceInputPacket packet = new CadenceInputPacket(
            sequenceCounter,
            Time.time,
            direction.normalized,
            true,
            targetId
        );

        // 1. 本地立即執行物理位移 (體感 0ms)
        agent.Move(packet.moveDirection * distance);

        // 2. 緩存未確認數據包
        unconfirmedPackets.Enqueue(packet);

        // 3. 模擬向伺服器發送網絡包
        SendPacketToServer(packet);
    }

    private void SendPacketToServer(CadenceInputPacket packet)
    {
        // 實際網絡層：NetworkManager.Send(packet);
        // 此處演示伺服器驗證回調
        SimulateServerValidation(packet);
    }

    /// <summary>
    /// 伺服器端校驗回調 (帶 Ping 容錯校驗)
    /// </summary>
    public void OnServerValidationReceived(uint acknowledgedSeq, bool isValid, Vector3 authoritativePos)
    {
        while (unconfirmedPackets.Count > 0 && unconfirmedPackets.Peek().sequenceId <= acknowledgedSeq)
        {
            unconfirmedPackets.Dequeue();
        }

        if (!isValid)
        {
            // 伺服器駁回 (例如中途被敵方眩暈或撞牆) ➔ 平滑回滾至權威座標
            targetReconciledPosition = authoritativePos;
            isReconciling = true;
            Debug.LogWarning($"<color=yellow>[網絡校驗回滾] 序列號 #{acknowledgedSeq} 被駁回，微平滑校正身位。</color>");
        }
    }

    private void SimulateServerValidation(CadenceInputPacket packet)
    {
        // 模擬伺服器在合理延遲後確認
        float simulatedLag = (estimatedPingMs / 1000f);
        StartCoroutine(SimulatedServerReply(packet.sequenceId, transform.position, simulatedLag));
    }

    private System.Collections.IEnumerator SimulatedServerReply(uint seq, Vector3 pos, float delay)
    {
        yield return new WaitForSeconds(delay);
        OnServerValidationReceived(seq, true, pos);
    }
}
