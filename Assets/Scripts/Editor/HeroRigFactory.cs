using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Vow.Animation;

namespace Vow.EditorTools
{
    // 英雄載體的資產管線（ARCHITECTURE §陸-4）。
    //
    // 目前隨專案附的是 KayKit Character Pack: Adventurers 的 Knight.fbx（CC0，授權檔在同資料夾），可自由換成別的 Humanoid FBX。
    // 首選：Assets/Art/Characters 內的 Humanoid FBX（Mixamo 或其他）。角色 FBX（含蒙皮網格）自建 Humanoid Avatar，
    //       其餘動作 FBX 依檔名關鍵字對應到 Idle/Run/Attack/Dash/Hit 五個切片並共用該 Avatar；
    //       Attack 切片自動掛上 OnAttackHit() 動畫事件，Animator 的 Attack 狀態播放速度自動校正成「事件恰在前搖 0.25s 觸發」。
    // 後備：找不到 FBX 時，產生一具「依 Humanoid 骨骼命名與階層搭建」的方塊佔位骨架與 5 支程式生成的切片，
    //       同樣由 Animator＋動畫事件驅動——狀態機的整條事件鏈不需要等美術資產就能驗證。
    //       放入 FBX 後重跑 VOW/Phase 1/Build Greybox Scene 即自動換用，不需改任何程式碼。
    internal static class HeroRigFactory
    {
        public const string CharactersFolder = "Assets/Art/Characters";
        public const string GeneratedFolder = "Assets/Art/Characters/Generated";

        // Attack 切片的傷害判定幀位置（佔整支動畫的比例）。Mixamo 揮砍類動作的命中點多落在 35%~45%。
        private const float AttackHitNormalizedTime = 0.4f;

        // 指名優先的切片名稱（KayKit Adventurers 的 take 名；其他資產對不上時自動退回關鍵字比對）
        private static readonly string[] IdlePreferred = { "Idle", "Unarmed_Idle" };
        private static readonly string[] RunPreferred = { "Running_A", "Running_B", "Run" };
        private static readonly string[] AttackPreferred = { "1H_Melee_Attack_Slice_Horizontal", "1H_Melee_Attack_Chop", "Unarmed_Melee_Attack_Punch_A" };
        private static readonly string[] DashPreferred = { "Dodge_Forward" };
        private static readonly string[] HitPreferred = { "Hit_A", "Hit_B" };

        private static readonly string[] IdleKeywords = { "idle" };
        private static readonly string[] RunKeywords = { "run", "jog", "sprint" };
        private static readonly string[] AttackKeywords = { "attack", "slash", "punch", "swing", "shoot", "strike" };
        private static readonly string[] DashKeywords = { "dash", "dodge", "roll", "step", "slide" };
        private static readonly string[] HitKeywords = { "hit", "react", "impact", "damage" };

        public struct HeroRig
        {
            public GameObject ModelInstance;   // 已掛好 Animator 與 HeroAnimationDriver，尚未設定父物件
            public bool IsHumanoid;
        }

        public static HeroRig Build(float windupSeconds, Material placeholderMaterial)
        {
            GreyboxAssetFactory.EnsureFolder(CharactersFolder);
            GreyboxAssetFactory.EnsureFolder(GeneratedFolder);

            if (!TryBuildFromHumanoidFbx(windupSeconds, out HeroRig rig, out string reason))
                rig = BuildPlaceholder(windupSeconds, placeholderMaterial, reason);

            if (rig.ModelInstance.GetComponent<HeroAnimationDriver>() == null)
                rig.ModelInstance.AddComponent<HeroAnimationDriver>();
            return rig;
        }

        // ───────────────────────── Humanoid FBX 路線 ─────────────────────────

        // 一個候選切片：來自哪個 FBX、是該 FBX 匯入設定裡的第幾個 clip、叫什麼名字。
        private struct ClipCandidate
        {
            public string ModelPath;
            public int Index;
            public string ClipName;
            public string FileName;
        }

