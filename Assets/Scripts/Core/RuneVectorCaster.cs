using System.Collections;
using UnityEngine;

/// <summary>
/// 《VOW 誓約》P1 核心施法器：右側符印目標軸心向量施法 (Rune-Vector Smart Cast)
/// 規則：
/// 1. 玩家大拇指按住右下角符印，系統自動以鎖定目標身後錨定為「起點 A」。
/// 2. 大拇指在符印上的滑動向量決定石牆的旋轉角度與「終點 B」。
/// 3. 鬆手瞬間 (0.08s 極速) 生成石牆，與走A 操作絕對解耦、零誤觸。
/// </summary>
public class RuneVectorCaster : MonoBehaviour
{
    [Header("預製體與圖層")]
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private LayerMask targetLayer;

    [Header("技能數值")]
    [SerializeField] private float castRange = 8.0f;
    [SerializeField] private float wallLength = 7.0f;
    [SerializeField] private float cooldownTime = 6.0f;

    [Header("UI / 螢幕符印觸發區 (右下角)")]
    [SerializeField] private Rect runeTouchArea = new Rect(0.75f, 0.15f, 0.2f, 0.25f); // 歸一化螢幕比例

    // 狀態追蹤
    private Camera mainCam;
    private bool isHoldingRune = false;
    private Vector2 runeDragStart;
    private Vector2 currentDragDelta;
    private float nextReadyTime = 0f;
    private Vector3 currentAnchorPointA;
    private Quaternion currentWallRotation;

    public bool IsReady => Time.time >= nextReadyTime;
    public float CooldownRemaining => Mathf.Max(0f, nextReadyTime - Time.time);

    private void Awake()
    {
        mainCam = Camera.main;
    }

    private void Update()
    {
        HandleRuneInput();
    }

    private void HandleRuneInput()
    {
        // 1. 按下檢測 (支援滑鼠右鍵或觸控右下角符印區)
        if (Input.GetMouseButtonDown(1) || (Input.GetMouseButtonDown(0) && IsTouchInRuneArea(Input.mousePosition)))
        {
            if (!IsReady)
            {
                Debug.Log($"<color=grey>符印冷卻中: {CooldownRemaining:F1}s</color>");
                return;
            }

            isHoldingRune = true;
            runeDragStart = Input.mousePosition;
            currentDragDelta = Vector2.up; // 預設朝向
            AnchorStartingPointA();
        }

        // 2. 拖曳中：旋轉調整終點 B 的角度
        if (isHoldingRune && (Input.GetMouseButton(1) || Input.GetMouseButton(0)))
        {
            Vector2 mousePos = Input.mousePosition;
            Vector2 delta = mousePos - runeDragStart;
            if (delta.magnitude > 10f)
            {
                currentDragDelta = delta.normalized;
            }

            UpdateWallOrientation();
        }

        // 3. 鬆手即釋放 (0.08s 極速盲操)
        if (isHoldingRune && (Input.GetMouseButtonUp(1) || Input.GetMouseButtonUp(0)))
        {
            isHoldingRune = false;
            CastWall();
        }
    }

    /// <summary>
    /// 自動為石牆尋找最佳「起點 A」
    /// </summary>
    private void AnchorStartingPointA()
    {
        // 優先以滑鼠指針所指的目標為軸心
        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hitTarget, 100f, targetLayer))
        {
            currentAnchorPointA = hitTarget.point;
            return;
        }

        // 否則錨定在角色正前方射程內
        currentAnchorPointA = transform.position + transform.forward * (castRange * 0.6f);
    }

    /// <summary>
    /// 根據手指推動的向量更新石牆的旋轉角度
    /// </summary>
    private void UpdateWallOrientation()
    {
        Vector3 camForward = mainCam.transform.forward;
        Vector3 camRight = mainCam.transform.right;
        camForward.y = 0;
        camRight.y = 0;
        camForward.Normalize();
        camRight.Normalize();

        Vector3 worldDir = (camRight * currentDragDelta.x + camForward * currentDragDelta.y).normalized;
        currentWallRotation = Quaternion.LookRotation(worldDir);
    }

    private void CastWall()
    {
        if (wallPrefab == null)
        {
            Debug.LogError("未指定 wallPrefab 預製體！");
            return;
        }

        nextReadyTime = Time.time + cooldownTime;

        // 生成石牆實體
        GameObject wall = Instantiate(wallPrefab);
        DynamicOBBWall obbWall = wall.GetComponent<DynamicOBBWall>();
        if (obbWall != null)
        {
            obbWall.Initialize(currentAnchorPointA, currentWallRotation);
        }
        else
        {
            wall.transform.position = currentAnchorPointA;
            wall.transform.rotation = currentWallRotation;
            Destroy(wall, 5.0f);
        }

        Debug.Log("<color=green>★【符印塑形成功】石牆已升起！</color>");
    }

    private bool IsTouchInRuneArea(Vector2 screenPos)
    {
        float normX = screenPos.x / Screen.width;
        float normY = screenPos.y / Screen.height;
        return runeTouchArea.Contains(new Vector2(normX, normY));
    }

    // 在編輯器場景中繪製預覽線
    private void OnDrawGizmos()
    {
        if (!isHoldingRune) return;

        Gizmos.color = Color.cyan;
        Vector3 halfExtend = (currentWallRotation * Vector3.forward) * (wallLength * 0.5f);
        Gizmos.DrawLine(currentAnchorPointA - halfExtend, currentAnchorPointA + halfExtend);
        Gizmos.DrawWireSphere(currentAnchorPointA, 0.5f);
    }
}
