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

        protected override void Awake()
        {
            base.Awake();
            _collider = GetComponent<BoxCollider>();
            if (_wallRenderer == null) _wallRenderer = GetComponentInChildren<Renderer>();
        }

        protected override void HandleDeath()
        {
            _respawnTimer = _respawnSeconds;
            _collider.enabled = false;
            if (_wallRenderer != null) _wallRenderer.enabled = false;
        }

        private void Update()
        {
            if (_respawnTimer <= 0f) return;

            _respawnTimer -= Time.deltaTime;
            if (_respawnTimer > 0f) return;

            _collider.enabled = true;
            if (_wallRenderer != null) _wallRenderer.enabled = true;
            Revive();
        }
    }
}
