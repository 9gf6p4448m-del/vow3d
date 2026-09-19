using UnityEngine;
using UnityEngine.AI;
using Vow.Core.Logic;

namespace Vow.Core
{
    // 點地導航與位移落地。
    // NavMesh 100% 靜態預烘焙、石牆不 carving（紅線 5），所以 NavMeshAgent 本身「看不見」石牆：
    // 這裡只拿 agent 算路徑方向，實際位移一律經 ApplyDisplacement 以 SphereCast 對實體 Collider 做裁切與貼牆滑動。
    // Phase 2 批 2 起，另外接上 0.5m 阻擋格點的向量場（GridNavigator）負責繞牆——
    // **但只在 SetNavigator 餵了非 null 的導航器之後**。`_navigator == null` 時本類別的行為與 v0.3.2 (1931b53) 逐行相同。
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

        // ── Phase 2 批 2：格點繞牆。以下欄位只有 _navigator != null 時才會被讀到 ──
        private GridNavigator _navigator;
        private float _inflateRadius = 0.35f;  // 推出重疊時當作身體半徑用（＝格點的外擴量）
        private Vector3 _rawDestination;       // 使用者點的原始目的地；格點版本變了要拿它重新解析
        private float _goalX;
        private float _goalZ;
        private bool _navigatorOrder;          // 當下這道指令是不是在有導航器的情況下發出的（指令早於 SetNavigator 就不接手）
        private int _resolvedGridVersion = -1;
        private SteerMode _lastSteerMode = SteerMode.Direct;

        // 供測試觀察「這一幀到底走的是哪條路」：Direct＝沿用 Phase 1 的 NavMesh 速度，Follow＝格點向量場。
        public SteerMode LastSteerMode => _lastSteerMode;

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

        // 接上格點導航器。navigator 為 null（或從未呼叫）時，本類別的行為與 v0.3.2 逐行相同。
        // inflateRadius＝推出重疊時當作身體半徑用的值，呼叫端傳 NavGridTuning.BodyRadius（＝格點外擴量）。
        public void SetNavigator(GridNavigator navigator, float inflateRadius)
        {
            // r1 對抗審查 L4：拔掉導航器時，agent 的目的地可能還停在上一次解析出來的替代點——
            // 交還控制權之前先把它換回使用者真正點的那個位置。整段都在「舊的 _navigator 非空」之內，
            // 導航器從未接上過（_navigator == null）時這裡一行都不會執行，行為仍與 v0.3.2 逐行相同。
            if (_navigator != null && _navigatorOrder && _hasOrder && _chaseTarget == null
                && _agent.enabled && _agent.isOnNavMesh)
                _agent.SetDestination(_rawDestination);

            _navigator = navigator;
            _inflateRadius = inflateRadius;
            _resolvedGridVersion = -1;
            _navigatorOrder = false;
            _lastSteerMode = SteerMode.Direct;
        }

        public void MoveTo(Vector3 destination)
        {
            if (!TryPlaceOnNavMesh()) return; // 不在 NavMesh 上時 SetDestination 只會噴錯；先試著放回去，放不回就不下指令
            _chaseTarget = null;
            _hasOrder = true;
            _agent.isStopped = false;
            if (_navigator != null)
            {
                _rawDestination = destination;
                _agent.SetDestination(ResolveGoal(destination));
                return;
            }
            _agent.SetDestination(destination);
        }

