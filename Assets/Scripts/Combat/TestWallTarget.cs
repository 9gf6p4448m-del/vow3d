using UnityEngine;
using Vow.Core;

namespace Vow.Combat
{
    // Phase 1 測試石牆：只有 BoxCollider 的實體阻擋，陣營為 DestructibleWall。
    // 紅線 5：這裡沒有、也不准加 NavMeshObstacle——NavMesh 100% 靜態預烘焙，石牆的阻擋完全靠物理碰撞。
    // （符印石牆 IRuneWall 的壽命、穿透、坍塌屬 Phase 2，不在此實作。）
    [RequireComponent(typeof(BoxCollider))]
    public sealed class TestWallTarget : CombatTargetBehaviour
    {
        [SerializeField] private Renderer _wallRenderer;
        [SerializeField] private float _respawnSeconds = 6f;

        private BoxCollider _collider;
        private float _respawnTimer;
        private TargetOverheadDisplay _overhead;

        // v0.9.0 E22（使用者 2026-09-25 同意，V090_ENCIRCLE_PLAN.md §5 Q6）：兩面測試牆壓進 6 號、2 號光圈，
        // 佔領模式（Lobby／Active／Ended）時停用——Renderer（含頭頂血條）與 Collider 關閉、從格點取消登記；
        // 回 Off 時恢復並重新登記。切換當下若正在碎裂重生倒數中就維持隱藏，倒數在停用期間暫停，
        // 回 Off 後由原本的重生流程打開。單挑模式從不呼叫 SetCaptureSuppressed，行為與 v0.8.0 逐行相同。
        public bool IsCaptureSuppressed { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            _collider = GetComponent<BoxCollider>();
            if (_wallRenderer == null) _wallRenderer = GetComponentInChildren<Renderer>();
            _overhead = GetComponent<TargetOverheadDisplay>();
        }

        public void SetCaptureSuppressed(bool suppressed)
        {
            if (IsCaptureSuppressed == suppressed) return;
            IsCaptureSuppressed = suppressed;
            if (_overhead != null) _overhead.SetHidden(suppressed);
            if (!IsAlive) return; // 碎裂中：維持隱藏、不擋路，交給重生流程打開

            bool shown = !suppressed;
            _collider.enabled = shown;
            if (_wallRenderer != null) _wallRenderer.enabled = shown;
            if (suppressed) UnregisterNavBlocker();
            else RegisterNavBlocker(_collider);
        }

        // Phase 2 批 2：靜態測試牆一開場就擋路，所以在 Start 登記（Phase1Bootstrap 是 -1000，它的 Start 先跑完、
        // 格點已經注入）。萬一注入更晚，SetNavGrid 也會把這筆補蓋上去。
        private void Start()
        {
            if (IsAlive) RegisterNavBlocker(_collider);
        }

        protected override void HandleDeath()
        {
            _respawnTimer = _respawnSeconds;
            _collider.enabled = false;
            if (_wallRenderer != null) _wallRenderer.enabled = false;
            UnregisterNavBlocker();
        }

        private void Update()
        {
            if (_respawnTimer <= 0f || IsCaptureSuppressed) return; // 佔領模式停用期間重生倒數暫停（E22）

            _respawnTimer -= Time.deltaTime;
            if (_respawnTimer > 0f) return;

            _collider.enabled = true;
            if (_wallRenderer != null) _wallRenderer.enabled = true;
            Revive();
            RegisterNavBlocker(_collider); // 重生＝重新擋路
        }
    }
}
