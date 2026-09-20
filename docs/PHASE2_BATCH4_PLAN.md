# 計畫：Phase 2 批 4 —— 三大元素反應（泥濘流沙／蒸氣迷霧／擴散火浪）（2026-09-20）

> 已知良好狀態：main `566a81f`（v0.5.0，`origin/gh-pages 766d7e5`；純邏輯 155＋1 略過、突變 74/74、EditMode 152＋4 略過、PlayMode 66/66）。工作分支 `phase2-batch4`。
> 規格依據：`GDD.md:114-132`（四大基石元素與三大反應、視覺預警鐵律、打擊反饋金字塔）、`GDD.md:245`（路線圖）、`GDD.md:301-303`（漏洞九圍欄：流沙 1.2s、受擊顯影、霧內引導減半）、`ARCHITECTURE.md:98-119`（`ICombatTarget`／`Faction`／`DamageType` 逐字契約，**不得改**）、`ARCHITECTURE.md:199-230`（`ISkillTelegraphService`／`TelegraphShape` 逐字契約，**不得改、不得加列舉值**）、`ARCHITECTURE.md:17,187-193`（紅線：零 carving、主線程零 GC、不得動 `Time.timeScale`）。
> 本檔 §5 為凍結驗收條件；要動它只能走 `02 §2.1`（先寫明「原標準錯在哪、為什麼現在才知道」，再取得使用者針對該條的明確同意）。

## 0. 使用者裁定（2026-09-20，需求對齊一輪，五題全「按照建議」）

1. **岩＝既有石牆**：符印牆或 ENEMY WALL 鈕生的牆，`Activate` 落點在水域內 → 觸發泥濘流沙。不新增岩系技能。
2. **水／火／風＝HUD 三顆除錯鈕** `WATER`／`FIRE`／`WIND`：比照 ENEMY WALL 施放在英雄前方 4m；風以英雄為頂點朝面向放扇形。不做正式拖曳技能鈕（留給英雄設計批次）。代價已告知：不能自由瞄準，靠走位對準。
3. **作用對象與陣營**：HUD 加 `ELEM: BLUE／RED` 切換鈕，決定**下一發**技能的陣營。傷害只打木樁與石牆（`ICombatTarget`）；英雄血量留後面批次。
4. **蒸氣迷霧**：白霧視覺；霧內目標不能被點選鎖定，受傷後顯影 1.5s（期間可鎖定）；「霧內引導晶塔速度減半」只做純邏輯係數＋測試、**不接線**；「遮斷外部視野」這批只有白霧本身的遮蔽，真正的視野遮斷留 Phase 3。
5. **暫定數值（試玩即改）**：見 §2 tuning 表，逐項與對齊紀錄相同。

### ★裁定 3 的更正（2026-09-20，主對話審 GDD 原文後發現，已回報使用者）★

`GDD.md:121-123` 的「友軍／敵方」是**從被困者的角度**講的：

- 火的陣營 **≠** 流沙陣營（＝被困者的友軍放火）→ **救援**：烘乾、立即解除禁錮、流沙終止、**不造成傷害**。
- 火的陣營 **＝** 流沙陣營（＝被困者的敵人放火）→ **爆沸**：對流沙內與流沙陣營敵對的目標造成 AOE 80、流沙終止。

所以正確的兩個試玩情境是：

- 紅水＋紅牆＝紅流沙困住英雄（藍）→ **藍火＝救援**。
- 藍水＋藍牆＝藍流沙罩住木樁（紅）→ **藍火＝爆沸打木樁 80**；**紅火在這裡是救援木樁**。
- 水域與石牆陣營不同時：流沙陣營＝**石牆（完成 Combo 的那一方）**的陣營（§4-2 假設）。

### 主對話自決（已告知使用者、未反對）

結尾推 v0.6.0，依常設授權直接部署＋Playwright 線上實測；計畫仿批 3、驗收條件動手前凍結；對抗覆審上限 3 輪；舊債（R3-1 另一半、單跑零配置 48 bytes）本批踩不到，繼續掛著；v0.5.0 試玩回饋另行處理，不擋批 4。

## 1. 檔案清單與批次

### 步驟 A：純邏輯（不需 Unity，`verify.sh` 可驗）

- 新增 `Core/Logic/ElementTuning.cs` —— 元素全部數值的單一來源（§2 表）
- 新增 `Core/Logic/ElementGeometry.cs` —— 圓形／扇形的點內判定與「名冊過濾」（固定容量、零配置、不用 Linq）
- 新增 `Core/Logic/ElementZoneField.cs` —— 區域名冊與生命週期（生成／Tick／終止／池滿擠掉）
- 新增 `Core/Logic/ElementReactionLogic.cs` —— 反應判定、優先序、陣營方向（純函式）
- 新增 `Core/Logic/QuicksandStatusLogic.cs` —— 縛足→減速時序（單一單位的狀態機）
- 新增 `Core/Logic/SteamConcealmentLogic.cs` —— 受擊顯影計時、遮蔽判定、晶塔引導係數
- 新增 `Core/Logic/ElementCastCooldowns.cs` —— 三技能各 5s 冷卻＋HUD 標籤索引（零配置字串表）
- 新增 `Assets/Tests/EditMode/ElementGeometryTests.cs`、`ElementZoneFieldTests.cs`、`ElementReactionLogicTests.cs`、`QuicksandStatusLogicTests.cs`、`SteamConcealmentLogicTests.cs`、`ElementTuningTests.cs`
- 修改 `Tools/DotnetCheck/mutation_check.py` —— 追加批 4 突變（**只增不改既有 74 筆**）
- `Tools/DotnetCheck/PureLogic.Tests/PureLogic.Tests.csproj` **預期零改動**：`Core/Logic/**/*.cs`（`:14`）與 `Assets/Tests/EditMode/**/*.cs`（`:20`）都是 glob；`Core/Contracts`／`Input` 是逐檔列舉（`:15-19`）→ **`Core/Logic` 的新檔不得 `using` 任何 Contracts 型別**（`Faction` 定義在含 `using UnityEngine` 的 `ICombatTarget.cs`），純邏輯一律用 `int` 陣營代碼，Unity 端做 `(int)Faction` 轉換（批 3 先例）
- **Checkpoint A**：`bash Tools/DotnetCheck/verify.sh` 全綠（`RESULT: ALL PASS`）＋ `python Tools/DotnetCheck/mutation_check.py` 印 `ALL MUTATIONS CAUGHT`。Unity 端未接線，遊戲行為與 v0.5.0 相同。

### 步驟 B：Unity 端接線

- 新增 `Core/Contracts/IElementFieldQuery.cs`、`Core/Contracts/IConcealable.cs`（**新增介面，既有契約一字不動**；先例＝批 1 `IRuneCastInput`、批 3 `IFactionOwned`）
- 新增 `Combat/ElementField.cs` —— 持有 `ElementZoneField`，做 AOE 結算、區域視覺借還、Combo 反饋、流沙／蒸氣對外查詢；實作 `IElementFieldQuery`
- 新增 `Combat/CombatTargetRoster.cs` —— 可列舉的 `ICombatTarget` 名冊（固定容量 32 陣列），與 `ColliderTargetRegistry` 在 `Phase1Bootstrap` 成對註冊／註銷（理由見 §4-5）
- 新增 `Combat/ElementZoneView.cs` —— 池化的扁圓柱區域視覺（四色 sharedMaterial 切換，執行期零 `CreatePrimitive`）
- 新增 `Combat/Feedback/SectorTelegraph.cs` —— 扇形預警（自有 LineRenderer：兩條邊＋圓弧），**不經 `ISkillTelegraphService`**（理由見 §4-6）
- 修改 `Core/HeroLocomotion.cs` —— 新增 `SetSpeedMultiplier(float)`／`SpeedMultiplier`／`SetMovementLocked(bool)`／`IsMovementLocked`；`Configure`（`:123-132`）記下 `_baseMoveSpeed`；`ApplyDisplacement`（`:384`）在鎖住時回 `Vector3.zero`
- 修改 `Core/HeroController.cs` —— 新增**單一鎖定判準** `CanEngage(ICombatTarget)`，`:107` 與 `:146` 兩個既有 `CanBeTargetedBy` 呼叫點都改走它（收斂，見 §4-4）；`TryBeginCadenceDash`（`:208`）縛足時回 `false`；每幀把流沙狀態推給 `HeroLocomotion`
- 修改 `Combat/CombatTargetBehaviour.cs` —— 實作 `IConcealable`：`ReceiveDamage`（`:86-94`）呼叫 `NotifyDamaged()`，顯影倒數在 `Update` 遞減
- 修改 `Combat/RuneWall.cs` —— `Activate`（`:57-73`）結尾通知 `ElementField`（`_elementField == null` 時完全跳過，保住既有行為）
- 修改 `Combat/Feedback/CombatFeedbackService.cs` —— 只新增唯讀計數 `HitstopCount`（比照既有 `ScreenFlashCount` `:57`），供 PlayMode 活性斷言；`TriggerHitstop` 的 `Clamp(30,60)` 與其餘一字不動
- 修改 `UI/DebugHud.cs` —— 四顆鈕 `WATER`／`FIRE`／`WIND`／`ELEM: BLUE|RED`（委派簽章 `:80-83`、`RegisterUiRegion` `:109-114`、`HandleRegionTapped` `:198-226`、`RecalculateLayout` `:228-259`、`OnGUI` 繪製 `:271-370`）；**`InfoRows`（`:22`）不動、`_modeRect`～`_turretRect` 的座標一像素不動**，新 rect 接在 `_turretRect` 那一列之後
- 修改 `Bootstrap/Phase1Bootstrap.cs` —— 組裝 `ElementField`／`CombatTargetRoster`／`SectorTelegraph`；四顆鈕的委派（比照 `:144-153` 的 `spawnEnemyWall`／`toggleTurret` 模式）；目標註冊處（`:73-74` 一帶）加名冊登記
- 修改 `Editor/VOWPhase1SceneBuilder.cs` —— 預建區域視覺池（8 個）、四顆區域材質、扇形預警載體；反射注入（比照 `:479-480`）。**Editor-only，不受執行期禁 `CreatePrimitive` 限制**
- 修改 `Assets/Tests/PlayMode/ZeroAllocationTests.cs` —— **只加 `Batch4Driver` 與活性斷言**；`UpdateBytes==0`／`LateUpdateBytes==0` 門檻、`Frames` 下限、各段 `deadline`（`:405` `12f`、`:479` `25f`、`:494` `15f`、`:536`／`:547` `20f`）與正向對照 `Probe_DetectsADeliberatePerFrameAllocation`（`:333-352`）**一個數字都不准動**
- 新增 `Assets/Tests/PlayMode/ElementReactionPlayTests.cs`
- **Checkpoint B**：`<U> -batchmode -projectPath <P> -runTests -testPlatform EditMode -testResults edit.xml` 與 `-testPlatform PlayMode -testResults play.xml` 皆全綠（`<U>`＝`Unity.exe` 2022.3.62f1）。

### 步驟 C：上線