        public void Chase(Transform target)
        {
            if (!TryPlaceOnNavMesh()) return;
            _chaseTarget = target;
            _hasOrder = true;
            _repathTimer = 0f;
            _agent.isStopped = false;
            if (_navigator != null)
            {
                if (target != null)
                {
                    _rawDestination = target.position;
                    _agent.SetDestination(ResolveGoal(target.position));
                }
                return;
            }
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
                        if (_navigator != null)
                        {
                            _rawDestination = _chaseTarget.position;
                            _agent.SetDestination(ResolveGoal(_chaseTarget.position));
                        }
                        else _agent.SetDestination(_chaseTarget.position);
                    }
                }
                // 牆出現或消失：還持有移動指令的英雄用原始目的地重新解析一次（計畫書 §4 假設 6）。
                // 追擊每 0.1s 本來就重解析，不必再走這條。
                else if (_navigator != null && _navigatorOrder && _resolvedGridVersion != _navigator.Grid.Version
                         && _agent.enabled && _agent.isOnNavMesh)
                {
                    _agent.SetDestination(ResolveGoal(_rawDestination));
                }

                Vector3 velocity = _agent.enabled && _agent.isOnNavMesh ? _agent.desiredVelocity : Vector3.zero;
                velocity.y = 0f;
                if (_navigator != null) velocity = SteerAroundWalls(velocity);
                if (velocity.sqrMagnitude > 1e-6f)
                {
                    ApplyDisplacement(velocity * dt);
                    Quaternion look = Quaternion.LookRotation(velocity, Vector3.up);
                    _self.rotation = Quaternion.RotateTowards(_self.rotation, look, _turnSpeed * dt);
                }
            }

            SyncAgent();
        }

        // ───────────────────── Phase 2 批 2：格點繞牆（只在 _navigator != null 時走到）─────────────────────

        // 解析目的地：走得到就原樣用，走不到（被圍死／點在牆腳）就退到「最近可達點」（使用者裁定 2）。
        // 回傳值直接餵給 _agent.SetDestination，所以 HasArrived 不必改——它比的一直是 agent 當下的目的地。
        private Vector3 ResolveGoal(Vector3 destination)
        {
            _navigatorOrder = true;
            _resolvedGridVersion = _navigator.Grid.Version;

            // r1 對抗審查 H3（§6 R2）：格點外的目的地先夾進格點再照常解析。
            // 舊實作在這裡整趟退回 Phase 1，實測 z=19 繞得過去、z=20 卻頂在牆上——那條分支唯一的作用
            // 是讓兩個與批 2 行為互相矛盾的既有測試維持綠燈，已依 §6 R2 刪除。
            _navigator.Grid.ClampToGrid(destination.x, destination.z, out float destX, out float destZ);

            Vector3 position = _self.position;
            _navigator.ResolveGoal(position.x, position.z, destX, destZ,
                out float goalX, out float goalZ, out _);
            _goalX = goalX;
            _goalZ = goalZ;
            return new Vector3(goalX, destination.y, goalZ);
        }

        // 把 NavMesh 算出來的速度換成「繞得過牆」的速度。
        // Direct（到目的地有視線）一律原樣回傳：沒有牆擋路時，批 2 對 Phase 1 手感零影響（計畫書 §4 假設 3）。
        private Vector3 SteerAroundWalls(Vector3 navMeshVelocity)
        {
            if (!_navigatorOrder) return navMeshVelocity; // 這道指令早於 SetNavigator，格點沒有它的解析結果

            Vector3 position = _self.position;
            SteerMode mode = _navigator.Steer(position.x, position.z, _goalX, _goalZ, out float dirX, out float dirZ);

            // r1 對抗審查 M4：解析出來的 goal 格被新的一面牆蓋住了（追擊最長 0.1s 的重解析窗口）。
            // 當幀立刻拿原始目的地重新解析，不得無聲退回 v0.3.2 的頂牆。
            if (mode == SteerMode.GoalBlocked && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.SetDestination(ResolveGoal(_rawDestination));
                mode = _navigator.Steer(position.x, position.z, _goalX, _goalZ, out dirX, out dirZ);
            }
            _lastSteerMode = mode;

            // Stuck＝連逃脫格都找不到（10m 內全是 Blocked，實務上不會發生）。退回 Phase 1 行為，至少不比 v0.3.2 差。
            if (mode != SteerMode.Follow) return navMeshVelocity;

            // 速度沿用 agent 這一幀算出來的大小；agent 認為已到達（直線距離近）但格點還要繞路時退回設定速度。
            float speed = navMeshVelocity.magnitude;
            if (speed < 1e-6f) speed = _agent.speed;
            return new Vector3(dirX * speed, 0f, dirZ * speed);
        }

        // 石牆立在英雄身上時把他推到最近的空格（使用者裁定 3）。只看實體重疊（圓對 OBB），不看外擴區——
        // 貼牆站著是常態，不該被彈開（計畫書 §4 假設 5）。回傳有沒有真的動過。
        public bool EjectFromBox(float centerX, float centerZ, float normalX, float normalZ,
                                 float halfWidth, float halfThickness)
        {
            if (_navigator == null) return false;

            Vector3 position = _self.position;
            if (!BlockGrid.CircleOverlapsBox(position.x, position.z, _inflateRadius,
                                             centerX, centerZ, normalX, normalZ, halfWidth, halfThickness))
                return false;

            // M5：搜尋半徑讀 GridNavigator 持有的 tuning，不在這裡另外複製一份常數（NavGridTuning 是單一來源）。
            if (!_navigator.Grid.TryFindNearestFree(position.x, position.z, _navigator.Tuning.EscapeSearchRadiusCells,
                                                    out int cx, out int cz))
                return false;

            _navigator.Grid.CellCenter(cx, cz, out float x, out float z);
            _self.position = new Vector3(x, position.y, z);
            SyncAgent();
            return true;
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
