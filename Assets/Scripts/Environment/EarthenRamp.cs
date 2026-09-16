using System.Collections;
using UnityEngine;

/// <summary>
/// 《VOW 誓約》三層立體地勢：土石斜坡 (Earthen Ramp)
/// 規則：
/// 1. 低層英雄朝崖壁劃線，直接生成一條傾斜的土石斜坡。
/// 2. 英雄可踩著斜坡 0.5 秒直接殺上高台。
/// 3. 6 秒後地脈修復沉降自毀。
/// </summary>
public class EarthenRamp : MonoBehaviour
{
    [SerializeField] private float lifetime = 6.0f;
    [SerializeField] private float riseSpeed = 8.0f;

    private Vector3 targetPos;
    private Vector3 hiddenPos;

    public void Initialize(Vector3 bottomPoint, Vector3 topPoint)
    {
        Vector3 direction = (topPoint - bottomPoint).normalized;
        float distance = Vector3.Distance(bottomPoint, topPoint);

        // 設置中心點與旋轉角度
        transform.position = (bottomPoint + topPoint) * 0.5f;
        transform.rotation = Quaternion.LookRotation(direction);

        // 設置坡長度與傾角
        transform.localScale = new Vector3(3.0f, 0.4f, distance);

        targetPos = transform.position;
        hiddenPos = targetPos + Vector3.down * 4f;
        transform.position = hiddenPos;

        StartCoroutine(LifecycleRoutine());
    }

    private IEnumerator LifecycleRoutine()
    {
        // 1. 破土而出的升起動畫
        float elapsed = 0f;
        while (elapsed < 1f)
        {
            elapsed += Time.deltaTime * riseSpeed;
            transform.position = Vector3.Lerp(hiddenPos, targetPos, elapsed);
            yield return null;
        }
        transform.position = targetPos;

        // 2. 存在窗口 (供隊友策馬殺上高台)
        yield return new WaitForSeconds(lifetime);

        // 3. 沉降銷毀
        elapsed = 0f;
        while (elapsed < 1f)
        {
            elapsed += Time.deltaTime * (riseSpeed * 0.5f);
            transform.position = Vector3.Lerp(targetPos, hiddenPos, elapsed);
            yield return null;
        }

        Destroy(gameObject);
    }
}