- `Assets/Scripts/Core/VowVersion.cs:7` → `"0.6.0"`；`docs/PHASE1_ACCEPTANCE_GUIDE.md` 補 §14
- fresh-context `opus` 對抗審查 → 修 → 三態覆審（上限 3 輪）→ merge main → push → `Tools/deploy-webgl.bat` → Playwright 開 `https://9gf6p4448m-del.github.io/vow3d/`（手機模擬、`hasTouch`、CDP 觸控）實測三個反應 → 截圖 `vow-toolchain/browser-screenshots/v060-*`
- **Checkpoint C**：`git log origin/gh-pages -1` 顯示 v0.6.0、線上首頁版本列＝`v0.6.0`、console 無 error／exception。

## 2. 介面（先寫死再實作；`Core/Logic` 全部零 UnityEngine、執行期零配置、不用 Linq、固定容量陣列）

```csharp
// Core/Logic/ElementTuning.cs —— 數值單一來源（[Serializable]，比照 ProjectileTuning）
public sealed class ElementTuning
{
    // 水
    public float WaterRadius = 3f;              public float WaterDurationSeconds = 6f;
    // 火
    public float FireDirectDamage = 40f;
    public float BurnRadius = 3f;               public float BurnDurationSeconds = 4f;
    public float BurnDamagePerSecond = 20f;
    // 反應區（GDD 明文）
    public float ReactionRadius = 4f;
    public float QuicksandDurationSeconds = 3.5f;   public float SteamDurationSeconds = 3f;
    // 流沙（GDD 圍欄九）
    public float RootDurationSeconds = 1.2f;    public float QuicksandSlowMultiplier = 0.65f;
    // 蒸氣（GDD 圍欄九）
    public float RevealDurationSeconds = 1.5f;  public float SteamChannelSpeedMultiplier = 0.5f;
    // 火浪
    public float FirestormAngleDegrees = 60f;   public float FirestormRangeMeters = 6f;
    public float FirestormDamageMultiplier = 1.5f;  // 40 × 1.5 ＝ 60
    // 爆沸
    public float BoilDamage = 80f;
    // 施放
    public float SkillCooldownSeconds = 5f;     public float CastDistanceMeters = 4f;
    // Combo 反饋（GDD 明文 45ms，非暫定）
    public float ComboHitstopMs = 45f;          public float ComboTrauma = 0.5f;
    // 容量
    public int MaxLiveZones = 6;                public int TargetRosterCapacity = 32;
}
```

**tuning 數值逐項對照裁定 5**：水域 3m／6s；火直傷 40、燃燒區 3m／4s／每秒 20；反應區 4m；流沙 3.5s、蒸氣 3.0s；縛足 1.2s、減速 ×0.65（＝「保留 35% 減速」）；顯影 1.5s；引導 ×0.5；火浪 60°／6m／×1.5（＝60）；爆沸 80；冷卻 5s；施放距離 4m；Hitstop 45ms。**這 20 個數字不得在實作時改動**（§5 V6-d 用 grep 逐一對）。`MaxLiveZones`／`ComboTrauma`／容量為主對話自決（§4-3、§4-8）。

```csharp
// Core/Logic/ElementGeometry.cs
public static class ElementGeometry
{
    // 邊界語意定死：含邊界（<=）。一律平方比較，不開根號。
    public static bool IsInsideCircle(float px, float pz, float cx, float cz, float radius);
    // dir 內部正規化；dir 長度 < 1e-6 → 一律 false。頂點本身（dist == 0）→ true。
    public static bool IsInsideSector(float px, float pz, float apexX, float apexZ,
                                      float dirX, float dirZ, float range, float totalAngleDegrees);
    // 平行陣列輸入；回傳寫進 outIndices 的筆數，索引為升冪；outIndices 滿了就停（回傳＝容量）。
    public static int CollectInsideCircle(float[] xs, float[] zs, int count,
                                          float cx, float cz, float radius, int[] outIndices);
    public static int CollectInsideSector(float[] xs, float[] zs, int count, float apexX, float apexZ,
                                          float dirX, float dirZ, float range, float totalAngleDegrees,
                                          int[] outIndices);
}

// Core/Logic/ElementZoneField.cs
public enum ElementZoneKind { None = 0, Water = 1, Burning = 2, Quicksand = 3, Steam = 4 }

public struct ElementZone
{
    public int Id; public ElementZoneKind Kind; public float X, Z, Radius, RemainingSeconds;
    public int FactionId; public bool Active;
}

public sealed class ElementZoneField
{
    public ElementZoneField(ElementTuning tuning);   // 容量＝tuning.MaxLiveZones，固定容量陣列
    public int Capacity { get; }   public int ActiveCount { get; }
    public bool TryGetBySlot(int slot, out ElementZone zone);
    public bool TryGetById(int id, out ElementZone zone);
    // 回傳新區域 id（單調遞增，不重用）。滿了 → 擠掉「RemainingSeconds 最小」那一格（同值取 slot 小者），再放。
    public int Spawn(ElementZoneKind kind, float x, float z, float radius, float durationSeconds, int factionId);
    public bool Terminate(int id);                    // 手動終止（被反應消耗）；已不存在 → false
    public void Tick(float deltaSeconds);             // RemainingSeconds <= 0 → 該格 Active = false
    // 取「點在其內、指定種類」的區域中圓心最近者；平手取 id 小者。沒有 → -1。
    public int FindNearestContaining(float x, float z, ElementZoneKind kind);
}

// Core/Logic/ElementReactionLogic.cs
public enum ElementCast { Water = 1, Fire = 2, Wind = 3, Rock = 4 }
public enum ElementReaction { None = 0, Quicksand = 1, Steam = 2, Boil = 3, Rescue = 4,
                              Firestorm = 5, PlainFire = 6, WaterPool = 7 }

public struct ReactionOutcome
{
    public ElementReaction Reaction;
    public int ConsumedZoneId;      // 被消耗／終止的區域 id；無 ＝ -1
    public int NewZoneKindCode;     // 要新生的區域（ElementZoneKind），無 ＝ 0
    public float NewZoneRadius, NewZoneDurationSeconds;
    public int NewZoneFactionId;
    public float AoeDamage;         // 對「與 DamageFactionId 敵對」的目標；無傷害 ＝ 0
    public int DamageFactionId;
    public bool IsCombo;            // true → 呼叫端做 Hitstop 45ms ＋ 震屏
}

public static class ElementReactionLogic
{
    // 唯一入口。同一發只會回傳一個反應。
    // 優先序（定死）：流沙 ＞ 水域 ＞ 空地；風只看燃燒區；岩只看水域。
    public static ReactionOutcome Resolve(ElementCast cast, int castFactionId, float x, float z,
                                          float dirX, float dirZ, ElementZoneField field, ElementTuning tuning);
    // 敵對判定（兩個方向都要測）：不同陣營代碼＝敵對。DestructibleWall／Neutral 由 Unity 端沿用
    // ICombatTarget.CanBeTargetedBy 處理，純邏輯層只管「英雄 vs 區域」這一條。
    public static bool IsHostile(int castFactionId, int targetFactionId);
}

// Core/Logic/QuicksandStatusLogic.cs —— 單一單位的縛足／減速狀態機
public sealed class QuicksandStatusLogic
{
    public QuicksandStatusLogic(ElementTuning tuning);
    public bool IsRooted { get; }              // 禁位移中
    public float RootRemainingSeconds { get; }
    public float SpeedMultiplier { get; }      // 在敵對流沙內 ＝ 0.65；否則 1.0
    public int RootedByZoneId { get; }         // 已被哪個流沙縛足過；-1 ＝ 未曾
    // insideZoneId：此刻所在的「敵對流沙」id；不在任何敵對流沙內 ＝ -1
    public void Tick(float deltaSeconds, int insideZoneId);
    public void Reset();
}

// Core/Logic/SteamConcealmentLogic.cs
public sealed class SteamConcealmentLogic
{
    public SteamConcealmentLogic(ElementTuning tuning);
    public bool IsRevealed { get; }   public float RevealRemainingSeconds { get; }
    public void NotifyDamaged();      // 顯影 1.5s；重複受傷 ＝ 刷新回 1.5s（不疊加）
    public void Tick(float deltaSeconds);
    // 三個 bool 的真值表（八種組合全非恆定）
    public static bool IsConcealed(bool targetInsideSteam, bool attackerInsideSameSteam, bool targetRevealed);
    public static float ChannelSpeedMultiplier(bool channellerInsideSteam, ElementTuning tuning);
}

// Core/Logic/ElementCastCooldowns.cs
public sealed class ElementCastCooldowns
{
    public ElementCastCooldowns(ElementTuning tuning);
    public bool TryBeginCast(ElementCast cast, float nowSeconds);        // 冷卻中 → false，不重置冷卻
    public float RemainingSeconds(ElementCast cast, float nowSeconds);
    public int RemainingLabelIndex(ElementCast cast, float nowSeconds);  // 0 ＝ 可用；1..5 ＝ 向上取整的剩餘秒
}

// Core/Contracts/IElementFieldQuery.cs（新增介面）
public interface IElementFieldQuery
{
    bool IsConcealedFrom(Vector3 targetPosition, bool targetRevealed, Vector3 attackerPosition);
}
// Core/Contracts/IConcealable.cs（新增介面）
public interface IConcealable { bool IsRevealed { get; } }
```

**鎖定判準的收斂（先數分母再收斂）**：`grep -rn "CanBeTargetedBy" Assets/ --include=*.cs` ＝ 4 命中，其中生產呼叫點 **N＝2**：`HeroController.cs:107`（點擊當下）與 `HeroController.cs:146`（`IsTargetValid` 的持續驗證），另兩處是契約宣告（`ICombatTarget.cs:13`）與實作（`CombatTargetBehaviour.cs:80`）。兩個呼叫點**全部**改走新的

```csharp
public bool CanEngage(ICombatTarget target)   // HeroController 內唯一的「這個目標打不打得到」
{
    if (target == null || !target.CanBeTargetedBy(_faction)) return false;
    if (_elementField == null || target.TargetTransform == null) return true;
    bool revealed = target is IConcealable c && c.IsRevealed;
    return !_elementField.IsConcealedFrom(target.TargetTransform.position, revealed, transform.position);
}
```

分母歸一，涵蓋 2/2。`ICombatTarget.CanBeTargetedBy(Faction)` **簽章一字不動**（它拿不到攻擊者座標，蒸氣規則②需要）。

**位移防線的收斂（先數分母再收斂）**：`grep -rn "ApplyDisplacement\|EjectFromBox" Assets/Scripts --include=*.cs` → 會真的移動英雄的入口 **N＝3**：`HeroLocomotion.cs:253`（走路，Step 內）、`MicroCadenceMover.cs:70`（滑步）、`Phase1Bootstrap.cs:258` → `HeroLocomotion.EjectFromBox`（`:360`，被牆壓住時推出）。前兩者**都經過 `HeroLocomotion.ApplyDisplacement`（`:384`）**→ 防線寫在 `ApplyDisplacement`，涵蓋 2/3。**第 3 個 `EjectFromBox` 刻意不涵蓋**：那是「英雄與牆重疊時推出去」的安全網，擋掉它會讓在流沙裡被牆壓到的英雄永久卡在牆體內。`TryBeginCadenceDash` 縛足時回 `false` 是**附加**（不消耗充能），不是防線本體。

