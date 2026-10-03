using UnityEngine;
using Vow.Combat;
using Vow.Core;

namespace Vow.Bootstrap
{
    // camera-lab 弓：每一發「真的結算」一支看得見的箭（2026-10-03，vow-toolchain/acceptance-bowline-20261003.md 與其修訂 R1）。
    // 結算＝英雄普攻傷到直接目標的那一刻（含蓄力、快速射擊、自動普攻；覆審 r1 H1）或錐內無人的空射（放開當下）。
    // 被冷卻／前搖吃掉、DASH／換武器／倒地等打斷而沒有結算的放開＝不生箭、不出提示（H3 由建構上消失：沒有傷害就沒有箭）。
    // 起點＝英雄胸口；終點＝目標身體中心（M2）／滿蓄穿透＝沿英雄→目標到射程盡頭／空射＝沿 aimYaw 到射程盡頭。
    // 命中提示＝箭飛到「這一發傷到的目標」那一幀，位置＝結算當下快照的身體中心（目標在箭到前死亡仍照常提示，D8）。
    public readonly struct BowArrowVisual
    {
        public readonly int Serial;          // 第幾支（1 起算）；0＝還沒射過
        public readonly Vector3 Start;
        public readonly Vector3 End;
        public readonly float Speed;         // m/s（遊戲時間）；近距離為了最短可見飛行時間會低於 40
        public readonly bool Hit;            // 這一發有傷到目標（終點＝該目標；空射＝false）
        public readonly float SpawnTime;     // Time.time

        public BowArrowVisual(int serial, Vector3 start, Vector3 end, float speed, bool hit, float spawnTime)
        {
            Serial = serial;
            Start = start;
            End = end;
            Speed = speed;
            Hit = hit;
            SpawnTime = spawnTime;
        }
    }

    // 目標身體（覆審 r1 M2／H2）：中心＝碰撞體中心（優先 enabled 的那個；換算世界座標，碰撞體停用／目標已隱藏也算得出來），
    // 半徑＝碰撞體水平半徑；BoxCollider＝三個半軸（含縮放與旋轉）投影到「水平、垂直於英雄→目標視線」方向的半寬（覆審 r2 N3，朝向相關）；
    // 一律上限 MaxRadiusMeters。沒有碰撞體＝TargetTransform 位置、半徑 0。零配置（TargetColliders 已快取）。
    internal static class BowTargetBody
    {
        public const float MaxRadiusMeters = 1.0f;   // 修訂 R2 N3


