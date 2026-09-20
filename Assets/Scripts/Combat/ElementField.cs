using UnityEngine;
using Vow.Combat.Feedback;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Combat
{
    // 三大元素反應的 Unity 端結算（PHASE2_BATCH4_PLAN.md §1 步驟 B、§4）。
    //
    // 持有 `ElementZoneField`（純邏輯的區域名冊），負責四件事：
    //   ① 把三顆除錯鈕與石牆落點翻成 `ElementReactionLogic.Resolve` 的輸入，再把結果落到場上
    //   ② AOE／DoT 結算——走 `CombatTargetRoster` ＋ `ElementGeometry` 的純邏輯幾何，
    //      **不用 `Physics.Overlap*`**（§4-5：規格講的是圓心距離，碰撞體半徑會把最近反例的鑑別力吃掉）
    //   ③ 區域視覺的借還（池 8 > 同時存活上限 6，§4-3）
    //   ④ 流沙／蒸氣對外的查詢面（`IElementFieldQuery`），給 `HeroController` 每幀問
    //
    // 執行順序 -900：晚於 `Phase1Bootstrap`（-1000）、早於英雄與其餘一切（0）。
    // 區域的 Tick／到期／視覺同步一律先做完，英雄那一幀讀到的才是當幀狀態
    //（救援／爆沸「當幀解除縛足」靠的就是這個順序）。
    [DefaultExecutionOrder(-900)]
    public sealed class ElementField : MonoBehaviour, IElementFieldQuery
    {
        private ElementTuning _tuning = new ElementTuning();
        private ElementZoneField _field;
        private CombatTargetRoster _roster;
        private ICombatFeedbackService _feedback;
        private SectorTelegraph _telegraph;
        private ElementZoneView[] _views = System.Array.Empty<ElementZoneView>();

        // Resolve 之前的全場快照：`ReactionOutcome` 不回傳被消耗區域的座標（§8 的介面事實），
        // 爆沸的 AOE 圓心／半徑只能從這裡反查。固定容量、零配置。
        private int[] _snapshotIds;
        private float[] _snapshotX;
        private float[] _snapshotZ;
        private float[] _snapshotRadius;
        private int _snapshotCount;

        public int QuicksandFormedCount { get; private set; }
        public int SteamFormedCount { get; private set; }
        public int FirestormCount { get; private set; }
        public int BoilCount { get; private set; }
        public int RescueCount { get; private set; }
        public int PlainFireCount { get; private set; }
        public int ViewBorrowCount { get; private set; }
        public int ViewReturnCount { get; private set; }
        public int ComboCount { get; private set; }

        public ElementTuning Tuning => _tuning;
        public int ActiveZoneCount => _field != null ? _field.ActiveCount : 0;
        public int ZoneCapacity => _field != null ? _field.Capacity : 0;
        public int ViewPoolSize => _views != null ? _views.Length : 0;

        public int VisibleViewCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _views.Length; i++) if (_views[i] != null && _views[i].IsVisible) n++;
                return n;
            }
        }

        // 由 Phase1Bootstrap 注入。views 為 SceneBuilder 預建的池（執行期禁止 CreatePrimitive）。
        public void Initialize(ElementTuning tuning, CombatTargetRoster roster, ICombatFeedbackService feedback,
                               SectorTelegraph telegraph, ElementZoneView[] views)
        {
            _tuning = tuning ?? new ElementTuning();
            _field = new ElementZoneField(_tuning);
            _roster = roster;
            _feedback = feedback;
            _telegraph = telegraph;
            _views = views ?? System.Array.Empty<ElementZoneView>();

            int capacity = _field.Capacity;
            _snapshotIds = new int[capacity];
            _snapshotX = new float[capacity];
            _snapshotZ = new float[capacity];
            _snapshotRadius = new float[capacity];

            for (int i = 0; i < _views.Length; i++) if (_views[i] != null) _views[i].Release();

            // 全場目標共用同一份 tuning：受擊顯影 1.5s 的來源只有一個。
            if (_roster != null)
                for (int i = 0; i < _roster.Count; i++)
                {
                    CombatTargetBehaviour target = _roster.GetBehaviour(i);
                    if (target != null) target.ConfigureConcealment(_tuning);
                }

            // 批 3 r1 CRITICAL-1 的同型陷阱：池的物件數（SceneBuilder）與同時存活上限（tuning）是兩個
            // 互不相干的常數。池不比上限多的話，「擠掉剩餘時間最短那個」永遠走不到——第 7 次施放
            // 在玩家眼裡就是「按鈕沒反應」。V4-m 有一條測試守這個 LogError。
            if (_views.Length <= _tuning.MaxLiveZones)
                Debug.LogError("[VOW] ElementField: 區域視覺池只有 " + _views.Length + " 個，必須比同時存活上限 "
                               + _tuning.MaxLiveZones + " 多至少一個緩衝，否則池滿時新區域生得出來卻看不見。", this);
        }

        // ───────────────────────── 每幀 ─────────────────────────

        private void Update()
        {
            if (_field == null) return;
            float dt = Time.deltaTime;

            _field.Tick(dt);
            TickConcealment(dt);
            ApplyBurningDamageOverTime(dt);
            SyncViews();
        }

        private void TickConcealment(float dt)
        {
            if (_roster == null) return;
            for (int i = 0; i < _roster.Count; i++)
            {
                CombatTargetBehaviour target = _roster.GetBehaviour(i);
                if (target != null) target.TickConcealment(dt);
            }
        }

        // 燃燒區 DoT：每秒 BurnDamagePerSecond，對「落在圓內且與該區陣營敵對」的目標。
        private void ApplyBurningDamageOverTime(float dt)
        {
            if (_roster == null || dt <= 0f) return;
            float amount = _tuning.BurnDamagePerSecond * dt;
            if (amount <= 0f) return;

            for (int slot = 0; slot < _field.Capacity; slot++)
            {
                if (!_field.TryGetBySlot(slot, out ElementZone zone)) continue;
                if (zone.Kind != ElementZoneKind.Burning) continue;
                ApplyCircleDamage(amount, zone.FactionId, zone.X, zone.Z, zone.Radius);
            }
        }

        // 視覺池以「區域 id」對帳：場上有、池裡沒有的就借一個；池裡有、場上已經沒有的就還回去。
        private void SyncViews()
        {
            // ① 先還：綁定的 id 已經不在場上（到期、被反應消耗、池滿被擠掉）
            for (int i = 0; i < _views.Length; i++)
            {
                ElementZoneView view = _views[i];
                if (view == null || view.BoundZoneId < 0) continue;
                if (_field.TryGetById(view.BoundZoneId, out ElementZone _)) continue;
                view.Release();
                ViewReturnCount++;
            }

            // ② 再借／更新
            for (int slot = 0; slot < _field.Capacity; slot++)
            {
                if (!_field.TryGetBySlot(slot, out ElementZone zone)) continue;

                ElementZoneView bound = FindViewFor(zone.Id);
                if (bound == null)
                {
                    bound = FindFreeView();
                    if (bound == null) continue; // 池不夠：Initialize 已經 LogError 過
                    ViewBorrowCount++;
                }
                bound.Bind(zone.Id, zone.Kind, zone.X, zone.Z, zone.Radius);
            }
        }

        private ElementZoneView FindViewFor(int zoneId)
        {
            for (int i = 0; i < _views.Length; i++)
                if (_views[i] != null && _views[i].BoundZoneId == zoneId) return _views[i];
            return null;
        }

        private ElementZoneView FindFreeView()
        {
            for (int i = 0; i < _views.Length; i++)
                if (_views[i] != null && _views[i].BoundZoneId < 0) return _views[i];
            return null;
        }

        // ───────────────────────── 施放入口 ─────────────────────────
        // 冷卻閘在 Phase1Bootstrap 的按鈕委派裡，不在這裡：測試要湊滿 6 個同時存活的區域（V4-m），
        // 三顆鈕各 5s 冷卻是湊不出來的。這幾個方法就是按鈕委派呼叫的同一條路徑。

        // 水域沒有反應（`ElementReactionLogic.Resolve` 的 Water 分支回 None）：直接生一個水域。
        public int CastWater(Vector3 groundPoint, int factionId)
        {
            if (_field == null) return -1;
            return _field.Spawn(ElementZoneKind.Water, groundPoint.x, groundPoint.z,
                                _tuning.WaterRadius, _tuning.WaterDurationSeconds, factionId);
        }

        public ElementReaction CastFire(Vector3 groundPoint, int factionId)
        {
            return Cast(ElementCast.Fire, factionId, groundPoint.x, groundPoint.z, 0f, 0f);
        }

        // 風以英雄本體為扇形頂點、朝面向（§4-11）。預警一律顯示，掃不到燃燒區時沒有任何傷害與區域（§4-10）。
        public ElementReaction CastWind(Vector3 apex, Vector3 direction, int factionId)
        {
            if (_telegraph != null)
                _telegraph.Show(apex, direction, _tuning.FirestormRangeMeters, _tuning.FirestormAngleDegrees, 0.6f);

            direction.y = 0f;
            return Cast(ElementCast.Wind, factionId, apex.x, apex.z, direction.x, direction.z);
        }

        // 岩＝既有石牆（使用者裁定 1）：`RuneWall.Activate` 落點在水域內就凝結成流沙。
        public ElementReaction NotifyWallActivated(Vector3 position, Faction owner)
        {
            return Cast(ElementCast.Rock, (int)owner, position.x, position.z, 0f, 0f);
        }

        private ElementReaction Cast(ElementCast cast, int factionId, float x, float z, float dirX, float dirZ)
        {
            if (_field == null) return ElementReaction.None;

            TakeSnapshot();
            ReactionOutcome outcome = ElementReactionLogic.Resolve(cast, factionId, x, z, dirX, dirZ, _field, _tuning);
            ResolveDamage(outcome, x, z, dirX, dirZ);
            CountReaction(outcome.Reaction);

            // Combo 反饋只掛流沙成形、蒸氣成形、火浪三個（§4-8，GDD.md:132）——救援與爆沸是流沙的破除鏈，
            // 不是新的 Combo。純邏輯層已經把這條規則放進 `IsCombo`，這裡只忠實轉達。
            if (outcome.IsCombo && _feedback != null)
            {
                ComboCount++;
                _feedback.TriggerHitstop(_tuning.ComboHitstopMs);
                _feedback.RequestCameraShake(_tuning.ComboTrauma, 0.2f);
            }
            return outcome.Reaction;
        }

        private void CountReaction(ElementReaction reaction)
        {
            switch (reaction)
            {
                case ElementReaction.Quicksand: QuicksandFormedCount++; break;
                case ElementReaction.Steam: SteamFormedCount++; break;
                case ElementReaction.Firestorm: FirestormCount++; break;
                case ElementReaction.Boil: BoilCount++; break;
                case ElementReaction.Rescue: RescueCount++; break;
                case ElementReaction.PlainFire: PlainFireCount++; break;
            }
        }

        private void TakeSnapshot()
        {
            _snapshotCount = 0;
            for (int slot = 0; slot < _field.Capacity; slot++)
            {
                if (!_field.TryGetBySlot(slot, out ElementZone zone)) continue;
                _snapshotIds[_snapshotCount] = zone.Id;
                _snapshotX[_snapshotCount] = zone.X;
                _snapshotZ[_snapshotCount] = zone.Z;
                _snapshotRadius[_snapshotCount] = zone.Radius;
                _snapshotCount++;
            }
        }

        private bool TryGetSnapshot(int zoneId, out float x, out float z, out float radius)
        {
            for (int i = 0; i < _snapshotCount; i++)
            {
                if (_snapshotIds[i] != zoneId) continue;
                x = _snapshotX[i];
                z = _snapshotZ[i];
                radius = _snapshotRadius[i];
                return true;
            }
            x = 0f; z = 0f; radius = 0f;
            return false;
        }

        private void ResolveDamage(ReactionOutcome outcome, float castX, float castZ, float dirX, float dirZ)
        {
            if (outcome.AoeDamage <= 0f) return;

            if (outcome.Reaction == ElementReaction.Firestorm)
            {
                // 「掃到」的定義定死＝燃燒區圓心落在扇形內（§4-10）；傷害對象＝落在**同一個扇形**內的目標。
                ApplySectorDamage(outcome.AoeDamage, outcome.DamageFactionId, castX, castZ, dirX, dirZ);
                return;
            }

            if (outcome.Reaction == ElementReaction.Boil)
            {
                // 爆沸打的是「流沙圈內」——圓心與半徑取被消耗那個流沙的（`ReactionOutcome` 不回傳，靠快照反查）。
                if (TryGetSnapshot(outcome.ConsumedZoneId, out float qx, out float qz, out float qr))
                    ApplyCircleDamage(outcome.AoeDamage, outcome.DamageFactionId, qx, qz, qr);
                return;
            }

            // 空地火的直傷：以落點為圓心、燃燒區半徑（若這一發是在刷新既有燃燒區，新區的圓心是舊圓心，
            // 但直傷打的是「火砸下來的地方」，所以用 castX/castZ）。
            ApplyCircleDamage(outcome.AoeDamage, outcome.DamageFactionId, castX, castZ, _tuning.BurnRadius);
        }

        private void ApplyCircleDamage(float amount, int castFactionId, float cx, float cz, float radius)
        {
            if (_roster == null) return;
            for (int i = 0; i < _roster.Count; i++)
            {
                CombatTargetBehaviour target = _roster.GetBehaviour(i);
                if (!IsElementDamageable(target, castFactionId)) continue;

                Vector3 position = target.TargetTransform.position;
                if (!ElementGeometry.IsInsideCircle(position.x, position.z, cx, cz, radius)) continue;
                target.ReceiveDamage(amount, DamageType.Elemental, null);
            }
        }

        private void ApplySectorDamage(float amount, int castFactionId, float apexX, float apexZ, float dirX, float dirZ)
        {
            if (_roster == null) return;
            for (int i = 0; i < _roster.Count; i++)
            {
                CombatTargetBehaviour target = _roster.GetBehaviour(i);
                if (!IsElementDamageable(target, castFactionId)) continue;

                Vector3 position = target.TargetTransform.position;
                if (!ElementGeometry.IsInsideSector(position.x, position.z, apexX, apexZ, dirX, dirZ,
                        _tuning.FirestormRangeMeters, _tuning.FirestormAngleDegrees)) continue;
                target.ReceiveDamage(amount, DamageType.Elemental, null);
            }
        }

        // §4-7：對 ICombatTarget 沿用 CanBeTargetedBy（石牆與中立皆可打、其餘不得同陣營），
        // 另外**一律跳過「OwnerFaction ＝施放陣營」的石牆**——自家牆不吃自家元素傷害
        //（GDD.md:300 陣營歸屬校驗的同一精神）。
        private static bool IsElementDamageable(CombatTargetBehaviour target, int castFactionId)
        {
            if (target == null || !target.IsAlive || target.TargetTransform == null) return false;

            Faction castFaction = (Faction)castFactionId;
            if (target.TargetFaction == Faction.DestructibleWall && target.OwnerFaction == castFaction) return false;
            return target.CanBeTargetedBy(castFaction);
        }

        // ───────────────────────── IElementFieldQuery ─────────────────────────

        public bool IsConcealedFrom(Vector3 targetPosition, bool targetRevealed, Vector3 attackerPosition)
        {
            if (_field == null) return false;

            int steamId = _field.FindNearestContaining(targetPosition.x, targetPosition.z, ElementZoneKind.Steam);
            bool targetInsideSteam = steamId >= 0;
            bool attackerInsideSameSteam = false;
            if (targetInsideSteam && _field.TryGetById(steamId, out ElementZone steam))
                attackerInsideSameSteam = ElementGeometry.IsInsideCircle(attackerPosition.x, attackerPosition.z,
                                                                        steam.X, steam.Z, steam.Radius);

            return SteamConcealmentLogic.IsConcealed(targetInsideSteam, attackerInsideSameSteam, targetRevealed);
        }

        // 只認「敵對」流沙：自家流沙不困自己（V4-c）。多個重疊時取圓心最近者、平手取 id 小者（決定性）。
        public int FindHostileQuicksandId(Vector3 position, int factionId)
        {
            if (_field == null) return -1;

            int bestId = -1;
            float bestDistSq = 0f;
            for (int slot = 0; slot < _field.Capacity; slot++)
            {
                if (!_field.TryGetBySlot(slot, out ElementZone zone)) continue;
                if (zone.Kind != ElementZoneKind.Quicksand) continue;
                if (!ElementReactionLogic.IsHostile(factionId, zone.FactionId)) continue;
                if (!ElementGeometry.IsInsideCircle(position.x, position.z, zone.X, zone.Z, zone.Radius)) continue;

                float dx = position.x - zone.X;
                float dz = position.z - zone.Z;
                float distSq = dx * dx + dz * dz;
                if (bestId != -1 && distSq > bestDistSq) continue;
                if (bestId != -1 && distSq == bestDistSq && zone.Id >= bestId) continue;

                bestId = zone.Id;
                bestDistSq = distSq;
            }
            return bestId;
        }

        // ───────────────────────── 驗收用的唯讀查詢 ─────────────────────────

        public bool TryGetZoneBySlot(int slot, out ElementZone zone)
        {
            if (_field == null) { zone = default; return false; }
            return _field.TryGetBySlot(slot, out zone);
        }

        public bool TryGetZoneById(int id, out ElementZone zone)
        {
            if (_field == null) { zone = default; return false; }
            return _field.TryGetById(id, out zone);
        }

        // 這個區域此刻有沒有借到一個「真的看得見」的視覺（V4-m：第 7 個區域必須出現在場上）。
        public bool IsZoneVisible(int zoneId)
        {
            for (int i = 0; i < _views.Length; i++)
                if (_views[i] != null && _views[i].BoundZoneId == zoneId) return _views[i].IsVisible;
            return false;
        }

        public int FindZoneIdContaining(Vector3 position, ElementZoneKind kind)
        {
            return _field != null ? _field.FindNearestContaining(position.x, position.z, kind) : -1;
        }

        public int CountZonesOfKind(ElementZoneKind kind)
        {
            if (_field == null) return 0;
            int n = 0;
            for (int slot = 0; slot < _field.Capacity; slot++)
                if (_field.TryGetBySlot(slot, out ElementZone zone) && zone.Kind == kind) n++;
            return n;
        }
    }
}