**HUD 版面**：`RecalculateLayout`（`:228-259`）在 `_enemyWallRect`／`_turretRect` 那一列之後續接兩列——
第 1 列三等分：`_waterRect`／`_fireRect`／`_windRect`（寬 `(PanelWidth - Pad*4) / 3`）；第 2 列整寬：`_elemRect`。
標籤零配置：四張預建字串表（`{"WATER","WATER 5",…,"WATER 1"}` 等），索引＝`RemainingLabelIndex`；`ELEM: BLUE`／`ELEM: RED` 兩個常數字串。**不得字串串接**。

## 3. 不做什麼

- 英雄血量、會攻擊英雄的敵人、木樁以外的受害者；流沙／蒸氣對木樁「移動」的影響（木樁不會動）
- 晶塔／佔點系統的接線（裁定 4：引導減半只做純邏輯係數＋測試）
- 真正的視野遮斷、Dithered Occlusion、粒子／著色器／正式美術（冷庫協議）——區域視覺一律是灰盒半透明圓柱
- 正式的水／火／風英雄技能與拖曳瞄準（裁定 2：只做除錯鈕）
- **不改 `ARCHITECTURE.md`／`GDD.md`**；**不改 `Core/Contracts` 下任何既有介面**（含 `TelegraphShape` 不得加列舉值）；**不新增或修改任何 asmdef**
- 不動 `HeroCombatBrain`／`PlayerStateMachine`／`CadenceSim`／`RuneGestureTracker`／`RuneCastLogic`／`RuneWallLogic`／`ProjectileFlightLogic`／`RockShieldLogic`／`TapPickLogic` 任何一行
- 不動 `SkillTelegraphService.cs`（扇形另開元件，§4-6）；不新增自訂圖層、不動 `TagManager.asset`、不動 Physics 碰撞矩陣
- 不動既有 74 筆突變、`verify.sh` 的紅線 grep 期望數、`ZeroAllocationTests` 的門檻與正向對照
- 不用 `Physics.OverlapSphereNonAlloc`／`OverlapBox`（§4-5）；不動 `Time.timeScale`（紅線）

## 4. 假設與取捨（自行採用、可推翻，會同步寫進驗收指南 §14）

**1. 反應優先序定死：流沙 ＞ 水域 ＞ 空地，只觸發一個。** 一發火同時蓋到流沙與水域時只算流沙（爆沸／救援），水域不消耗。同種區域蓋到多個時取**圓心最近**者，平手取 id 小者（決定性）。風只看燃燒區、岩只看水域，其餘區域一律無反應。

**2. 流沙陣營＝石牆的陣營**（alignment ★更正★）。水域與石牆陣營不同時，完成 Combo 的是「砸下去的那面牆」，所以流沙歸牆的主人。**反應區的圓心定死＝被消耗那個水域的圓心**（`GDD.md:119`／`:125`：是「水域」凝結／沸騰成反應區，不是以牆或火的落點為圓心）。**蒸氣不分陣營**（GDD 只說「遮斷外部視野」，沒說只遮敵方）——蒸氣區的 `FactionId` 一律記 `Neutral` 的代碼，遮蔽判定完全不看它。

**3. 同時存活上限 6 個區域、池 8 個物件**（8 > 6，批 3 r1 CRITICAL-1 的教訓：池物件數等於上限時，「擠掉最舊」永遠走不到，玩家看到的是按鈕沒反應）。池滿時擠掉「剩餘時間最短」那一格（不是最舊——剩餘時間最短的那個本來就最快消失，觀感干擾最小），同剩餘時間取 slot 小者。上限 6 的來由：三顆鈕各 5s 冷卻、最長區域壽命 6s → 單人最多同時 2 個水 ＋ 1 燃燒 ＋ 1 流沙 ＋ 1 蒸氣 ＝ 5，留 1 格餘裕。【試玩必調】

**4. 縛足語意**：進入（或成形時已在內）起算 1.2s 禁位移（不能走、不能滑步）；**可以普攻**（`GDD.md:120` 字面＝「禁位移（縛足）」，不含禁攻擊，`CanAttack` 與大腦一行不動）。1.2s 後只要還在區內就是移速 ×0.65；離開或流沙終止即恢復。**每個流沙對同一單位只縛足一次**（走出去再走回來只有減速，靠 `RootedByZoneId` 比對）；**換一個流沙會重新縛足**。

**5. AOE 命中判定走「已登記名冊」的純邏輯幾何，不用 `Physics.Overlap*`。** 先數分母：`grep -rnE 'OverlapSphere|OverlapBox|OverlapCapsule' Assets/` 全專案 **0 命中**——沒有既有慣例可循。否決 `OverlapSphereNonAlloc` 的三個理由：(a) 它量的是「碰撞體有沒有碰到球」，而規格與純邏輯測試講的是「**圓心距離**」，最近反例（圓外 0.1m）的鑑別力會被木樁 CapsuleCollider 的半徑整個吃掉；(b) 要挑 LayerMask，而層 2 目前同時住著英雄本體、邊界牆、GRID 疊圖、血條四邊形與技能預警（批 3 §4-1 已數過的分母），一個 mask 寫錯就打到不該打的東西；(c) 幾何在純邏輯層才測得到、突變才打得到（`mutation_check.py` 碰不到 `Assets/Scripts/Combat/`）。
**既有 `ColliderTargetRegistry` 不能用**（`ColliderTargetRegistry.cs:10` 是 `Dictionary<int, ICombatTarget>`，鍵是 collider instance id）：它不可列舉，而且一個目標有多個 Collider 時會出現在字典多次，AOE 會重複結算。所以另開 `CombatTargetRoster`（固定容量 32 陣列，去重），與 `ColliderTargetRegistry.Register/Unregister` 在 `Phase1Bootstrap` **成對**呼叫。

**6. 扇形預警另開元件，不經 `ISkillTelegraphService`。** `TelegraphShape`（`ISkillTelegraphService.cs:5-9`）是 `ARCHITECTURE.md:206-210` 的逐字契約，不得加 `SectorCast`。否決「借用既有 Line＋自繪兩條邊」的理由：`SkillTelegraphService` 只有三條 LineRenderer（`Awake` `:35-41` 建）、唯一呼叫者是 `CadenceAimPreview`，共用 `ActiveShape`／`_snapRemaining` 狀態；要同時畫兩條邊＋圓弧得加第 4、5 條線並改 `ShowLineIndicator` 的語意，等於改一個已被 Phase 1 驗收過的元件。新開 `SectorTelegraph` 只加不改，風險小得多，而且 `SkillTelegraphService.cs` 可以列進 §5 V6 的零改動清單。

**7. 元素傷害的敵對判定**：對 `ICombatTarget` 沿用 `CanBeTargetedBy(castFaction)`（石牆與中立皆可打、其餘不得同陣營）；對英雄用 `HeroFaction != zoneFaction` 直接比（英雄不是 `ICombatTarget`）。**元素 AOE／DoT 一律跳過「`IFactionOwned.OwnerFaction` ＝施放陣營」的石牆**（自家牆不吃自家元素傷害；`GDD.md:300` 陣營歸屬校驗的同一精神）。傷害型別一律 `DamageType.Elemental`（`ICombatTarget.cs:25-30` 既有列舉，批 3 之前無人使用）。

**8. Combo 反饋只掛三個反應**：流沙成形、蒸氣成形、火浪（`GDD.md:132`「元素 Combo 觸發 45ms 打擊頓挫與創傷震屏」）。**救援與爆沸不另觸發**——它們是流沙的破除鏈，不是新的 Combo。`ComboTrauma = 0.5f` 介於 `BasicAttackTrauma 0.2`（`HeroTuningAsset.cs:22`）與 `WallBreakTrauma 0.8`（`:23`）之間。【試玩必調】
**45ms 的陷阱**：`CombatFeedbackService.TriggerHitstop`（`:85-87`）會 `Clamp(30, 60)`。若有人把常數打成 `4.5` 或 `450`，Clamp 會**靜默**改成 30／60，所有「有頓挫」的斷言照樣綠 → 所以 §5 另有一條純邏輯測試只守「45 嚴格落在 (30, 60) 開區間」。

**9. 火的落點細則**：落在**既有燃燒區**內 → 視同空地，但**不新增第二個燃燒區**，改為把該燃燒區剩餘時間刷新回 4s（避免同位置疊區把 DPS 變成 40）。落在**蒸氣**內 → 視同空地，蒸氣不消耗。落在水域 → 蒸氣、**不留燃燒區**。落在流沙 → 爆沸或救援、**不留燃燒區**。

**10. 「掃到」的定義定死＝燃燒區的圓心落在扇形內**（`IsInsideSector`，不是「扇形與燃燒圓有任何交疊」）；火浪的傷害對象＝落在扇形內的目標。**風沒掃到燃燒區**：只顯示扇形預警與輕推視覺，**不造成任何傷害、不生任何區域**（裁定沒說要傷害；正式技能設計留英雄批次）。【待試玩推翻】

**11. 施放點**：水／火＝英雄前方 4m 的地面點（沿 `transform.forward`，y 取英雄腳下高度，比照 `RuneCaster` 的 `QuickCastDistance` 用法）；風＝**以英雄本體為扇形頂點**、朝 `transform.forward`。石牆的落點沿用 `RuneWall.Activate` 的 `position`。

**12. 冷卻與陣營切換**：三顆技能鈕各自獨立 5s 冷卻；冷卻中按下＝**沒有任何事發生**（不重置冷卻、不扣任何東西）。`ELEM` 鈕無冷卻，切換只影響**之後**施放的技能，已成形的區域陣營不變。

Simplicity 例外：

| 違反了什麼 | 為何必要 | 更簡單的方案為何被否決 |
|---|---|---|
| 晶塔引導係數只有純邏輯、無生產呼叫點 | 裁定 4 明指「只做純邏輯係數＋測試、不接線」；`GDD.md:126` 的圍欄要有地方守 | 完全不寫＝後面批次容易漏掉圍欄，而且 premortem 漏洞九的一半會靜靜消失 |
| 另開 `CombatTargetRoster`，與 `ColliderTargetRegistry` 並存 | AOE 要能列舉全場目標；字典鍵是 collider、一目標多 collider 會重複結算 | 把 `ColliderTargetRegistry` 改成可列舉＝批 3 剛驗收過的點擊路徑要重測，且去重邏輯仍得另寫 |
| 新增四顆除錯鈕 | 不變量掃描（§5 V0）：沒有它們，三個反應在遊戲內零鑑別力，`02 §6.1` 第 5 條（量測位置）不過 | 只寫純邏輯測試＝線上版驗不到，使用者看不到任何東西 |
| `HeroLocomotion` 新增 4 個公開成員 | 移速倍率與禁位移**沒有任何既有入口**（`Configure` `:123-132` 只在 `HeroController.Awake` `:47` 設一次 `_agent.speed` `:126`，之後不再讀寫） | 每幀呼叫 `Configure` 重設速度＝會連帶重設 `acceleration`／`angularSpeed`／`radius`，是比新增入口更大的行為面 |

## 5. 端到端驗收條件（凍結）

每條括號＝「什麼樣的壞實作會讓這條變紅」。數值一律讀 `ElementTuning`，不得在測試裡寫死寬鬆常數。

