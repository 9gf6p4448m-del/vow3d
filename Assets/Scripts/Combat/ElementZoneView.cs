using UnityEngine;
using Vow.Core.Logic;

namespace Vow.Combat
{
    // 池化的扁圓柱區域視覺（灰盒：沒有粒子、沒有著色器，冷庫協議）。
    //
    // 紅線：執行期禁止 `CreatePrimitive`（IL2CPP 會剔除沒被場景引用的類別）——圓柱本體與四份材質
    // 一律由 `VOWPhase1SceneBuilder` 預建並以序列化欄位餵進來。
    // **沒有 Collider、不進 NavGrid**：區域不擋路、不吃點擊射線（V4-q）。
    [DisallowMultipleComponent]
    public sealed class ElementZoneView : MonoBehaviour
    {
        // 依 ElementZoneKind 取用：索引 1=Water、2=Burning、3=Quicksand、4=Steam（0 不用）。
        [SerializeField] private Material[] _kindMaterials = new Material[5];
        [SerializeField] private Renderer _visual;

        private Transform _self;
        private int _shownKind = -1;

        // 目前借給哪一個區域 id；-1 ＝ 在池裡待命。
        public int BoundZoneId { get; private set; } = -1;
        public bool IsVisible => _visual != null && _visual.enabled;

        private void Awake()
        {
            _self = transform;
            if (_visual == null) _visual = GetComponentInChildren<Renderer>(true);
            Release();
        }

        // 借出：綁定一個區域 id 並套上該種類的外觀。重複呼叫同一個 id 只更新幾何（零配置）。
        public void Bind(int zoneId, ElementZoneKind kind, float x, float z, float radius)
        {
            if (_self == null) _self = transform;
            BoundZoneId = zoneId;

            int kindCode = (int)kind;
            if (kindCode != _shownKind)
            {
                _shownKind = kindCode;
                if (_visual != null && _kindMaterials != null
                    && kindCode >= 0 && kindCode < _kindMaterials.Length && _kindMaterials[kindCode] != null)
                    _visual.sharedMaterial = _kindMaterials[kindCode];
            }

            // Unity 的 Cylinder 預設高 2m（localScale.y 是半高）；壓成 0.04m 的薄餅，直徑＝2×半徑。
            _self.position = new Vector3(x, 0.03f, z);
            _self.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);
            if (_visual != null && !_visual.enabled) _visual.enabled = true;
        }

        public void Release()
        {
            BoundZoneId = -1;
            if (_visual != null && _visual.enabled) _visual.enabled = false;
        }
    }
}