        private static bool TryBuildFromHumanoidFbx(float windupSeconds, out HeroRig rig, out string reason)
        {
            rig = default;

            List<string> modelPaths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { CharactersFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith(GeneratedFolder)) modelPaths.Add(path);
            }
            if (modelPaths.Count == 0)
            {
                reason = CharactersFolder + " 內沒有任何 FBX";
                return false;
            }

            // 1) 角色 FBX：第一個帶蒙皮網格的模型
            string characterPath = null;
            foreach (string path in modelPaths)
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset != null && asset.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                {
                    characterPath = path;
                    break;
                }
            }
            if (characterPath == null)
            {
                reason = "找不到帶蒙皮網格的角色 FBX（Mixamo 下載角色時請勾選 With Skin）";
                return false;
            }

            ModelImporter characterImporter = (ModelImporter)AssetImporter.GetAtPath(characterPath);
            if (characterImporter.animationType != ModelImporterAnimationType.Human)
            {
                characterImporter.animationType = ModelImporterAnimationType.Human;
                characterImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                characterImporter.SaveAndReimport();
            }

            Avatar avatar = FindSubAsset<Avatar>(characterPath);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                reason = "角色 FBX 無法建立有效的 Humanoid Avatar（骨架自動對應失敗）：" + characterPath;
                return false;
            }

            // 2) 動作切片。兩種資產形態都支援：
            //    - 一個 FBX 內含多個 take（KayKit 等）：比對 take 名稱
            //    - 一個動作一個 FBX（Mixamo：clip 一律叫 mixamo.com）：比對檔名
            List<ClipCandidate> candidates = CollectCandidates(modelPaths);
            ClipCandidate? idleC = Pick(candidates, IdlePreferred, IdleKeywords);
            ClipCandidate? runC = Pick(candidates, RunPreferred, RunKeywords);
            ClipCandidate? attackC = Pick(candidates, AttackPreferred, AttackKeywords);
            ClipCandidate? dashC = Pick(candidates, DashPreferred, DashKeywords);
            ClipCandidate? hitC = Pick(candidates, HitPreferred, HitKeywords);

            if (idleC == null || runC == null || attackC == null)
            {
                reason = "缺少必要切片（至少需要名稱或檔名含 idle／run／attack 的動作）";
                return false;
            }

            ConfigureClips(modelPaths, avatar, characterPath, idleC, runC, attackC, dashC, hitC);

            AnimationClip idle = LoadClip(idleC.Value);
            AnimationClip run = LoadClip(runC.Value);
            AnimationClip attack = LoadClip(attackC.Value);
            AnimationClip dash = dashC != null ? LoadClip(dashC.Value) : null;
            AnimationClip hit = hitC != null ? LoadClip(hitC.Value) : null;
            if (idle == null || run == null || attack == null)
            {
                reason = "切片重新匯入後載入失敗";
                return false;
            }
            if (dash == null) dash = run;   // 灰盒容忍：沒有滑步動作時先借用跑步
            if (hit == null) hit = idle;

            float attackSpeed = attack.length * AttackHitNormalizedTime / Mathf.Max(0.01f, windupSeconds);
            RuntimeAnimatorController controller = BuildController(idle, run, attack, dash, hit, attackSpeed);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(characterPath));
            instance.name = "HeroModel";
            Animator animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;   // 位移一律由狀態機與 NavMesh 決定，動畫不得推動角色
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            rig = new HeroRig { ModelInstance = instance, IsHumanoid = true };
            reason = null;
            Debug.Log("[VOW] 已採用 Humanoid FBX：" + characterPath + "（idle=" + idle.name + " run=" + run.name +
                      " attack=" + attack.name + " dash=" + dash.name + " hit=" + hit.name + "）");
            return true;
        }

        private static List<ClipCandidate> CollectCandidates(List<string> modelPaths)
        {
            List<ClipCandidate> result = new List<ClipCandidate>();
            foreach (string path in modelPaths)
            {
                ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(path);
                ModelImporterClipAnimation[] clips = importer.clipAnimations;
                if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
                if (clips == null) continue;

                string fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                for (int i = 0; i < clips.Length; i++)
                    result.Add(new ClipCandidate { ModelPath = path, Index = i, ClipName = clips[i].name, FileName = fileName });
            }
            return result;
        }

        // 優先序：① 指名的切片名稱（完全相符）② 切片名稱含關鍵字（取名稱最短的，避免 "2H_Melee_Idle" 搶走 "Idle"）③ 檔名含關鍵字。
        private static ClipCandidate? Pick(List<ClipCandidate> candidates, string[] preferredNames, string[] keywords)
        {
            foreach (string preferred in preferredNames)
                foreach (ClipCandidate c in candidates)
                    if (string.Equals(c.ClipName, preferred, System.StringComparison.OrdinalIgnoreCase)) return c;

            ClipCandidate? best = null;
            foreach (ClipCandidate c in candidates)
            {
                if (!ContainsAny(c.ClipName.ToLowerInvariant(), keywords)) continue;
                if (best == null || c.ClipName.Length < best.Value.ClipName.Length) best = c;
            }
            if (best != null) return best;

            foreach (ClipCandidate c in candidates)
                if (ContainsAny(c.FileName, keywords)) return c;
            return null;
        }

        // 同一個 FBX 可能同時供應好幾個切片：每個 FBX 只改一次匯入設定、只重新匯入一次。
        private static void ConfigureClips(List<string> modelPaths, Avatar avatar, string characterPath,
            ClipCandidate? idle, ClipCandidate? run, ClipCandidate? attack, ClipCandidate? dash, ClipCandidate? hit)
        {
            foreach (string path in modelPaths)
            {
                ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(path);
                ModelImporterClipAnimation[] clips = importer.clipAnimations;
                if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
                if (clips == null || clips.Length == 0) continue;

                bool touched = false;
                touched |= Configure(clips, path, idle, true, false);
                touched |= Configure(clips, path, run, true, false);
                touched |= Configure(clips, path, attack, false, true);
                touched |= Configure(clips, path, dash, false, false);
                touched |= Configure(clips, path, hit, false, false);
                if (!touched) continue;

                if (importer.animationType != ModelImporterAnimationType.Human)
                {
                    importer.animationType = ModelImporterAnimationType.Human;
                    if (path != characterPath)
                    {
                        // 純動作 FBX 沒有網格，骨架定義沿用角色 FBX 的 Avatar
                        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                        importer.sourceAvatar = avatar;
                    }
                }

                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
        }

        private static bool Configure(ModelImporterClipAnimation[] clips, string path, ClipCandidate? candidate, bool loop, bool addHitEvent)
        {
            if (candidate == null || candidate.Value.ModelPath != path) return false;

            ModelImporterClipAnimation clip = clips[candidate.Value.Index];
            clip.loopTime = loop;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;   // 原地動畫：位移交給程式
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;

            if (addHitEvent)
            {
                // ModelImporter 的事件時間是 0~1 的正規化時間
                clip.events = new[]
                {
                    new AnimationEvent { functionName = HeroAnimatorContract.AttackHitEvent, time = AttackHitNormalizedTime }
                };
            }

            clips[candidate.Value.Index] = clip;
            return true;
        }

        private static AnimationClip LoadClip(ClipCandidate candidate)
        {
            AnimationClip fallback = null;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(candidate.ModelPath))
            {
                AnimationClip clip = asset as AnimationClip;
                if (clip == null || clip.name.StartsWith("__preview__")) continue;
                if (clip.name == candidate.ClipName) return clip;
                if (fallback == null) fallback = clip;
            }
            return fallback;
        }

        private static T FindSubAsset<T>(string path) where T : Object
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is T typed && !asset.name.StartsWith("__preview__")) return typed;
            }
            return null;
        }

        private static bool ContainsAny(string text, string[] keywords)
        {
            foreach (string keyword in keywords)
                if (text.Contains(keyword)) return true;
            return false;
        }

        // ───────────────────────── Animator Controller ─────────────────────────

        private static RuntimeAnimatorController BuildController(
            AnimationClip idle, AnimationClip run, AnimationClip attack, AnimationClip dash, AnimationClip hit, float attackSpeed)
        {
            string path = GeneratedFolder + "/HeroGreybox.controller";
            AssetDatabase.DeleteAsset(path);

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            AnimatorStateMachine root = controller.layers[0].stateMachine;

            // 狀態之間不建任何 Transition：所有切換由 HeroAnimationDriver 依狀態機直接 CrossFade／Play，
            // 動畫圖不含邏輯，「現在是什麼狀態」只有 IPlayerStateMachine 一個事實來源。
            AnimatorState idleState = root.AddState(HeroAnimatorContract.IdleState);
            idleState.motion = idle;
            root.defaultState = idleState;

            root.AddState(HeroAnimatorContract.RunState).motion = run;

            AnimatorState attackState = root.AddState(HeroAnimatorContract.AttackState);
            attackState.motion = attack;
            attackState.speed = attackSpeed;

            root.AddState(HeroAnimatorContract.DashState).motion = dash;
            root.AddState(HeroAnimatorContract.HitState).motion = hit;

            EditorUtility.SetDirty(controller);
            return controller;
        }

        // ───────────────────────── 佔位骨架路線 ─────────────────────────

        private static HeroRig BuildPlaceholder(float windupSeconds, Material material, string reason)
        {
            Debug.LogWarning("[VOW] 未採用 Humanoid FBX（" + reason + "）→ 改用程式生成的佔位骨架。" +
                             "請將 Mixamo Y-Bot／X-Bot（With Skin）與檔名含 idle／run／attack／dash／hit 的動作 FBX 放進 " +
                             CharactersFolder + " 後重新執行本選單。");

            GameObject model = new GameObject("HeroModel");
            Transform hips = Bone(model.transform, "Hips", new Vector3(0f, 0.95f, 0f), new Vector3(0.42f, 0.24f, 0.26f), material);
            Transform spine = Bone(hips, "Spine", new Vector3(0f, 0.22f, 0f), new Vector3(0.38f, 0.26f, 0.24f), material);
            Transform chest = Bone(spine, "Chest", new Vector3(0f, 0.26f, 0f), new Vector3(0.5f, 0.3f, 0.28f), material);
            Bone(chest, "Head", new Vector3(0f, 0.34f, 0f), new Vector3(0.26f, 0.28f, 0.26f), material);

            Transform leftArm = Limb(chest, "LeftUpperArm", new Vector3(-0.33f, 0.08f, 0f), 0.3f, material);
            Limb(leftArm, "LeftLowerArm", new Vector3(0f, -0.3f, 0f), 0.28f, material);
            Transform rightArm = Limb(chest, "RightUpperArm", new Vector3(0.33f, 0.08f, 0f), 0.3f, material);
            Transform rightFore = Limb(rightArm, "RightLowerArm", new Vector3(0f, -0.3f, 0f), 0.28f, material);
            Bone(rightFore, "Weapon", new Vector3(0f, -0.62f, 0f), new Vector3(0.06f, 0.7f, 0.06f), material); // 讓揮砍方向看得出來

            Transform leftLeg = Limb(hips, "LeftUpperLeg", new Vector3(-0.13f, -0.12f, 0f), 0.42f, material);
            Limb(leftLeg, "LeftLowerLeg", new Vector3(0f, -0.42f, 0f), 0.42f, material);
            Transform rightLeg = Limb(hips, "RightUpperLeg", new Vector3(0.13f, -0.12f, 0f), 0.42f, material);
            Limb(rightLeg, "RightLowerLeg", new Vector3(0f, -0.42f, 0f), 0.42f, material);

            const string chestPath = "Hips/Spine/Chest";
            const string rightArmPath = chestPath + "/RightUpperArm";
            const string leftArmPath = chestPath + "/LeftUpperArm";

            // Idle：胸口緩慢起伏
            AnimationClip idle = NewClip("Placeholder_Idle", true);
            Curve(idle, chestPath, "x", 0f, 0f, 1f, 3f, 2f, 0f);

            // Run：四肢交替擺動
            AnimationClip run = NewClip("Placeholder_Run", true);
            Curve(run, "Hips/LeftUpperLeg", "x", 0f, -35f, 0.2f, 35f, 0.4f, -35f);
            Curve(run, "Hips/RightUpperLeg", "x", 0f, 35f, 0.2f, -35f, 0.4f, 35f);
            Curve(run, leftArmPath, "x", 0f, 30f, 0.2f, -30f, 0.4f, 30f);
            Curve(run, rightArmPath, "x", 0f, -30f, 0.2f, 30f, 0.4f, -30f);
            Curve(run, "Hips", "x", 0f, 8f, 0.4f, 8f);

            // Attack：舉刀 → 在 windupSeconds 劈到底（＝傷害判定幀）→ 收刀。切片以 1 倍速播放時事件恰在前搖結束那一刻。
            float hitTime = windupSeconds;
            float total = hitTime + 0.37f;   // 0.22s 目押窗口 + 0.15s 收招
            AnimationClip attack = NewClip("Placeholder_Attack", false);
            Curve(attack, rightArmPath, "x", 0f, 0f, hitTime * 0.55f, -150f, hitTime, 35f, total, 0f);
            Curve(attack, chestPath, "y", 0f, 0f, hitTime * 0.55f, 25f, hitTime, -20f, total, 0f);
            AnimationUtility.SetAnimationEvents(attack, new[]
            {
                new AnimationEvent { functionName = HeroAnimatorContract.AttackHitEvent, time = hitTime } // AnimationClip 的事件時間單位是秒
            });

            if (attack.length < hitTime)
                Debug.LogError("[VOW] 佔位 Attack 切片長度 " + attack.length + "s 短於判定幀時間 " + hitTime +
                               "s，OnAttackHit 事件不會觸發。");

            // Dash：身體前傾
            AnimationClip dash = NewClip("Placeholder_Dash", false);
            Curve(dash, "Hips", "x", 0f, 0f, 0.04f, 28f, 0.16f, 0f);

            // Hit：上身後仰
            AnimationClip hit = NewClip("Placeholder_Hit", false);
            Curve(hit, chestPath, "x", 0f, 0f, 0.06f, -22f, 0.3f, 0f);

            foreach (AnimationClip clip in new[] { idle, run, attack, dash, hit })
            {
                AssetDatabase.CreateAsset(clip, GeneratedFolder + "/" + clip.name + ".anim");
                VerifyBindingsResolve(clip, model);
            }

            Animator animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = BuildController(idle, run, attack, dash, hit, 1f);
            animator.applyRootMotion = false;
            // FBX 預製物的 Animator 預設是 CullUpdateTransforms（渲染器不可見就不更新骨頭，但動畫事件照發）。
            // 玩家英雄永遠在畫面內、而且遊戲邏輯依賴它的動畫事件，一律 AlwaysAnimate，行為才不會隨「看不看得見」改變。
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            return new HeroRig { ModelInstance = model, IsHumanoid = false };
        }

        // 自檢：對每一條曲線綁定實際向骨架取值。路徑或屬性名寫錯時 SetCurve 不會報錯、動畫只是靜靜地不動，
        // 而 clip.length 依然正常——所以只看長度抓不到這種錯，必須逐條確認綁定解析得到東西。
        private static void VerifyBindingsResolve(AnimationClip clip, GameObject rigRoot)
        {
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings.Length == 0)
            {
                Debug.LogError("[VOW] 佔位切片 " + clip.name + " 沒有任何曲線。");
                return;
            }

            foreach (EditorCurveBinding binding in bindings)
            {
                if (!AnimationUtility.GetFloatValue(rigRoot, binding, out float _))
                    Debug.LogError("[VOW] 佔位切片 " + clip.name + " 的曲線綁定解析失敗：" +
                                   binding.path + " → " + binding.propertyName + "（骨架上找不到該路徑或屬性，這條曲線不會生效）");
            }
        }

        private static Transform Bone(Transform parent, string name, Vector3 localPosition, Vector3 visualSize, Material material)
        {
            Transform bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = localPosition;
            AddVisual(bone, Vector3.zero, visualSize, material);
            return bone;
        }

        // 肢體：關節在上端，方塊往下延伸，旋轉關節時整段肢體跟著擺
        private static Transform Limb(Transform parent, string name, Vector3 localPosition, float length, Material material)
        {
            Transform bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = localPosition;
            AddVisual(bone, new Vector3(0f, -length * 0.5f, 0f), new Vector3(0.13f, length, 0.13f), material);
            return bone;
        }

        private static void AddVisual(Transform bone, Vector3 offset, Vector3 size, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = bone.name + "_Mesh";
            Object.DestroyImmediate(cube.GetComponent<Collider>()); // 碰撞只由英雄根物件上的 CapsuleCollider 負責
            cube.transform.SetParent(bone, false);
            cube.transform.localPosition = offset;
            cube.transform.localScale = size;
            if (material != null) cube.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static AnimationClip NewClip(string name, bool loop)
        {
            AnimationClip clip = new AnimationClip { name = name, frameRate = 60f };
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        // 參數為 (時間, 角度) 成對列出；axis 為 "x"／"y"／"z"。
        //
        // 寫成四元數曲線 localRotation.x/y/z/w，而不是歐拉角：四元數是 Transform 旋轉一定可綁定的屬性，
        // 歐拉角的綁定名稱在不同 API 入口的行為不一致，寫錯時不會報錯、只是動畫不動。
        // 做法：先用角度曲線取得平滑插值，再以 60fps 取樣轉成四元數關鍵幀。
        private static void Curve(AnimationClip clip, string path, string axis, params float[] timeValuePairs)
        {
            Keyframe[] angleKeys = new Keyframe[timeValuePairs.Length / 2];
            for (int i = 0; i < angleKeys.Length; i++)
                angleKeys[i] = new Keyframe(timeValuePairs[i * 2], timeValuePairs[i * 2 + 1]);
            AnimationCurve angle = new AnimationCurve(angleKeys);

            float endTime = timeValuePairs[timeValuePairs.Length - 2];
            int samples = Mathf.Max(2, Mathf.CeilToInt(endTime * 60f) + 1);

            AnimationCurve qx = new AnimationCurve(), qy = new AnimationCurve(), qz = new AnimationCurve(), qw = new AnimationCurve();
            for (int i = 0; i < samples; i++)
            {
                float t = endTime * i / (samples - 1);
                float degrees = angle.Evaluate(t);
                Quaternion q = axis == "x" ? Quaternion.Euler(degrees, 0f, 0f)
                             : axis == "y" ? Quaternion.Euler(0f, degrees, 0f)
                             : Quaternion.Euler(0f, 0f, degrees);
                qx.AddKey(t, q.x);
                qy.AddKey(t, q.y);
                qz.AddKey(t, q.z);
                qw.AddKey(t, q.w);
            }

            clip.SetCurve(path, typeof(Transform), "localRotation.x", qx);
            clip.SetCurve(path, typeof(Transform), "localRotation.y", qy);
            clip.SetCurve(path, typeof(Transform), "localRotation.z", qz);
            clip.SetCurve(path, typeof(Transform), "localRotation.w", qw);
            clip.EnsureQuaternionContinuity();
        }
    }
}