**容差的來由（全域，不得逐條放寬）**
- **位置容差 0.10m**＝英雄 `MoveSpeed 5.5 m/s`（`HeroTuningAsset.cs:13`）在一個 60fps tick 的位移 `5.5/60 = 0.0917m` 向上取整。PlayMode 一律 `Time.captureDeltaTime = 1/60f`。
- **時間容差 0.034s**＝兩個 60fps tick（涵蓋「判定落在幀首或幀尾」）。
- **純邏輯時間／幾何容差 1e-4**（固定 dt、無幀邊界）。
- **傷害容差 0.01**（單次結算，浮點誤差）；**DoT 總量容差 0.5**＝一個 tick 的燃燒量 `20 × (1/60) = 0.334` 向上取整。

### V0 不變量掃描（動手前 30 秒，寫進實作 PR 的第一則）

- `IsConcealed(a,b,c) = a && !b && !c` —— 三個 bool 八種組合，非恆真非恆假。✅
- `IsInsideCircle` 用 `<=`：`radius > 0` 時圓心一定 true、`radius + ε` 一定 false，非恆定。✅
- `IsInsideSector` 的 `totalAngle = 60°` → 半角 30°，`cos30° ≈ 0.866`，落在 (0,1) 開區間 → 判斷式有兩側。若寫成 `totalAngleDegrees >= 360` 則**恆真**（退化成圓），所以 §5 V2-b 要求 1° 內外各一個樣本。✅
- `RootRemainingSeconds > 0` 在 `RootDurationSeconds = 1.2 > 0` 下，縛足當幀一定成立 → 可當判斷式。✅
- `SpeedMultiplier != 1f` **在沒有流沙時恆假** → 「沒有元素區域時行為逐值不變」那組斷言的鑑別力**不在**這一條，而在 V4-a 的「有流沙時真的變成 0.65」。V4-a 與 V4-l 必須成對存在。✅
- 「風沒掃到燃燒區 → 木樁血量不變」**單獨看是恆真的**（一個什麼都不做的實作也過）→ V2-h **必須**在同一個測試裡先證明「同一個扇形、只是多放一個燃燒區，就會造成 60 傷害」（活性），再驗反例。❗
- 除錯鈕是**驗收設施**：沒有它們，三個反應判定式在遊戲內零鑑別力（§4 Simplicity 表第 3 列）。

### V1 純邏輯門檻

- **V1-a** `bash Tools/DotnetCheck/verify.sh` 印 `RESULT: ALL PASS`：既有純邏輯測試 **155 通過＋1 略過**一個不少、0 失敗；compile-only 0 error。（`Core/Logic` 新檔用了 UnityEngine／Linq；Contracts 型別漏進純邏輯專案）
- **V1-b** `verify.sh` 第 3 段紅線掃描 8 條期望數**與 v0.5.0 逐值相同**（`Application.targetFrameRate` 與 `InputSystem.pollingFrequency` 各 1、其餘 0，含 `timeScale` 0）。（新元件為了做頓挫去動 `Time.timeScale`＝ARCHITECTURE 紅線）

### V2 純邏輯新測試（全綠；括號內為紅燈條件）

- **V2-a `IsInsideCircle_IncludesTheBoundary_AndExcludesAPointJustOutside`**：圓心 (2.3, −1.7)、r＝3 → 距圓心 3.000 的點為 true；3.100 為 false；圓心 true。**刻意避開軸對齊**：取方位角 23.5°（批 2 R9 教訓，不用 0°／45°／90°）。（把 `<=` 寫成 `<`；把距離比較寫成直接比 dx 或 dz）
- **V2-b `IsInsideSector_AcceptsOneDegreeInside_AndRejectsOneDegreeOutside`**：頂點 (0.4, 0.9)、方向指向 37°、`range=6`、`totalAngle=60` → 偏 29° 的點（距頂點 4.0m）true；偏 31° false；兩側各測一次（＋29/−29 true、＋31/−31 false）。（忽略角度只看距離＝恆真；半角寫成全角）
- **V2-c `IsInsideSector_AcceptsAtRange_AndRejectsZeroPointOneMetreBeyond`**：同扇形、偏 10°，距離 6.000 true、6.100 false。（`range` 沒用上；用了 `range*range` 卻比未平方的距離）
- **V2-d `IsInsideSector_WithAZeroLengthDirection_ReturnsFalse`**：`dir=(0,0)` → 一律 false（不得回 true、不得 NaN）。（正規化沒防除以零）
- **V2-e `CollectInsideCircle_ReturnsIndicesInAscendingOrder_AndStopsAtBufferCapacity`**：8 個點、6 個在圓內、`outIndices` 長度 4 → 回 4、內容為前 4 個在圓內者的索引且升冪。（不檢查容量＝陣列越界；順序不決定性）
- **V2-f `Rock_LandingInsideWater_MakesQuicksand_AndConsumesTheWater`**：水域中心 (5,5)、r=3、陣營 Red；岩落 (7.1, 6.4)（距圓心 2.55 < 3）→ `Reaction==Quicksand`、`ConsumedZoneId`＝該水域、新區 `Quicksand` r=4／3.5s、`IsCombo==true`。（岩不看水域＝永遠 None；水域沒被消耗＝可以無限刷流沙）
- **V2-g `Rock_LandingZeroPointOneMetreOutsideWater_MakesNothing`**：同水域，岩落在距圓心 3.100 處 → `Reaction==None`、水域仍在、`ActiveCount` 不變。（半徑判定用了反應半徑 4 而不是水域半徑 3；`<=` 寫成 `<=+ε`）
- **V2-h `Wind_SweepingABurningZone_MakesAFirestorm_AndConsumesTheBurningZone` ＋ 反例（同一測試內）**：英雄在 (0,0) 朝 37°；燃燒區中心在該方向 4.5m、偏 12° → `Reaction==Firestorm`、`AoeDamage == 40×1.5 == 60`（容差 0.01）、燃燒區被消耗、`IsCombo==true`（**活性**）。接著同一個扇形、把燃燒區移到偏 31°（角外 1°）→ `Reaction==None`、`AoeDamage==0`、燃燒區仍在。（沒掃到也給傷害；扇形忽略角度；倍率寫死 1.0）
- **V2-i `Fire_HittingWater_MakesSteam_AndLeavesNoBurningZone`**：火落水域內 → `Steam` r=4／3.0s、水域被消耗、**新區只有一個且不是 Burning**、`AoeDamage==0`、`IsCombo==true`。（火照樣留燃燒區＝霧裡永遠在燒；水域沒被消耗）
- **V2-j `Fire_HittingHostileQuicksand_Boils_AndTerminatesTheQuicksand`**（陣營方向 B：火陣營 **＝** 流沙陣營）：流沙陣營 1、火陣營 1 → `Reaction==Boil`、`AoeDamage==80`（容差 0.01）、`DamageFactionId==1`、流沙被終止、`NewZoneKindCode==0`。（陣營比較恆真／恆假；爆沸不終止流沙＝死局）
- **V2-k `Fire_HittingFriendlyQuicksand_Rescues_AndTerminatesTheQuicksand`**（陣營方向 A：火陣營 **≠** 流沙陣營）：流沙陣營 1、火陣營 2 → `Reaction==Rescue`、`AoeDamage==0`、流沙被終止。（把救援寫成爆沸＝救援變成補刀；救援不終止流沙）
- **V2-l `Fire_OnOpenGround_DealsDirectDamage_AndLeavesABurningZone`**：空地 → `PlainFire`、`AoeDamage==40`、新區 `Burning` r=3／4s、`ConsumedZoneId==-1`、`IsCombo==false`。（空地火沒傷害；空地也算 Combo＝頓挫濫發）
- **V2-m `Fire_OverlappingQuicksandAndWater_PicksQuicksandOnly`**：火落點同時在一個流沙與一個水域內 → 只回一個反應且是 `Boil`／`Rescue`，**水域 `Active` 仍為 true**。（優先序倒過來；兩個反應都觸發＝一發火兩次 Combo）
- **V2-n `Fire_OverlappingTwoWaterZones_PicksTheNearestCentre`**：兩個水域都含火落點，圓心距離 1.2m vs 2.4m → 消耗較近那個；把兩個距離設成相等時取 id 小者。（用「第一個找到的」＝順序依賴，slot 重用後結果會漂）
- **V2-o `Quicksand_TakesTheFactionOfTheWall_WhenWaterAndWallDisagree`**：水域陣營 1、岩陣營 2 → 新流沙 `FactionId==2`。（流沙取水域陣營＝★更正★整段失效，紅藍情境全反）
- **V2-p `Spawn_WhenTheFieldIsFull_EvictsTheZoneWithTheLeastTimeLeft`**：填滿 6 格、剩餘時間各為 5.0/1.0/4.0/3.0/2.0/6.0 → 第 7 次 `Spawn` 回傳 **≥0 的新 id**、`ActiveCount==6`、剩餘 1.0 那格 `Active==false`、其餘五格原封不動。（池滿回 −1＝按鈕沒反應，批 3 r1 CRITICAL-1 同型；擠掉最舊而非剩餘最短）
- **V2-q `Tick_ExpiresAWaterZoneExactlyAtSixSeconds`**：dt=0.1 連跑 → t=5.9 仍 `Active`、t=6.0 之後 `Active==false`（容差 1e-4）。（Tick 不遞減；用 `<` 判到期差一幀）
- **V2-r `Tick_ExpiresAQuicksandAtThreePointFive_AndSteamAtThree`**：兩個區域同場 → 3.0s 時蒸氣消失、流沙仍在；3.5s 後流沙也消失。（兩種區域共用同一個壽命常數）
- **V2-s `BurningZone_OverFourSeconds_DealsEightyDamageInTotal`**：對一個固定在燃燒區內的假想目標以 dt=1/60 累加 4.0s → 總傷害 80.0（容差 0.5）；4.0s 之後不再累加。（每秒傷害或壽命被改；到期後還在燒）
- **V2-t `Entering_RootsForOnePointTwoSeconds_ThenSlowsToSixtyFivePercent`**：`Tick(dt, zoneId=7)` → 當幀 `IsRooted==true`、`SpeedMultiplier==0.65`。（進入不縛足；縛足期間速度沒降）
- **V2-u `AtOnePointOneNineSeconds_StillRooted_AtOnePointTwoOne_ItMovesAtSixtyFivePercent`**：累計 1.19s → `IsRooted==true`；累計 1.21s → `IsRooted==false` 且 `SpeedMultiplier==0.65`（容差 1e-4）。（縛足時長被改；縛足結束後速度回 1.0＝35% 減速消失）
- **V2-v `LeavingAndReentering_TheSameQuicksand_DoesNotRootAgain`**：縛足結束 → `Tick(dt,-1)`（`SpeedMultiplier==1`、`IsRooted==false`）→ 再 `Tick(dt, 7)` → `IsRooted==false`、`SpeedMultiplier==0.65`。（每次進入都重縛＝走位交解控的生存空間沒了，`GDD.md:120` 圍欄失效）
- **V2-w `EnteringADifferentQuicksand_RootsAgain`**：離開 7 後進入 9 → `IsRooted==true`。（把「只縛一次」寫成全域旗標＝第二個流沙形同無效）
- **V2-x `LeavingTheQuicksand_RestoresFullSpeedImmediately`**：`Tick(dt,-1)` 當幀 `SpeedMultiplier==1f`。（減速有殘留倒數＝離開後還黏著）
- **V2-y `TargetInsideSteam_IsConcealed_FromAnAttackerOutside_ButNotFromOneInTheSameSteam`**（蒸氣陣營方向 A/B）：`IsConcealed(true,false,false)==true`；`IsConcealed(true,true,false)==false`；`IsConcealed(false,false,false)==false`。（忽略「攻擊者同霧」＝霧裡自己人也打不到；遮蔽恆真＝全場不能鎖定）
- **V2-z `AtOnePointFourNineSeconds_StillRevealed_AtOnePointFiveOne_ConcealedAgain`**：`NotifyDamaged()` 後累計 1.49s → `IsRevealed==true` 且 `IsConcealed(true,false,true)==false`；1.51s → `IsRevealed==false` 且 `IsConcealed(true,false,false)==true`（容差 1e-4）。（顯影時長被改；顯影不衰減＝一次受傷永久現形）
- **V2-aa `BeingDamagedAgain_RefreshesTheRevealToOnePointFive`**：剩 0.3s 時再 `NotifyDamaged()` → `RevealRemainingSeconds==1.5`（**不是 1.8**）。（疊加而非刷新）
- **V2-ab `ChannelSpeedMultiplier_IsHalvedInsideSteam_AndFullOutside`**：霧內 0.5、霧外 1.0。（係數被改＝`GDD.md:126` 的佔點懲罰失效）
- **V2-ac `Cast_IsRejectedDuringTheFiveSecondCooldown_AndAcceptedAtFiveSeconds`**：`TryBeginCast(Fire, 0)` true → `TryBeginCast(Fire, 4.99)` **false 且不重置冷卻** → `TryBeginCast(Fire, 5.0)` true；同一時刻 `TryBeginCast(Water, 4.99)` 仍 true（三技能各自獨立）。（冷卻共用一顆計時器；冷卻中按下反而重置＝永遠放不出來）
- **V2-ad `RemainingLabelIndex_CountsDownFiveToOne_AndReturnsZeroWhenReady`**：t=0 施放後，t=0.01→5、t=4.01→1、t=4.99→1、t=5.0→0。（標籤算成向下取整＝冷卻剩 0.5s 時顯示 0 卻按不動）
- **V2-ae `ComboHitstop_IsStrictlyInsideTheClampWindow_SoTheValueIsNotSilentlyRewritten`**：`30f < ComboHitstopMs && ComboHitstopMs < 60f`。（把 45 打成 4.5 或 450 → `CombatFeedbackService` 的 `Clamp(30,60)` 會靜默改成 30／60，所有「有頓挫」的斷言照樣綠，只有這一條會紅）
- **V2-af `IsHostile_IsTrueAcrossFactions_AndFalseWithinOne`**：`IsHostile(1,2)==true`、`IsHostile(1,1)==false`、`IsHostile(2,1)==true`。（比較恆真＝紅流沙連紅方自己都困；恆假＝流沙對誰都沒用）