        private static Collider Pick(CombatTargetBehaviour target)
        {
            if (target == null) return null;
            Collider[] colliders = target.TargetColliders;
            Collider any = null;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c == null) continue;
                if (c.enabled) return c;
                if (any == null) any = c;
            }
            return any;
        }

        public static Vector3 Center(ICombatTarget target)
        {
            Collider c = Pick(target as CombatTargetBehaviour);
            if (c is CapsuleCollider capsule) return capsule.transform.TransformPoint(capsule.center);
            if (c is BoxCollider box) return box.transform.TransformPoint(box.center);
            if (c is SphereCollider sphere) return sphere.transform.TransformPoint(sphere.center);
            if (c != null && c.enabled && c.gameObject.activeInHierarchy) return c.bounds.center;
            Transform t = target != null ? target.TargetTransform : null;
            return t != null ? t.position : Vector3.zero;
        }

        // (sightX, sightZ)＝英雄→目標中心的水平向量（不必正規化）。
        public static float Radius(CombatTargetBehaviour target, float sightX, float sightZ)
        {
            return Mathf.Min(RawRadius(target, sightX, sightZ), MaxRadiusMeters);
        }

        private static float RawRadius(CombatTargetBehaviour target, float sightX, float sightZ)
        {
            Collider c = Pick(target);
            if (c == null) return 0f;
            Vector3 s = c.transform.lossyScale;
            float horizontalScale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
            if (c is CapsuleCollider capsule && capsule.direction == 1) return capsule.radius * horizontalScale;
            if (c is SphereCollider sphere) return sphere.radius * Mathf.Max(horizontalScale, Mathf.Abs(s.y));
            if (c is BoxCollider box)
            {
                float length = Mathf.Sqrt(sightX * sightX + sightZ * sightZ);
                float px = length > 1e-6f ? -sightZ / length : 1f;   // 水平、垂直於視線
                float pz = length > 1e-6f ? sightX / length : 0f;
                Transform t = box.transform;
                Vector3 hx = t.TransformVector(box.size.x * 0.5f, 0f, 0f);
                Vector3 hy = t.TransformVector(0f, box.size.y * 0.5f, 0f);
                Vector3 hz = t.TransformVector(0f, 0f, box.size.z * 0.5f);
                return Mathf.Abs(hx.x * px + hx.z * pz) + Mathf.Abs(hy.x * px + hy.z * pz) + Mathf.Abs(hz.x * px + hz.z * pz);
            }
            if (!c.enabled || !c.gameObject.activeInHierarchy) return 0f;
            Vector3 e = c.bounds.extents;
            return Mathf.Max(e.x, e.z);
        }
    }

    // 物件池（箭 4 支、提示閃光 6 個），全部共用場景扇形預警的頂點色材質（WebGL 已在用的同一個），
    // 顏色走 LineRenderer 頂點色，不 new 材質；執行期零配置。
    internal sealed class BowArrowFx
    {
        public const float ArrowSpeed = 40f;          // 暫定
        public const float MinFlightSeconds = 0.12f;  // 近距離也看得見（覆審 r1 M4／修訂 R1 D7）：速度 = min(40, 距離/0.12)
        public const float ChestHeight = HeroController.ChestHeightMeters;   // 穿透卷修訂 P1：箭起點與穿透判定線同一常數（值只寫在 Core 一處）
        public const float LingerSeconds = 0.1f;      // 到達後停留多久才收（規格 ≤ 0.3s）
        public const float ArrowLength = 1.6f;        // 箭身（D7：≥ 1.5m，暫定）
        public const float ArrowWidth = 0.14f;        // 箭頭端寬（D7：≥ 0.12m，暫定）；尾端 0.4 倍
        private const int ArrowPool = 4;
        private const int MaxVictims = 8;
        private const int CuePool = 6;
        private const int CuePoints = 9;              // 四芒星＋收口
        private const float ReleaseCueSeconds = 0.12f, ReleaseCueSize = 0.45f;
        private const float HitCueSeconds = 0.18f, HitCueSize = 0.9f;
        private static readonly Color ArrowHead = new Color(1f, 0.97f, 0.8f, 1f);
        private static readonly Color ArrowTail = new Color(1f, 0.75f, 0.25f, 0.35f);
        private static readonly Color ReleaseCueColor = new Color(0.6f, 0.95f, 1f, 1f);
        private static readonly Color HitCueColor = new Color(1f, 0.55f, 0.15f, 1f);

        private readonly GameObject[] _arrowObjects = new GameObject[ArrowPool];
        private readonly LineRenderer[] _arrowLines = new LineRenderer[ArrowPool];
        private readonly Vector3[] _start = new Vector3[ArrowPool];
        private readonly Vector3[] _end = new Vector3[ArrowPool];
        private readonly float[] _length = new float[ArrowPool];
        private readonly float[] _speed = new float[ArrowPool];
        private readonly float[] _traveled = new float[ArrowPool];
        private readonly float[] _age = new float[ArrowPool];
        private readonly float[] _arrivedAge = new float[ArrowPool];   // < 0＝還沒到
        private readonly bool[] _flying = new bool[ArrowPool];          // 視覺體還在
        private readonly int[] _serial = new int[ArrowPool];
        private readonly int[] _launchFrame = new int[ArrowPool];
        // 這一發傷到的目標（直接目標＋同一次結算的沿線目標）：箭飛到 _victimAt 那一幀在 _victimPos 提示一次。
        private readonly int[] _victimCount = new int[ArrowPool];
        private readonly float[] _victimAt = new float[ArrowPool * MaxVictims];
        private readonly Vector3[] _victimPos = new Vector3[ArrowPool * MaxVictims];
        private readonly bool[] _victimCued = new bool[ArrowPool * MaxVictims];

        private readonly GameObject[] _cueObjects = new GameObject[CuePool];
        private readonly LineRenderer[] _cueLines = new LineRenderer[CuePool];
        private readonly Vector3[] _cueCenter = new Vector3[CuePool];
        private readonly float[] _cueAge = new float[CuePool];
        private readonly float[] _cueLife = new float[CuePool];
        private readonly float[] _cueSize = new float[CuePool];
        private readonly Color[] _cueColor = new Color[CuePool];
        private readonly Vector3[] _cuePoints = new Vector3[CuePoints];
        private readonly Vector3[] _arrowPoints = new Vector3[2];
        private int _nextCue;
        private int _serialCounter;

        public int ReleaseCueCount { get; private set; }
        public int HitCueCount { get; private set; }
        public BowArrowVisual Last { get; private set; }
        public GameObject LastObject { get; private set; }
        public GameObject LastReleaseCueObject { get; private set; }
        public GameObject LastHitCueObject { get; private set; }
        public Vector3 LastHitCuePosition { get; private set; }

        private readonly Transform _camera;

        public BowArrowFx(Material material, Transform camera)
        {
            _camera = camera;
            GameObject root = new GameObject("BowArrowFx");
            root.layer = 2;
            for (int i = 0; i < ArrowPool; i++)
            {
                LineRenderer line = CreateLine(root.transform, "BowArrow", material, 2);
                line.widthCurve = new AnimationCurve(new Keyframe(0f, ArrowWidth * 0.4f), new Keyframe(1f, ArrowWidth));
                line.startColor = ArrowTail;
                line.endColor = ArrowHead;
                _arrowLines[i] = line;
                _arrowObjects[i] = line.gameObject;
            }
            for (int i = 0; i < CuePool; i++)
            {
                LineRenderer line = CreateLine(root.transform, "BowCue", material, CuePoints);
                line.widthMultiplier = 0.07f;
                _cueLines[i] = line;
                _cueObjects[i] = line.gameObject;
            }
        }

        private static LineRenderer CreateLine(Transform parent, string name, Material material, int points)
        {
            GameObject go = new GameObject(name);
            go.layer = 2;   // Ignore Raycast（主鏡頭 cullingMask 有畫這層，D6 測試核對）
            go.transform.SetParent(parent, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = points;
            if (material != null) line.sharedMaterial = material;
            go.SetActive(false);
            return line;
        }

        // 一發結算：生成箭＋出手提示（同一幀）。primary＝傷到的直接目標（null＝空射）；primaryAt＝它身體中心的快照（提示位置）。
        public void Launch(Vector3 start, Vector3 end, ICombatTarget primary, Vector3 primaryAt, float now, int frame)
        {
            int slot = FreeSlot();
            _serialCounter++;
            _start[slot] = start;
            _end[slot] = end;
            float length = Vector3.Distance(start, end);
            _length[slot] = length;
            _speed[slot] = length > 1e-4f ? Mathf.Min(ArrowSpeed, length / MinFlightSeconds) : ArrowSpeed;
            _traveled[slot] = 0f;
            _age[slot] = 0f;
            _arrivedAge[slot] = -1f;
            _flying[slot] = true;
            _serial[slot] = _serialCounter;
            _launchFrame[slot] = frame;
            _victimCount[slot] = 0;
            if (primary != null) AddVictim(slot, primaryAt, true);
            DrawArrow(slot);
            if (!_arrowObjects[slot].activeSelf) _arrowObjects[slot].SetActive(true);
            Last = new BowArrowVisual(_serialCounter, start, end, _speed[slot], primary != null, now);
            LastObject = _arrowObjects[slot];
            ReleaseCueCount++;
            LastReleaseCueObject = SpawnCue(start, ReleaseCueColor, ReleaseCueSeconds, ReleaseCueSize);
        }

        // 同一次結算的沿線目標（滿蓄穿透／裂風矢）：併入這一幀剛生成、有直接目標的那一支。找不到就不提示。
        public void AddLineVictim(Vector3 bodyCentre, int frame)
        {
            int best = -1;
            for (int s = 0; s < ArrowPool; s++)
                if (_flying[s] && _launchFrame[s] == frame && _victimCount[s] > 0 && (best < 0 || _serial[s] > _serial[best])) best = s;
            if (best >= 0) AddVictim(best, bodyCentre, false);
        }

        // 直接目標＝箭飛到終點那一幀提示（同 f9b4133：滿蓄穿透時終點在射程盡頭）；沿線目標＝飛到它的投影距離那一幀。
        private void AddVictim(int slot, Vector3 bodyCentre, bool primary)
        {
            int n = _victimCount[slot];
            if (n >= MaxVictims) return;
            int k = slot * MaxVictims + n;
            Vector3 path = _end[slot] - _start[slot];
            float sq = path.sqrMagnitude;
            float f = sq > 1e-6f ? Mathf.Clamp01(Vector3.Dot(bodyCentre - _start[slot], path) / sq) : 1f;
            _victimAt[k] = primary ? _length[slot] : f * _length[slot];
            _victimPos[k] = bodyCentre;
            _victimCued[k] = false;
            _victimCount[slot] = n + 1;
        }

        public void Tick(float dt)
        {
            for (int s = 0; s < ArrowPool; s++)
            {
                if (!_flying[s]) continue;
                _age[s] += dt;
                _traveled[s] = Mathf.Min(_length[s], _traveled[s] + _speed[s] * dt);
                CueArrived(s);
                if (_arrivedAge[s] < 0f && _traveled[s] >= _length[s]) _arrivedAge[s] = _age[s];
                if (_arrivedAge[s] >= 0f && _age[s] - _arrivedAge[s] >= LingerSeconds) Hide(s);
                else DrawArrow(s);
            }
            for (int i = 0; i < CuePool; i++)
            {
                if (_cueLife[i] <= 0f) continue;
                _cueAge[i] += dt;
                if (_cueAge[i] >= _cueLife[i])
                {
                    _cueLife[i] = 0f;
                    _cueObjects[i].SetActive(false);
                }
                else DrawCue(i);
            }
        }

        private void CueArrived(int slot)
        {
            for (int v = 0; v < _victimCount[slot]; v++)
            {
                int k = slot * MaxVictims + v;
                if (_victimCued[k] || _traveled[slot] + 1e-4f < _victimAt[k]) continue;
                _victimCued[k] = true;
                HitCueCount++;
                LastHitCuePosition = _victimPos[k];
                LastHitCueObject = SpawnCue(_victimPos[k], HitCueColor, HitCueSeconds, HitCueSize);
            }
        }

        private void Hide(int slot)
        {
            _flying[slot] = false;
            _victimCount[slot] = 0;
            if (_arrowObjects[slot] != null) _arrowObjects[slot].SetActive(false);
        }

        // 沒有空位就收最舊的一支（視覺與未提示的命中一起丟掉）。
        private int FreeSlot()
        {
            int oldest = 0;
            for (int s = 0; s < ArrowPool; s++)
            {
                if (!_flying[s]) return s;
                if (_serial[s] < _serial[oldest]) oldest = s;
            }
            Hide(oldest);
            return oldest;
        }

        private void DrawArrow(int s)
        {
            Vector3 dir = _length[s] > 1e-4f ? (_end[s] - _start[s]) / _length[s] : Vector3.forward;
            Vector3 head = _start[s] + dir * _traveled[s];
            _arrowPoints[0] = head - dir * Mathf.Min(ArrowLength, Mathf.Max(0.05f, _traveled[s]));
            _arrowPoints[1] = head;
            _arrowLines[s].SetPositions(_arrowPoints);
        }

        private GameObject SpawnCue(Vector3 center, Color color, float life, float size)
        {
            int i = _nextCue;
            _nextCue = (_nextCue + 1) % CuePool;
            _cueCenter[i] = center;
            _cueAge[i] = 0f;
            _cueLife[i] = life;
            _cueSize[i] = size;
            _cueColor[i] = color;
            DrawCue(i);
            if (!_cueObjects[i].activeSelf) _cueObjects[i].SetActive(true);
            return _cueObjects[i];
        }

        // 四芒星（面向鏡頭）：由小放大、淡出。
        private void DrawCue(int i)
        {
            float k = Mathf.Clamp01(_cueAge[i] / _cueLife[i]);
            float r = _cueSize[i] * (0.4f + 0.6f * k);
            float inner = r * 0.25f;
            Vector3 c = _cueCenter[i];
            Vector3 right = _camera != null ? _camera.right : Vector3.right;
            Vector3 up = _camera != null ? _camera.up : Vector3.up;
            for (int p = 0; p < CuePoints; p++)
            {
                float a = p * Mathf.PI * 0.25f;
                float rr = (p % 2 == 0) ? r : inner;
                _cuePoints[p] = c + right * (Mathf.Sin(a) * rr) + up * (Mathf.Cos(a) * rr);
            }
            LineRenderer line = _cueLines[i];
            line.SetPositions(_cuePoints);
            Color color = _cueColor[i];
            color.a *= 1f - k;
            line.startColor = color;
            line.endColor = color;
        }

        // 換武器／切 TOP／停用（覆審 r1 H4／修訂 R1 D4）：空中的箭與還沒出現的命中提示一併清掉。
        public void HideAll()
        {
            for (int s = 0; s < ArrowPool; s++) Hide(s);
            for (int i = 0; i < CuePool; i++)
            {
                _cueLife[i] = 0f;
                if (_cueObjects[i] != null) _cueObjects[i].SetActive(false);
            }
        }
    }
}
