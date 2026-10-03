using UnityEngine;
using Vow.Core;

namespace Vow.Bootstrap
{
    // camera-lab 弓：每次出手一支看得見的箭（2026-10-03，vow-toolchain/acceptance-bowline-20261003.md）。
    // 起點＝英雄胸口、終點＝挑到的目標（打空＝沿 aimYaw 到當下射程盡頭；滿蓄穿透＝沿英雄→目標方向到射程盡頭）。
    // 只管「看得見」：傷害仍由英雄普攻結算，時機不變。命中提示＝「這支箭真的傷到的目標」且「箭已飛到它」兩者都成立的那一幀，
    // 每個被傷到的目標各一次；沒人受傷（打空、被打斷、目標死了）就從不觸發。
    public readonly struct BowArrowVisual
    {
        public readonly int Serial;          // 第幾支（1 起算）；0＝還沒射過
        public readonly Vector3 Start;
        public readonly Vector3 End;
        public readonly float Speed;         // m/s（遊戲時間）
        public readonly bool Hit;            // 出手時挑到目標（終點＝該目標）
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

    // 物件池（箭 4 支、提示閃光 6 個），全部共用場景扇形預警的頂點色材質（WebGL 已在用的同一個），
    // 顏色走 LineRenderer 頂點色，不 new 材質；執行期零配置。
    internal sealed class BowArrowFx
    {
        public const float ArrowSpeed = 40f;          // 暫定
        public const float ChestHeight = 1.3f;
        public const float LingerSeconds = 0.1f;      // 到達後停留多久才收（規格 ≤ 0.3s）
        private const float ArrowLength = 0.9f;
        private const float ArrowWidth = 0.08f;
        private const float PendingHitSeconds = 1.4f; // 出手後等「真的傷到」的上限（＝普攻週期 0.8＋前搖保險 0.6）
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
        private readonly float[] _traveled = new float[ArrowPool];
        private readonly float[] _age = new float[ArrowPool];
        private readonly float[] _arrivedAge = new float[ArrowPool];   // < 0＝還沒到
        private readonly bool[] _flying = new bool[ArrowPool];          // 視覺體還在
        private readonly int[] _serial = new int[ArrowPool];
        // 命中提示的等待狀態（與視覺體分開：傷害可能晚於箭到達）
        private readonly ICombatTarget[] _primary = new ICombatTarget[ArrowPool];
        private readonly bool[] _pending = new bool[ArrowPool];
        private readonly bool[] _resolved = new bool[ArrowPool];
        private readonly int[] _resolvedFrame = new int[ArrowPool];
        private readonly int[] _victimCount = new int[ArrowPool];
        private readonly ICombatTarget[] _victims = new ICombatTarget[ArrowPool * MaxVictims];
        private readonly float[] _victimAt = new float[ArrowPool * MaxVictims];   // 箭飛到這段距離＝到達該目標
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
            go.layer = 2;   // Ignore Raycast
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

        // 出手：生成箭＋出手提示（同一幀）。primary＝挑到的目標（null＝打空）；piercePath＝滿蓄穿透時沿線也算受傷候選。
        public void Launch(Vector3 start, Vector3 end, ICombatTarget primary, float now)
        {
            int slot = FreeSlot();
            _serialCounter++;
            _start[slot] = start;
            _end[slot] = end;
            _length[slot] = Vector3.Distance(start, end);
            _traveled[slot] = 0f;
            _age[slot] = 0f;
            _arrivedAge[slot] = -1f;
            _flying[slot] = true;
            _serial[slot] = _serialCounter;
            _primary[slot] = primary;
            _pending[slot] = primary != null;
            _resolved[slot] = false;
            _resolvedFrame[slot] = -1;
            _victimCount[slot] = 0;
            DrawArrow(slot);
            if (!_arrowObjects[slot].activeSelf) _arrowObjects[slot].SetActive(true);
            Last = new BowArrowVisual(_serialCounter, start, end, ArrowSpeed, primary != null, now);
            LastObject = _arrowObjects[slot];
            ReleaseCueCount++;
            SpawnCue(start, ReleaseCueColor, ReleaseCueSeconds, ReleaseCueSize);
        }

        // 英雄普攻傷到一個目標（直接目標先、同一呼叫內的穿透沿線目標隨後）。
        public void NotifyDamage(ICombatTarget target, int frame)
        {
            if (target == null) return;
            // 同一幀、同一次結算的沿線目標：併入剛結算的那一支。
            for (int s = 0; s < ArrowPool; s++)
                if (_pending[s] && _resolved[s] && _resolvedFrame[s] == frame && !ReferenceEquals(target, _primary[s]))
                {
                    AddVictim(s, target);
                    return;
                }
            // 直接目標：最早射出、還沒結算、目標相同的那一支（先射的先到）。
            int best = -1;
            for (int s = 0; s < ArrowPool; s++)
            {
                if (!_pending[s] || _resolved[s] || !ReferenceEquals(target, _primary[s])) continue;
                if (best < 0 || _serial[s] < _serial[best]) best = s;
            }
            if (best < 0) return;
            _resolved[best] = true;
            _resolvedFrame[best] = frame;
            AddVictim(best, target);
        }

        private void AddVictim(int slot, ICombatTarget target)
        {
            int n = _victimCount[slot];
            if (n >= MaxVictims) return;
            int k = slot * MaxVictims + n;
            _victims[k] = target;
            _victimCued[k] = false;
            float at = _length[slot];
            Transform t = target.TargetTransform;
            if (t != null && !ReferenceEquals(target, _primary[slot]))
            {
                Vector3 path = _end[slot] - _start[slot];
                float sq = path.sqrMagnitude;
                float f = sq > 1e-6f ? Mathf.Clamp01(Vector3.Dot(t.position - _start[slot], path) / sq) : 1f;
                at = f * _length[slot];
            }
            _victimAt[k] = at;
            _victimCount[slot] = n + 1;
            TryCue(slot, false);   // 箭早已飛過（傷害晚到）→當幀就提示；收尾留給 Tick（同一呼叫後面還有沿線目標）
        }

        public void Tick(float dt)
        {
            for (int s = 0; s < ArrowPool; s++)
            {
                if (!_flying[s] && !_pending[s]) continue;
                _age[s] += dt;
                _traveled[s] = Mathf.Min(_length[s], _traveled[s] + ArrowSpeed * dt);
                if (_flying[s])
                {
                    if (_arrivedAge[s] < 0f && _traveled[s] >= _length[s]) _arrivedAge[s] = _age[s];
                    if (_arrivedAge[s] >= 0f && _age[s] - _arrivedAge[s] >= LingerSeconds)
                    {
                        _flying[s] = false;
                        _arrowObjects[s].SetActive(false);
                    }
                    else DrawArrow(s);
                }
                if (_pending[s])
                {
                    TryCue(s, true);
                    if (!_resolved[s] && _age[s] > PendingHitSeconds) ClearPending(s);   // 一直沒人受傷＝打空
                }
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

        private void TryCue(int slot, bool allowClear)
        {
            if (!_resolved[slot]) return;
            bool allDone = true;
            for (int v = 0; v < _victimCount[slot]; v++)
            {
                int k = slot * MaxVictims + v;
                if (_victimCued[k]) continue;
                if (_traveled[slot] + 1e-4f < _victimAt[k]) { allDone = false; continue; }
                _victimCued[k] = true;
                HitCueCount++;
                Transform t = _victims[k].TargetTransform;
                Vector3 at = t != null ? t.position : _end[slot];
                SpawnCue(at, HitCueColor, HitCueSeconds, HitCueSize);
            }
            if (allowClear && allDone && _traveled[slot] >= _length[slot]) ClearPending(slot);
        }

        private void ClearPending(int slot)
        {
            _pending[slot] = false;
            _primary[slot] = null;
            for (int v = 0; v < MaxVictims; v++) _victims[slot * MaxVictims + v] = null;
            _victimCount[slot] = 0;
        }

        // 沒有空位就收最舊的一支（視覺與等待一起丟掉）。
        private int FreeSlot()
        {
            int oldest = 0;
            for (int s = 0; s < ArrowPool; s++)
            {
                if (!_flying[s] && !_pending[s]) return s;
                if (_serial[s] < _serial[oldest]) oldest = s;
            }
            ClearPending(oldest);
            _flying[oldest] = false;
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

        private void SpawnCue(Vector3 center, Color color, float life, float size)
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

        public void HideAll()
        {
            for (int s = 0; s < ArrowPool; s++)
            {
                _flying[s] = false;
                ClearPending(s);
                if (_arrowObjects[s] != null) _arrowObjects[s].SetActive(false);
            }
            for (int i = 0; i < CuePool; i++)
            {
                _cueLife[i] = 0f;
                if (_cueObjects[i] != null) _cueObjects[i].SetActive(false);
            }
        }
    }
}