### V3 突變（`python Tools/DotnetCheck/mutation_check.py`）

既有 **74 筆**全部仍被抓到（`ALL MUTATIONS CAUGHT`），**既有突變一筆不改**；新增 **≥15 筆**全部被抓到。清單（`檔案／改壞什麼 → 要變紅的非參數化測試`）：

| # | 檔案 | 改壞什麼 | 要變紅的測試（非參數化） |
|---|---|---|---|
| E1 | `ElementTuning.cs` | `WaterRadius = 3f` → `5f` | `Rock_LandingZeroPointOneMetreOutsideWater_MakesNothing` |
| E2 | `ElementTuning.cs` | `RootDurationSeconds = 1.2f` → `2f` | `AtOnePointOneNineSeconds_StillRooted_AtOnePointTwoOne_ItMovesAtSixtyFivePercent` |
| E3 | `ElementTuning.cs` | `QuicksandSlowMultiplier = 0.65f` → `1f` | `Entering_RootsForOnePointTwoSeconds_ThenSlowsToSixtyFivePercent` |
| E4 | `ElementTuning.cs` | `RevealDurationSeconds = 1.5f` → `3f` | `AtOnePointFourNineSeconds_StillRevealed_AtOnePointFiveOne_ConcealedAgain` |
| E5 | `ElementTuning.cs` | `SteamChannelSpeedMultiplier = 0.5f` → `1f` | `ChannelSpeedMultiplier_IsHalvedInsideSteam_AndFullOutside` |
| E6 | `ElementTuning.cs` | `FirestormAngleDegrees = 60f` → `120f` | `IsInsideSector_AcceptsOneDegreeInside_AndRejectsOneDegreeOutside` |
| E7 | `ElementTuning.cs` | `FirestormRangeMeters = 6f` → `12f` | `IsInsideSector_AcceptsAtRange_AndRejectsZeroPointOneMetreBeyond` |
| E8 | `ElementTuning.cs` | `FirestormDamageMultiplier = 1.5f` → `1f` | `Wind_SweepingABurningZone_MakesAFirestorm_AndConsumesTheBurningZone` |
| E9 | `ElementTuning.cs` | `BurnDamagePerSecond = 20f` → `10f` | `BurningZone_OverFourSeconds_DealsEightyDamageInTotal` |
| E10 | `ElementTuning.cs` | `BurnDurationSeconds = 4f` → `8f` | `BurningZone_OverFourSeconds_DealsEightyDamageInTotal` |
| E11 | `ElementTuning.cs` | `BoilDamage = 80f` → `40f` | `Fire_HittingHostileQuicksand_Boils_AndTerminatesTheQuicksand` |
| E12 | `ElementTuning.cs` | `FireDirectDamage = 40f` → `0f` | `Fire_OnOpenGround_DealsDirectDamage_AndLeavesABurningZone` |
| E13 | `ElementTuning.cs` | `QuicksandDurationSeconds = 3.5f` → `7f` | `Tick_ExpiresAQuicksandAtThreePointFive_AndSteamAtThree` |
| E14 | `ElementTuning.cs` | `WaterDurationSeconds = 6f` → `12f` | `Tick_ExpiresAWaterZoneExactlyAtSixSeconds` |
| E15 | `ElementTuning.cs` | `SkillCooldownSeconds = 5f` → `0f` | `Cast_IsRejectedDuringTheFiveSecondCooldown_AndAcceptedAtFiveSeconds` |
| E16 | `ElementTuning.cs` | `ComboHitstopMs = 45f` → `450f` | `ComboHitstop_IsStrictlyInsideTheClampWindow_SoTheValueIsNotSilentlyRewritten` |
| E17 | `ElementReactionLogic.cs` | 陣營方向反轉（`!=` → `==`） | `Fire_HittingFriendlyQuicksand_Rescues_AndTerminatesTheQuicksand` |
| E18 | `ElementReactionLogic.cs` | 陣營判定恆真（整個條件 → `true`） | `Fire_HittingHostileQuicksand_Boils_AndTerminatesTheQuicksand` |
| E19 | `ElementReactionLogic.cs` | 優先序倒過來（先查 Water 再查 Quicksand） | `Fire_OverlappingQuicksandAndWater_PicksQuicksandOnly` |
| E20 | `ElementReactionLogic.cs` | 蒸氣反應也留燃燒區（`NewZoneKindCode` 改 `Burning`） | `Fire_HittingWater_MakesSteam_AndLeavesNoBurningZone` |
| E21 | `ElementReactionLogic.cs` | 流沙取水域陣營而非石牆陣營 | `Quicksand_TakesTheFactionOfTheWall_WhenWaterAndWallDisagree` |
| E22 | `ElementReactionLogic.cs` | 風沒掃到燃燒區也給 `AoeDamage` | `Wind_SweepingABurningZone_MakesAFirestorm_AndConsumesTheBurningZone` |
| E23 | `ElementReactionLogic.cs` | `IsHostile` 恆真 | `IsHostile_IsTrueAcrossFactions_AndFalseWithinOne` |
| E24 | `ElementGeometry.cs` | 圓形 `<=` → `<`（排除邊界） | `IsInsideCircle_IncludesTheBoundary_AndExcludesAPointJustOutside` |
| E25 | `ElementGeometry.cs` | 扇形忽略角度（只比距離） | `IsInsideSector_AcceptsOneDegreeInside_AndRejectsOneDegreeOutside` |
| E26 | `ElementGeometry.cs` | 半角當全角（`totalAngle * 0.5f` → `totalAngle`） | `IsInsideSector_AcceptsOneDegreeInside_AndRejectsOneDegreeOutside` |
| E27 | `ElementGeometry.cs` | `CollectInsideCircle` 不檢查 `outIndices` 容量 | `CollectInsideCircle_ReturnsIndicesInAscendingOrder_AndStopsAtBufferCapacity` |
| E28 | `ElementZoneField.cs` | 池滿時回 `-1`（不擠） | `Spawn_WhenTheFieldIsFull_EvictsTheZoneWithTheLeastTimeLeft` |
| E29 | `ElementZoneField.cs` | 擠掉「剩餘時間最長」者 | `Spawn_WhenTheFieldIsFull_EvictsTheZoneWithTheLeastTimeLeft` |
| E30 | `ElementZoneField.cs` | `FindNearestContaining` 回第一個命中而非最近 | `Fire_OverlappingTwoWaterZones_PicksTheNearestCentre` |
| E31 | `QuicksandStatusLogic.cs` | 拿掉 `RootedByZoneId` 比對（每次進入都縛足） | `LeavingAndReentering_TheSameQuicksand_DoesNotRootAgain` |
| E32 | `QuicksandStatusLogic.cs` | 離開後仍保留 0.65 減速 | `LeavingTheQuicksand_RestoresFullSpeedImmediately` |
| E33 | `SteamConcealmentLogic.cs` | 遮蔽忽略「攻擊者同霧」 | `TargetInsideSteam_IsConcealed_FromAnAttackerOutside_ButNotFromOneInTheSameSteam` |
| E34 | `SteamConcealmentLogic.cs` | 遮蔽忽略顯影旗標 | `AtOnePointFourNineSeconds_StillRevealed_AtOnePointFiveOne_ConcealedAgain` |
| E35 | `SteamConcealmentLogic.cs` | `NotifyDamaged` 改成疊加 | `BeingDamagedAgain_RefreshesTheRevealToOnePointFive` |
| E36 | `ElementCastCooldowns.cs` | 冷卻中按下也重置計時 | `Cast_IsRejectedDuringTheFiveSecondCooldown_AndAcceptedAtFiveSeconds` |

**已知涵蓋缺口（明寫）**：`mutation_check.py` 只打得到 `Core/Logic/` 與 `Input/`（`PureLogic.Tests.csproj:14-20`），**碰不到 `Assets/Scripts/Combat/`、`Core/HeroController.cs`、`Core/HeroLocomotion.cs`、`UI/DebugHud.cs`** —— 接線層的鑑別力只能靠 V4 的 PlayMode 斷言把關。

### V4 Unity batchmode（EditMode 全綠；PlayMode **既有 66 條全部原封不動全綠**，另加下列且全綠）

