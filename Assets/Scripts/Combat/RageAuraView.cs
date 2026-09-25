using UnityEngine;
using Vow.Core;

namespace Vow.Combat
{
    // v0.9.0 劣勢狂怒的世界空間光環（V090_ENCIRCLE_PLAN.md E18、R4）。只讀 ICaptureMatchView，不碰 CaptureMatchLogic。
    //
    // 兩個扁圓盤由 VOWPhase1SceneBuilder 預建，是**獨立的根物件**、不掛在英雄／對手底下：既有測試 AssertBody 會斷言
    // 英雄／對手底下所有 Renderer 的開關，倒地時也會把它們一併關掉（R4）。這裡在 LateUpdate 讓圓盤跟隨身體的 xz，
    // 該隊狂怒中（剩餘 > 0）而且英雄沒有倒地時才顯示。只切 Renderer.enabled（不 SetActive），執行期零配置。
    public sealed class RageAuraView : MonoBehaviour
    {
        [SerializeField] private Renderer _blueAura;
        [SerializeField] private Renderer _redAura;

        private ICaptureMatchView _view;
        private Transform _heroBody;
        private Transform _opponentBody;
        private Transform _blueTransform;
        private Transform _redTransform;
        private float _blueY;
        private float _redY;

        public Renderer BlueAura => _blueAura;
        public Renderer RedAura => _redAura;

        // 光環顯示／隱藏的累計切換次數（零配置量測的活性，V9-C03）。
        public int ToggleCount { get; private set; }

        public void Initialize(ICaptureMatchView view, Transform heroBody, Transform opponentBody)
        {
            _view = view;
            _heroBody = heroBody;
            _opponentBody = opponentBody;
            // Transform 包裝在這裡抓一次（第一次讀 Component.transform 才建出 managed 包裝，不讓它落在 LateUpdate）。
            _blueTransform = _blueAura != null ? _blueAura.transform : null;
            _redTransform = _redAura != null ? _redAura.transform : null;
            _blueY = _blueTransform != null ? _blueTransform.position.y : 0f;
            _redY = _redTransform != null ? _redTransform.position.y : 0f;
            if (_blueAura != null) _blueAura.enabled = false;
            if (_redAura != null) _redAura.enabled = false;
            if (_blueAura == null || _redAura == null)
                Debug.LogError("[VOW] RageAuraView 缺少光環 Renderer：狂怒在畫面上看不見（規則照跑）。" +
                               "請執行 VOW/Phase 1/Build Greybox Scene 重建場景。", this);
        }

        private void LateUpdate()
        {
            if (_view == null) return;
            Refresh(_blueAura, _blueTransform, _blueY, _heroBody, _view.BlueRageRemaining > 0f && !_view.BlueKnockedOut);
            Refresh(_redAura, _redTransform, _redY, _opponentBody, _view.RedRageRemaining > 0f && !_view.RedKnockedOut);
        }

        private void Refresh(Renderer aura, Transform auraTransform, float y, Transform body, bool shown)
        {
            if (aura == null) return;
            if (shown && body != null)
            {
                Vector3 p = body.position;
                auraTransform.position = new Vector3(p.x, y, p.z);
            }
            if (aura.enabled == shown) return;
            aura.enabled = shown;
            ToggleCount++;
        }
    }
}
