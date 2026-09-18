using UnityEngine;
using UnityEngine.AI;

namespace Vow.Core
{
    // 點地導航與位移落地。
    // NavMesh 100% 靜態預烘焙、石牆不 carving（紅線 5），所以 NavMeshAgent 本身「看不見」石牆：
    // 這裡只拿 agent 算路徑方向，實際位移一律經 ApplyDisplacement 以 SphereCast 對實體 Collider 做裁切與貼牆滑動。
    // （繞牆尋路屬 Phase 2 的 0.5m 格點向量場，Phase 1 僅保證「撞牆會被擋住」。）
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class HeroLocomotion : MonoBehaviour
    {
        private const float SkinWidth = 0.02f;
        private const float ChaseRepathInterval = 0.1f;
        private const float CastHeight = 0.9f;

        private readonly RaycastHit[] _hits = new RaycastHit[8];

        private NavMeshAgent _agent;
        private Transform _self;
        private Transform _chaseTarget;
        private bool _hasOrder;
        private float _repathTimer;
        private float _turnSpeed = 1080f;
        private float _bodyRadius = 0.35f;
        private bool _offMeshReported;

        public bool HasArrived
        {
            get
            {
                if (!_hasOrder || !_agent.enabled) return true;
                if (_agent.pathPending) return false;
                return _agent.remainingDistance <= _agent.stoppingDistance + 0.05f;
            }
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        // 同一物件上各元件的 Awake 順序 Unity 不保證；HeroController.Awake 可能先呼叫 Configure，所以初始化做成惰性且可重入。
        private void EnsureInitialized()
        {
            if (_agent != null) return;

            _self = transform;
            _agent = GetComponent<NavMeshAgent>();
            _agent.updatePosition = false;
            _agent.updateRotation = false;
            _agent.autoBraking = false;
            _agent.stoppingDistance = 0.05f;
        }

        // NavMesh 資料由地板上的 NavMeshSurface 在 OnEnable 時載入。agent 若比它早啟用，Unity 會回報
        // 「Failed to create agent because there is no valid NavMesh」且不會把它放上網格（WebGL 建置實際遇到）。
        // 所以 SceneBuilder 把 agent 存成停用狀態，等到 Start——此時全場的 OnEnable 都已跑完——才啟用。
        private void Start()
        {
            if (!_agent.enabled) _agent.enabled = true;
            TryPlaceOnNavMesh();
        }

        private bool TryPlaceOnNavMesh()
        {
            if (!_agent.enabled) return false; // Start 之前：尚未啟用
            if (_agent.isOnNavMesh) return true;

            if (NavMesh.SamplePosition(_self.position, out NavMeshHit hit, 4f, NavMesh.AllAreas) && _agent.Warp(hit.position))
                return true;

            // 症狀是「點了地板英雄卻不動」，沒有這行訊息的話幾乎無從查起。只報一次，避免洗版。
            if (!_offMeshReported)
            {
                _offMeshReported = true;
                Debug.LogError("[VOW] 英雄不在 NavMesh 上，且 4m 內找不到可放回的位置：移動指令將被忽略。" +
                               "請確認場景已由 VOW/Phase 1/Build Greybox Scene 生成、NavMesh 資產存在。", this);
            }
            return false;
        }

        public void Configure(float moveSpeed, float turnSpeedDegreesPerSecond, float bodyRadius)
        {
            EnsureInitialized();
            _agent.speed = moveSpeed;
            _agent.acceleration = 1000f;   // 純點擊手感：起步與急停不拖泥帶水
            _agent.angularSpeed = 0f;
            _agent.radius = bodyRadius;
            _turnSpeed = turnSpeedDegreesPerSecond;
            _bodyRadius = bodyRadius;
        }

        public void MoveTo(Vector3 destination)
        {
            if (!TryPlaceOnNavMesh()) return; // 不在 NavMesh 上時 SetDestination 只會噴錯；先試著放回去，放不回就不下指令
            _chaseTarget = null;
            _hasOrder = true;
            _agent.isStopped = false;
            _agent.SetDestination(destination);
        }

        public void Chase(Transform target)
        {
            if (!TryPlaceOnNavMesh()) return;
            _chaseTarget = target;
            _hasOrder = true;
            _repathTimer = 0f;
            _agent.isStopped = false;
            if (target != null) _agent.SetDestination(target.position);
        }

        public void Stop()
        {
            _chaseTarget = null;
            if (!_hasOrder) return;
            _hasOrder = false;
            if (_agent.enabled && _agent.isOnNavMesh) _agent.ResetPath();
        }

        public void FaceTowards(Vector3 worldPosition)
        {
            Vector3 dir = worldPosition - _self.position;
            FaceDirection(dir);
        }

        public void FaceDirection(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) return;
            _self.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        // 由 HeroController.Update 統一驅動，確保「移動 → 滑步 → 大腦」的執行順序固定。
        public void Step(float dt)
        {
            if (_hasOrder)
            {
                if (_chaseTarget != null)
                {
                    _repathTimer -= dt;
                    if (_repathTimer <= 0f && _agent.enabled && _agent.isOnNavMesh)
                    {
                        _repathTimer = ChaseRepathInterval;
                        _agent.SetDestination(_chaseTarget.position);
                    }
                }

                Vector3 velocity = _agent.enabled && _agent.isOnNavMesh ? _agent.desiredVelocity : Vector3.zero;
                velocity.y = 0f;
                if (velocity.sqrMagnitude > 1e-6f)
                {
                    ApplyDisplacement(velocity * dt);
                    Quaternion look = Quaternion.LookRotation(velocity, Vector3.up);
                    _self.rotation = Quaternion.RotateTowards(_self.rotation, look, _turnSpeed * dt);
                }
            }

            SyncAgent();
        }

        // 施加一段水平位移；遇到實體 Collider 時裁切並沿牆面滑動一次。回傳實際走出的位移。
        public Vector3 ApplyDisplacement(Vector3 delta)
        {
            delta.y = 0f;
            Vector3 start = _self.position;
            Vector3 remaining = delta;

            for (int iteration = 0; iteration < 2; iteration++)
            {
                float distance = remaining.magnitude;
                if (distance < 1e-5f) break;

                Vector3 direction = remaining / distance;
                Vector3 origin = _self.position + Vector3.up * CastHeight;
                // AllLayers：場地的隱形邊界牆放在 Ignore Raycast 層（不擋點擊射線），但必須擋得住身體
                int count = Physics.SphereCastNonAlloc(origin, _bodyRadius, direction, _hits, distance + SkinWidth,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);

                bool blocked = false;
                float nearest = float.MaxValue;
                Vector3 normal = Vector3.zero;
                for (int i = 0; i < count; i++)
                {
                    RaycastHit hit = _hits[i];
                    if (hit.collider.transform.root == _self.root) continue;
                    if (hit.distance <= 0f && hit.point == Vector3.zero) continue; // 起點已重疊：放行，讓角色能走出來
                    if (hit.normal.y > 0.7f) continue;                             // 地面
                    if (hit.distance < nearest)
                    {
                        nearest = hit.distance;
                        normal = hit.normal;
                        blocked = true;
                    }
                }

                if (!blocked)
                {
                    _self.position += remaining;
                    break;
                }

                float travel = Mathf.Max(0f, nearest - SkinWidth);
                _self.position += direction * travel;

                Vector3 leftover = remaining - direction * travel;
                normal.y = 0f;
                if (normal.sqrMagnitude < 1e-6f) break;
                normal.Normalize();
                remaining = leftover - Vector3.Dot(leftover, normal) * normal;
            }

            SyncAgent();
            return _self.position - start;
        }

        // 把 agent 的內部位置拉到角色身上，再讀回來：位置若落在 NavMesh 之外，agent 會夾回邊界，角色跟著被夾回。
        private void SyncAgent()
        {
            if (!_agent.enabled || !_agent.isOnNavMesh) return;
            _agent.nextPosition = _self.position;
            Vector3 clamped = _agent.nextPosition;
            clamped.y = _self.position.y;
            _self.position = clamped;
        }
    }
}