- **V4-a 流沙縛足（正例，真實路徑）**：`ELEM: RED` → `WATER` 在英雄前方 4m 生紅水域 → 用符印牆／`ENEMY WALL` 把牆立在水域內 → 流沙成形且英雄在區內。斷言：成形當幀起 1.2s 內，① 送 `OnMoveDestinationSelected` 到 6m 外，英雄位置位移 **< 0.10m**；② `hero.TryBeginCadenceDash(1,0) == false` 且充能格數不變；③ `locomotion.IsMovementLocked == true`。1.21s 後再送同一個移動指令 → 英雄開始移動；**穩態速度比**：起步 0.3s 後開始量的 0.5s 窗口內走過的距離 ÷ 同一個測試先前在**無流沙**下、同一條路徑同一個窗口量到的距離 ＝ **0.65 ± 0.035**（容差來由：0.5s 窗口裡差一個 60fps tick ＝ 1/30）。量測窗口內**每一幀**都要斷言英雄仍在流沙內且路徑不被石牆擋住（否則量到的是繞牆或已離開）。**盤面約束（V4 全體適用）**：除了刻意的邊界反例，受試者與任何區域邊界的距離 ≥0.5m——注意 `WATER` 與 `ENEMY WALL` 同樣生在前方 4m，原地連按會讓英雄恰好站在流沙邊界（距圓心 4.0m＝`ReactionRadius`），浮點數決定紅綠；測試與試玩流程都要先往前走約 2m 再立牆。（縛足沒擋住走路／滑步；縛足結束後速度沒降；把減速做成停止）
- **V4-b 流沙縛足（最接近的反例）**：把牆立在距水域圓心 `WaterRadius + 0.10m` 處 → **不生流沙**（`ElementField.ActiveZoneCount` 只多了水域那一個、`IsMovementLocked == false`、英雄照常全速走到 6m 外）。（反應半徑用錯；把「牆碰到水域」寫成「牆的碰撞體碰到水域」）
- **V4-c 流沙陣營方向（不生效）**：`ELEM: BLUE` → 藍水 ＋ 藍牆 → 藍流沙；英雄（`BlueTeam`）站在區內 → `IsMovementLocked == false`、`SpeedMultiplier == 1f`、移動距離與無流沙時逐值相同（容差 0.10m）。（陣營比較恆真＝自家流沙困死自己）
- **V4-d 爆沸（陣營方向 B）**：藍水＋藍牆罩住木樁（`RedTeam`，血 600）→ `ELEM: BLUE` → `FIRE` 打在流沙上 → 木樁 `Health` 恰少 **80**（容差 0.01）、流沙 `Active == false`、**沒有**新的燃燒區。（救援／爆沸寫反；爆沸不終止流沙）
- **V4-e 救援（陣營方向 A）**：同上盤面，改 `ELEM: RED` → `FIRE` → 木樁 `Health` **不變**、流沙 `Active == false`。搭配英雄版：紅水＋紅牆困住英雄，縛足中按 `ELEM: BLUE` ＋ `FIRE` → **當幀** `IsMovementLocked == false`、下一個移動指令立刻生效。（救援不解控；救援也扣血）
- **V4-f 蒸氣遮蔽（正例＋反例，兩個判定點都驗）**：`WATER` → `FIRE` 在水域上 → 蒸氣罩住木樁，英雄站在霧外。① **點擊當下**：對木樁的螢幕座標呼叫 `PlayerInputService.OnWorldTap` → **未**送出 `OnCombatTargetSelected`（或送出但 `HandleTargetSelected` 不下令，`brain.CurrentTarget == null`）；② **已鎖定後持續驗證**：英雄從 ≥8m 外在起霧**前**鎖定木樁並開始追擊，**在第一刀命中之前**起霧（命中會讓木樁顯影 1.5s，那是 ③），起霧後 1 幀內 `hero.CanEngage(dummy) == false` 且大腦停止攻擊（`IsTargetValid` 回 false 的效果）。③ **打中過就看得到**：英雄已在霧外對木樁連續普攻時起霧 → 每刀刷新顯影，`CanEngage` 持續為 true、攻擊不中斷（`GDD.md:126` 受擊顯影）；下 `OnMoveDestinationSelected` 停手並走到霧外、距最後一刀 1.51s 後 → `CanEngage == false`。反例：把英雄走進同一團霧 → `CanEngage(dummy) == true`、點擊送得出 `OnCombatTargetSelected`。（只守點擊當下＝鎖定後站在霧外照樣偷打；`CanEngage` 沒有真的被兩個點共用）
- **V4-g 受擊顯影（1.49／1.51 兩側）**：對霧內木樁呼叫**一次** `dummy.ReceiveDamage(1f, DamageType.Elemental, null)`（走真實的 `CombatTargetBehaviour.ReceiveDamage`→`NotifyDamaged` 路徑；不用砲台——它每 0.25s 一發會一直刷新，量不到過期）→ 受擊後 1.49s `CanEngage == true`；1.51s 後 `CanEngage == false`（時間以 `Time.captureDeltaTime = 1/60f` 計，容差 0.034s）。（顯影沒接上 `ReceiveDamage`；顯影永不過期）
- **V4-h 火浪（正例）**：`FIRE` 落空地生燃燒區於英雄前方 4m → 木樁站在燃燒區內 → 立即記下 `Health`，按 `WIND` → 木樁再減少的量落在 **[59.99, 60.68]**（60＝火浪；上界多出的 0.667＝記錄與結算之間最多兩個 tick 的燃燒 DoT `20×2/60`；倍率寫死 1.0 會得到 40，仍在界外）、燃燒區 `Active == false`、`feedback.HitstopCount` 增量 ≥1。（風不消耗燃燒區＝可以連吹；倍率沒套到最終傷害）
- **V4-i 火浪（最接近的反例，含活性）**：先重跑一次 V4-h 的正例確認扇形真的量得到（**活性**），再把燃燒區移到扇形角外 1°（用 `ELEM`／走位擺好，取向 37° 避開軸對齊）→ 按 `WIND` → 木樁 `Health` 不變、燃燒區仍 `Active`、`HitstopCount` 不變。（扇形忽略角度；風無條件給傷害）
- **V4-j 空地火與燃燒區 DoT**：`FIRE` 落在木樁腳下（空地）→ 當幀木樁少 **40**，之後 4.0s 內再少 **80**（容差 0.5），4.5s 時總減少量仍是 120（容差 0.5）。（直傷與 DoT 只有一個；DoT 到期不停）
- **V4-k Combo 反饋**：流沙成形、蒸氣成形、火浪三個時刻各自 `feedback.HitstopCount` 增量 **恰為 1** 且 `IsHitstopActive == true`；救援與爆沸時 `HitstopCount` **不變**。（把 Combo 掛在每個反應上＝頓挫濫發；沒接反饋）
- **V4-l 沒有元素區域時逐值不變**：場上沒有任何元素區域時 —— ① `locomotion.SpeedMultiplier == 1f`、`IsMovementLocked == false`；② 英雄從 (0,0,0) 走到 (0,0,6) 的到達時間與同一測試在 `_elementField = null` 下重跑一次的結果相同（容差 0.034s）；③ `hero.CanEngage(dummy)` 與 `dummy.CanBeTargetedBy(hero.HeroFaction)` 對木樁、`TestWall_A`、敵方牆三者逐一相同。**鑑別力聲明**：③ 在無霧時恆真，它的鑑別力在 V4-f 的反例；本條真正能紅的是 ① 與 ②。（每幀無條件套用 0.65；`CanEngage` 在沒有元素場時 fail-closed）
- **V4-m 池滿（實機）**：連續施放到同時存活 6 個區域，再放第 7 個 → 第 7 個**確實出現在場上**（有可見的 `ElementZoneView`、`ActiveZoneCount == 6`）、剩餘時間最短那個消失、**沒有任何 `Debug.LogError`／例外**。另斷言預建池物件數 **8 > MaxLiveZones 6**；若 `Phase1Bootstrap` 偵測到池 ≤ 上限，要 `Debug.LogError`（比照批 3 r2 N1）且有一條測試 `ElementZoneViewPool_WhenNotLargerThanTheLiveCap_LogsAnError` 守它。（池＝上限時第 7 次靜默失敗＝按鈕沒反應，批 3 r1 CRITICAL-1 同型）
- **V4-n 冷卻（實機）**：按 `WATER` → 立即再按 → 場上仍只有一個水域；標籤在按下後顯示 `WATER 5`，4.0s 後顯示 `WATER 1`，5.0s 後回 `WATER`。三顆鈕各自獨立（按 `WATER` 之後 `FIRE` 仍可用）。（冷卻沒接上；冷卻共用）
- **V4-o HUD 版面不重疊（三組螢幕）**：`RecalculateLayout`（`:228-259`）直接讀 `Screen.width/height/dpi`，batchmode 改不了 → 新增一個接受 `(width, height, dpi)` 的 `internal` 計算入口、`RecalculateLayout` 改呼叫它，**既有六個 rect 的算式逐字搬移**（V4-p 守）。三組輸入＝線上實測過的裝置像素：`(1688, 780, 192)` 橫式、`(780, 1688, 192)` 直式、`(2532, 1170, 288)` 高 DPR 橫式（使用者實際用橫式玩：v0.5.0 線上符印鈕在 CSS (757,303)）。斷言 ——① 四個新 rect 兩兩不相交；② 四個新 rect 與 `_modeRect`／`_hitboxRect`／`_latencyRect`／`_gridRect`／`_enemyWallRect`／`_turretRect` 皆不相交；③ 四個新 rect 與 `RuneButtonLayout.Compute(w, h, ppm).Button`（換算回 GUI 座標）不相交；④ 四個新 rect 的 `yMax * _scale <= Screen.height`（整塊面板沒有跑出畫面）。另跑一組 `(844, 390, 0)`（dpi 回 0 的退路，`_scale=1`）：只要求①②③，並把四個新 rect 與 `_turretRect` 的 `yMax` 實測值寫進回報——草稿推算 v0.5.0 既有面板在這組就已超出 390（未實測），所以④在這組不當及格線。（面板長出畫面＝手機上按不到；新鈕蓋住符印鈕＝按了會立牆）
- **V4-p 既有 HUD 座標一像素不動**：`_modeRect`／`_hitboxRect`／`_latencyRect`／`_gridRect`／`_enemyWallRect`／`_turretRect` 六個矩形的值，與 v0.5.0 在同一 `Screen` 尺寸下的值逐值相同。（為了塞新鈕去挪既有鈕＝批 3 的 M2 觸控分流測試會跟著漂）
- **V4-q 不污染既有物理與導航**：區域視覺**沒有 Collider**、不進 NavGrid（`BlockGrid.BlockedCount` 在整段量測中只隨石牆變動）；英雄站在任何區域上不會被推開（`EjectFromBox` 不被觸發）。（區域視覺帶 Collider＝英雄繞路、點擊被擋）
- **V4-r 縛足不擋推出**：英雄在流沙內被一面新立的石牆壓住 → `EjectFromBox` 照常把英雄推出牆體（位移 > 0.10m）。（防線寫在太上游＝被困者永久卡在牆裡）

### V5 零配置

在 `ZeroAllocationTests.Combat_UpdateAndLateUpdate_AllocateNothing` 的**同一個量測窗口**內，由夾區內的 `Batch4Driver` 自己的 `Update()` 送出下列動作並各自斷言次數 ≥1（活性，比照既有 `runeDriver.Casts`／`batch3Driver` 模式）：

- **V5-a** 流沙成形 ≥1、蒸氣成形 ≥1、火浪 ≥1（三者各有獨立計數器）
- **V5-b** 英雄處於「縛足中」的幀數 ≥1、處於「0.65 減速中」的幀數 ≥1
- **V5-c** 蒸氣遮蔽生效（`CanEngage` 回 false）的幀數 ≥1；受擊顯影刷新 ≥1
- **V5-d** 區域視覺池借還 ≥2 次（含一次池滿擠掉）
- **V5-e** HUD 四顆鈕的標籤在冷卻中被重算 ≥5 次（驗零配置字串表真的走到）
- **V5-f** `UpdateBytes == 0`／`LateUpdateBytes == 0` 門檻、`Frames` 下限、各段 `deadline`（`:405`／`:479`／`:494`／`:536`／`:547`）與正向對照 `Probe_DetectsADeliberatePerFrameAllocation`（`:333-352`）**一字不動**
- **V5-g 鑑別力證據（必附）**：在 `ElementField` 的每幀區域 Tick 內注入一次 `new float[1]` → 零配置測試**必須紅**；注入後用**改壞前的備份副本**還原（不用反向 sed）。貼出紅、綠兩次的實際輸出。
- **已知缺口（照舊註明，寫進驗收指南 §14）**：動畫事件階段與 `DebugHud.OnGUI`（IMGUI，四顆新鈕的繪製與標籤在這裡）結構上不在 `Update`／`LateUpdate` 夾區內，零配置量不到；本批不改探針結構。

### V6 範圍守門

`git diff --stat 566a81f..` 逐檔對應 §1，且下列檔案**零改動**（`git diff --stat 566a81f.. -- <路徑>` 必須為空）：

- **V6-a 文件與契約**：`ARCHITECTURE.md`、`GDD.md`、`Assets/Scripts/Core/Contracts/` 下**全部既有檔**（`ICombatTarget.cs`、`ISkillTelegraphService.cs`、`ICombatFeedbackService.cs`、`IRuneWall.cs`、`IPlayerInputService.cs`、`ICadenceMover.cs`、`IPlayerStateMachine.cs`、`IFactionOwned.cs`、`IRockShield.cs`、`IRuneCastInput.cs`、`IWorldTapInput.cs`、`IDebugHudPanel.cs`、`IHapticService.cs`、`ControlMode.cs`）
- **V6-b asmdef**：`Assets/Scripts/**/*.asmdef`、`Assets/Tests/**/*.asmdef` 全部零改動（PlayMode 測試 asmdef 已含 `Vow.Core`／`Vow.Combat`／`Vow.Bootstrap`，新元件放 `Vow.Combat` 即可，**不得為了測試加引用**）
- **V6-c 手感核心檔**：`HeroCombatBrain.cs`、`PlayerStateMachine.cs`、`CadenceSim.cs`、`RuneGestureTracker.cs`、`RuneCastLogic.cs`、`RuneWallLogic.cs`、`ProjectileFlightLogic.cs`、`RockShieldLogic.cs`、`TapPickLogic.cs`、`SkillTelegraphService.cs`、`Tools/DotnetCheck/verify.sh`、`Tools/DotnetCheck/PureLogic.Tests/PureLogic.Tests.csproj`
- **V6-d 數值未漂移**：`grep -n` 逐一確認 `ElementTuning.cs` 的 20 個裁定數值與本檔 §2 表逐項相同 —— `WaterRadius 3`／`WaterDurationSeconds 6`／`FireDirectDamage 40`／`BurnRadius 3`／`BurnDurationSeconds 4`／`BurnDamagePerSecond 20`／`ReactionRadius 4`／`QuicksandDurationSeconds 3.5`／`SteamDurationSeconds 3`／`RootDurationSeconds 1.2`／`QuicksandSlowMultiplier 0.65`／`RevealDurationSeconds 1.5`／`SteamChannelSpeedMultiplier 0.5`／`FirestormAngleDegrees 60`／`FirestormRangeMeters 6`／`FirestormDamageMultiplier 1.5`／`BoilDamage 80`／`SkillCooldownSeconds 5`／`CastDistanceMeters 4`／`ComboHitstopMs 45`
- **V6-e 既有測試零改動**：`git diff --stat 566a81f.. -- Assets/Tests/` 中，**`ZeroAllocationTests.cs` 只准有新增行（`+` 行）與 `Batch4Driver` 相關，刪除行數＝0**；其餘既有測試檔刪除行數＝0、`[Ignore]`／`[Explicit]` 新增數＝0。基準：純邏輯 155＋1 略過、突變 74/74、EditMode 152＋4 略過、PlayMode 66/66 —— **既有測試一條都不准改、不准刪、不准加略過**。讀碼判斷本批**預期零條既有測試需要調整**（理由見 §6 第 3 點）；若實作時發現有衝突，**停手回報主對話**（檔案:行號＋原因），不得自行修改。
- **V6-f `03 R2` 第 2 項對照**：`git diff --stat 566a81f..` 涵蓋測試碼、`conftest` 等價物（`ZeroAllocationTests` 的探針與 Driver）、`ElementTuning.cs`、執行指令與環境變數，任一有改動就逐行寫「這行為什麼不會提高通過機率」。

### V7 對抗審查

fresh-context `opus`，prompt 必含「**找出會讓三個反應在實機上靜默失效、或讓玩家卡死／永久不可鎖定的情境**」，並點名查：① 蒸氣遮蔽是否只守了點擊當下、鎖定後的持續驗證是否真的走同一個 `CanEngage` ② 縛足是否可能因流沙終止／池滿被擠掉／場景重載而永久留著（`IsMovementLocked` 卡在 true） ③ `SpeedMultiplier` 是否可能在多個流沙重疊時被連乘成 0.42 ④ 區域池滿時第 7 個是否真的生得出來 ⑤ `ComboHitstopMs` 是否落在 `Clamp(30,60)` 外而被靜默改值 ⑥ 測試裡的數字常數是否與 §2 tuning 表一致 ⑦ 是否有測試放寬了既有容差或動了 `ZeroAllocationTests` 的門檻 ⑧ 扇形幾何是否只在軸對齊／45° 這類零鑑別力的輸入上被測 ⑨ 任何 PlayMode 測試的受試者是否恰好站在區域邊界上（距圓心＝半徑）⑩ 逐一比對測試裡的容差與等待秒數是否與 §5 開頭的全域容差一致、有沒有哪條既有測試的窗口秒數被動過 ⑪ 自家石牆是否吃到自家元素傷害。
CRITICAL／HIGH 全修或經使用者簽准；修完送**三態覆審**（「反駁我已修好這個宣稱」，逐條要「真的修好／表面修好／沒修到」），**上限 3 輪**。

### V8 送達與線上實機

- `git log origin/gh-pages -1` 顯示 v0.6.0；線上首頁版本列＝`v0.6.0 · build … · <sha>`
- Playwright（`hasTouch` 手機模擬、CDP 觸控）開 `https://9gf6p4448m-del.github.io/vow3d/`，三個反應各一輪截圖：
  ① `ELEM: RED` → `WATER` → **往前走約 2m（走進水域）** → `ENEMY WALL`（紅牆落在水域內）→ 截圖可見黃褐流沙圓盤，英雄走不動（STATE 不變成 Moving）約 1.2s 後開始慢慢走；再 `ELEM: BLUE` → `FIRE` 打流沙 → 截圖可見流沙消失、英雄立刻正常移動
  ② `WATER` → `FIRE` 打水域 → 截圖可見白霧圓柱；點霧裡的木樁 → 英雄**不去打**（截圖 STATE Idle）；走進霧裡再點 → 英雄開始攻擊
  ③ `FIRE` 落空地（木樁腳下）→ 截圖可見橙色燃燒圓盤＋木樁血條下降；按 `WIND` → 截圖可見扇形預警與燃燒區消失、木樁血條再掉一段
- **步驟 C 量測**：用 Playwright 量出四顆新鈕在**直式**（390×844）與**橫式**（844×390）下的 CSS 座標，連同「不與符印鈕／版本列／既有 HUD 列重疊」的實測結果寫進驗收指南 §14
- console 無 error／exception；截圖存 `vow-toolchain/browser-screenshots/v060-*`

### 覆蓋表（反應 × 正例／最接近反例／陣營兩個方向）

| 反應 | 正例 | 最接近的反例 | 陣營方向 A（生效／救援） | 陣營方向 B（不生效／爆沸） |
|---|---|---|---|---|
| 泥濘流沙（成形） | V2-f（水內 2.55m）、V4-a | V2-g（圓外 0.10m）、V4-b | V2-o 牆＝陣營 2 → 流沙 2 | V2-o 水＝陣營 1 不被採用 |
| 流沙（縛足／減速） | V2-t、V2-u（1.21s）、V4-a | V2-u（1.19s）、V2-v（同區重入不重縛） | V4-a 紅流沙 vs 藍英雄＝生效 | V4-c 藍流沙 vs 藍英雄＝不生效 |
| 蒸氣迷霧（成形） | V2-i、V4-f | V2-m（流沙優先，水域不被消耗） | V4-f 藍火＋藍水成霧 | V4-e 盤面紅火＋藍水**一樣**成霧（不分陣營） |
| 蒸氣（遮蔽／顯影） | V2-y(true,false,false)、V4-f① | V2-z（1.49s 仍可鎖）、V4-g（1.49／1.51） | V2-y 攻擊者在霧外＝遮蔽 | V2-y 攻擊者同霧＝不遮蔽、V4-f 反例 |
| 爆沸 | V2-j、V4-d（木樁 −80） | V2-m（同時蓋水域時水域不消耗） | — 見右欄 | V2-j／V4-d 火陣營＝流沙陣營 |
| 救援 | V2-k、V4-e（解控、0 傷害） | V4-e（木樁 Health 不變＝不得誤打成爆沸） | V2-k／V4-e 火陣營≠流沙陣營 | — 見左欄 |
| 擴散火浪 | V2-h 正段、V4-h（−60） | V2-h 反段（角外 1°）、V4-i、V2-c（6.1m） | V4-h 藍風吹藍火→打紅木樁 | V4-i 木樁改藍隊＝同一發扇形不扣血 |
| 空地火＋燃燒區 | V2-l、V2-s、V4-j | V2-m（落在流沙上就不是空地火） | V4-j 藍火打紅木樁＝扣血 | V4-j② 木樁改藍隊＝不扣血 |
| 區域池上限 | V2-p、V4-m（第 7 個生得出來） | V4-m（池 8 > 上限 6 的防呆 LogError） | — 與陣營無關，本列不適用 | — 與陣營無關，本列不適用 |
| 既有行為不變 | V4-l①②（速度／到達時間） | V4-l③（無霧時 `CanEngage` 恆等，鑑別力在 V4-f） | V4-p HUD 既有座標不動 | V6-e 既有測試零改動 |

## 6. 風險、未決，與核對 facts 行號的出入

### 核對結果

實際開檔核對 **`p2b4-facts.md` 的 38 處行號引用**，**3 處有出入**（都是「標示的範圍指到別的東西」，不影響結論）：

1. **`DebugHud.OnGUI` 的行號**：facts 寫「OnGUI :355-366」，實際 `private void OnGUI()` 在 **`UI/DebugHud.cs:271`**；`:355-366` 是 OnGUI **內部**繪製 `ENEMY WALL`／`TURRET` 兩顆鈕的那兩個區塊（`:355-359` enemy wall、`:361-366` turret）。本檔 §1 用的是 `:271-370`。
2. **`SkillTelegraphService` 的三條 LineRenderer**：facts 寫「三條 LineRenderer（:35-41）」，實際**欄位宣告**在 `Combat/Feedback/SkillTelegraphService.cs:19-21`（`_shaft`／`_head`／`_ring`）；`:35-41` 是 `Awake()`，在那裡**建立**它們。本檔 §4-6 用的是 `:35-41`（建立處），並補上 `:19-21`。
3. **`DebugHud` 的標籤屬性**：facts 寫「標籤 :121-122」，實際有**三**個（`:121 TurretButtonLabel`、`:122 EnemyWallButtonLabel`、`:123 ShieldValueLabel`）。

另有 2 處是**寫法差異、不算出入**：facts 的「`Editor/VOWPhase1SceneBuilder.cs`」實際路徑是 `Assets/Scripts/Editor/VOWPhase1SceneBuilder.cs`（facts 全篇以 `Assets/Scripts/` 為相對根）；facts 的「`RegisterUiRegion` :113-114」只點了新增的兩個，完整範圍是 `:109-114`。

**已核對且完全吻合（35 處）**：`HeroLocomotion.cs` `:123`／`:126`／`:222`／`:253`／`:360`／`:384`／`:398`；`HeroController.cs` `:14`／`:47`／`:105`／`:107`／`:108`／`:143`／`:146`／`:182`／`:208`；`PlayerStateMachine.cs:16-18`；`RuneWall.cs:57`／`:72`；`RuneCaster.cs:97`／`:108`；`EnemyWallSpawner.cs:56`／`:81`；`CombatTargetBehaviour.cs:14`／`:29`／`:74`／`:80`／`:86`／`:96`／`:114`／`:233`；`PlayerInputService.cs:306`／`:312`／`:328`／`:332`／`:333`／`:338`／`:345`；`ColliderTargetRegistry.cs:28`；`ICombatTarget.cs:16-22`／`:25-30`；`IFactionOwned.cs:8-11`；`ICombatFeedbackService.cs:10`／`:14`／`:18`／`:22`；`CombatFeedbackService.cs:11`／`:12-13`／`:17`／`:85-87`；`ISkillTelegraphService.cs:5-9`；`SkillTelegraphService.cs:43`／`:59`／`:105`／`:111`／`:131`／`:150`；`DebugHud.cs:80-83`／`:113-114`／`:125`／`:130`／`:198`／`:218-225`／`:228`／`:257-258`；`RuneButtonLayout.cs:11`／`:14`／`:19`；`VOWPhase1SceneBuilder.cs:276`／`:279`／`:288`／`:298`／`:331`／`:449`／`:479`；`ProjectileTuning.cs:20`／`:31`；`RuneTuning.cs:23`；`ZeroAllocationTests.cs:22`／`:40`／`:67`／`:74`／`:200`；`PureLogic.Tests.csproj:14`／`:15-19`／`:20`；`mutation_check.py:27`／`:325`／`:350`／`:357`（既有突變實數 **74 筆**，與基準相符）。

### 風險與未決

- **R1（最大風險）HUD 面板在橫式螢幕會長出畫面。** 以現行常數推算（`Pad=8`、`Row=22`、`InfoRows=10`，`RecalculateLayout` `:236-250`），v0.5.0 的面板底緣約在 GUI **401px**；加兩列後約 **487px**。WebGL 下 `Screen.dpi` 常回 0 → `_scale = 1`（`:234`）→ 在高度 390px 的橫式螢幕上**整塊都會溢出**。批 3 的驗收指南寫的是「844×390」，沒有標明哪個是寬。→ 本檔把它做成**可機械判定的 V4-o④**，並要求步驟 C 用 Playwright 兩個方向各量一次。（主對話審稿：`_scale = max(1, dpi/ReferenceDpi)`，線上實測 dpi=192；V4-o 已改用實測過的裝置像素三組＋dpi=0 退路一組。）若橫式真的放不下，處置**只能是壓縮新鈕的列高或改成單列四等分**（不得縮既有鈕、不得刪驗收條），實作者遇到就照做並在回報中載明。
- **R2（已知限制，不處理）**：蒸氣遮蔽只作用在**英雄的鎖定**上。砲台子彈（`Projectile.ResolveSweep`）不查蒸氣——霧裡照樣被打到。理由：砲台是除錯設施、不是玩家操作面，而 `Projectile` 走的是掃掠射線、沒有「鎖定」這件事。寫進驗收指南 §14。
- **R3（已知限制，照裁定 4）**：霧內引導晶塔減半只有純邏輯測試背書（`SteamConcealmentLogic.ChannelSpeedMultiplier` 本批無生產呼叫點），與批 1 的 `TryPenetrate`、批 3 的 `Absorb` 同型。
- **R4（已知限制）**：木樁不會移動，所以流沙的縛足與減速**只有英雄一個受試者**；`QuicksandStatusLogic` 的多單位情形（同一個流沙同時困住兩人）只有純邏輯測試，實機驗不到。
- **R5（預期零條既有測試需要調整，理由）**：① `DebugHud` 的 `InfoRows` 不動、既有六個 rect 座標不動 → 批 3 M2 的觸控分流測試（走 `TryGetTurretButtonScreenPoint`）不受影響；② `RuneWall.Activate` 的新呼叫在 `_elementField == null` 時完全跳過 → `RuneWallPlayTests`／`WallDetourPlayTests`／`ShieldAndProjectilePlayTests` 的盤面沒有 `ElementField`，行為逐值不變；③ `HeroController.CanEngage` 在 `_elementField == null` 時退化為原本的 `CanBeTargetedBy` → 既有鎖定測試不變；④ `HeroLocomotion` 的 `_speedMultiplier` 初值 1f、`_movementLocked` 初值 false → `_agent.speed` 與 v0.5.0 逐值相同。**若實作時發現任何一條既有測試因新功能而紅，停手回報主對話（檔案:行號＋原因），不得自行修改測試。**
- **R6（設計定稿時重查硬規則）**：本批不碰憑證、不碰真實下單、不改策略數值——`~/CLAUDE.md` 硬規則 1–3 不觸發；巨檔 Read 不涉及（硬規則 4）；所有數值來自 GDD 與使用者裁定、不是估算（硬規則 5）。
- **R7（未決，待試玩）**：§4-10「風沒掃到燃燒區不造成傷害」是暫定；`ComboTrauma = 0.5f`、`MaxLiveZones = 6`、區域視覺的形狀與顏色全部【試玩必調】。
- **目前沒有需要使用者再裁定的題目。**

> 本節以外的所有選擇（§2 簽名、§4 各項、§5 條文）皆為可逆的實作決定，依 `03 R3` 自決並已在本檔載明。

## 7. 主對話審稿紀錄（2026-09-20，進 repo 前）

草稿由 fresh `opus` 起草（`vow-toolchain/PHASE2_BATCH4_PLAN.draft.md` 保留原稿）；主對話逐行審過後的修訂全部是**補定義或加嚴**，已直接寫進上文：

1. §4-2 反應區圓心＝被消耗水域的圓心；§4-10「掃到」＝燃燒區圓心落在扇形內（原稿兩者都沒定死）。
2. §4-7 元素 AOE／DoT 跳過施放陣營自己的石牆（原稿會讓藍爆沸打到藍牆）。
3. V4-a 由「1.0s 走 3.575m」改成穩態速度比 0.65±0.035（原稿假設零起步加速，主對話未查證 `HeroLocomotion` 的加速度；用比值就不依賴它）；加盤面約束：受試者離邊界 ≥0.5m（`WATER` 與 `ENEMY WALL` 同生在前方 4m，原地連按英雄恰在流沙邊界）。
4. V4-f② 補「第一刀命中之前起霧」、新增③「打中過就看得到」（原稿②在英雄已打中木樁時不可能通過：每刀都刷新顯影）。
5. V4-g 傷害來源改成單次 `ReceiveDamage`（砲台 0.25s 一發，量不到過期）。
6. V4-h 的 60 加上最多兩個 tick 的 DoT 上界。
7. V4-o 改用可注入的版面計算入口與線上實測過的三組裝置像素＋dpi=0 退路一組。
8. V7 追加點名 ⑨⑩⑪。

## 8. Checkpoint A 驗收紀錄（2026-09-20；標的 `f5210bb`）

主對話親自重跑：`verify.sh`＝187 通過＋1 略過、`RESULT: ALL PASS`、紅線 8 條期望數不變；`mutation_check.py`＝110／110（輸出 `vow-toolchain/p2b4-stepA-mutation-main.txt`）；`git diff --stat 6ed7285..f5210bb`＝29 檔（7 Logic＋6 測試＋13 .meta＋`mutation_check.py` 純新增 111 行、刪除 0 行）。

**兩條凍結條文的輸入被實作者調整（依 `02 §2.1` 例外自行修正、已回報使用者）**——兩處都是「原條文對正確實作也過不了」，不是實作過不了才改：

- **V2-a**：原文「方位角 23.5°、距圓心 3.000 → true」。float32 構造不出恰在半徑上的 23.5° 點（最後一位捨入決定內外），改用 3-4-5 分量 `(1.8, 2.4)`（1.8²＋2.4²＝9.0，約 53°，仍避開軸對齊與 45°）。鑑別力證據：E24（`<=`→`<`）在改後的測試上 CAUGHT。
- **V2-s**：原文「4.0s 之後不再累加」在第 240 個 `1/60f` tick 斷言到期；`RemainingSeconds` 浮點累減留約 3e-6s 殘差，正確實作此刻尚未 `<=0`。改為第 241 個 tick 斷言到期（放寬 1 tick＝16.7ms）；總傷害 80±0.5 不動。鑑別力證據：E10（4s→8s）CAUGHT。

**實作者自行加嚴（不需同意，記錄）**：V2-t／V2-u／V2-v／V2-ab 的期望值由讀 tuning 欄位改成寫死 0.65／0.5（原寫法讓 E3、E5 MISSED）；V2-n 兩個水域的生成順序對調（原順序讓 E30 MISSED）。

**留給步驟 B 的介面事實**（`vow-toolchain/p2b4-readback.md` 疑義）：`ReactionOutcome` 不含新區域的 id／座標；§4-9 的「刷新燃燒區」在純邏輯層以 Terminate＋原圓心重 Spawn 實作（id 會換）；蒸氣區 `FactionId`＝`ElementReactionLogic.NeutralFactionId = 2`（對應 `Faction.Neutral`）。§4-9 的兩個分支（火落在燃燒區／蒸氣內）目前沒有任何測試守，步驟 B 要各補一條 PlayMode 或 EditMode 測試。
