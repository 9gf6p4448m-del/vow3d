# 計畫：Phase 2 批 3 —— 陣營校驗破牆得護盾＋友軍彈道穿透己方石牆（2026-09-19）

> 已知良好狀態：main `e5768c4`（批 2 §6 R13 的 R3-1 修復已併回；純邏輯 143＋1 略過、突變 56/56、EditMode 140＋4 略過、PlayMode 41/41）。工作分支 `phase2-batch3`。
> 規格依據：`GDD.md:93-100`（防禦性圍欄二）、`GDD.md:244`、`GDD.md:298-300`（漏洞八）、`ARCHITECTURE.md:100-138`（`ICombatTarget`／`Faction`／`IRuneWall` 逐字契約，**不得改**；要補東西一律新增介面，先例＝批 1 的 `Core/Contracts/IRuneCastInput`）、`ARCHITECTURE.md:17,191-192`（紅線：零 carving、主線程零 GC）。
> 本檔 §5 為凍結驗收條件；要動它只能走 `02 §2.1`。

## 0. 使用者裁定（2026-09-19，需求對齊一輪）

1. **點己方牆＝穿過去點到牆後地板**（維持現手感），敵方／中立牆點得到、會鎖定去砸。己方牆無法用點的砸碎；「砸己方牆 0 護盾」只在邏輯層以測試守住。
2. **護盾這批只做顯示＋邏輯**：英雄頭上護盾條＋HUD 數值＋2.5s 倒數；「吸收傷害」的規則寫在純邏輯層並有測試。英雄血量與會打英雄的敵人留給後面批次。
3. 自行採用、試玩即改：兩本血量帳合一（`RuneWallLogic` 為單一來源）；場上兩面灰色測試牆視為**中立**牆、砸碎給盾；友軍測試砲台＝藍隊、固定位置、**預設關閉、由除錯 HUD 的 `TURRET` 鈕開關**、開啟時每 0.25s 朝木樁射一發直線彈（傷害 20、速度 20 m/s；射速與預設關閉是主對話審稿時的修訂，理由見 §4-4⑤、§4-7），穿己方牆走規格衰減，撞敵方／中立牆被擋下並對牆造成子彈傷害；「生成敵方牆」除錯鈕＝英雄正前方 4m 生一面紅隊牆（紅色、不佔己方 2 面上限、壽命同己方牆）；護盾重複取得＝刷新回 150、不疊加；批 3 結尾推 v0.5.0。
4. 沿用常設約束：`Core/Logic` 零 UnityEngine；執行期零 GC（投射物、護盾條一律池／預建；WebGL 執行期不得 `CreatePrimitive`）；不用 Linq；`ARCHITECTURE.md`／`GDD.md`／逐字契約不改；不得 `NavMeshObstacle`／carving；不得為方便加 asmdef 引用。批 2 §3 六個手感核心檔中 **`RuneWallLogic.cs` 本批必須動**（合帳），其餘五個零改動。

## 1. 檔案清單與批次

### 步驟 A：純邏輯（不需 Unity，`verify.sh` 可驗）

- 新增 `Core/Logic/ProjectileTuning.cs` —— 砲台／子彈／護盾全部數值的單一來源（見 §4 數值表）
- 新增 `Core/Logic/ProjectileFlightLogic.cs` —— 掃掠命中的排序與分派、每發對同一面牆只算一次、倍率連乘（§2）
- 新增 `Core/Logic/RockShieldLogic.cs` —— 護盾值／倒數／吸收／**授予規則**（§2）
- 新增 `Core/Logic/TapPickLogic.cs` —— 「跳過己方牆、取最近可接受命中」的挑選規則（§2）
- 修改 `Core/Logic/RuneWallLogic.cs` —— 合帳所需的最小補強：`ApplyDamage`（`:40`）從此有生產呼叫點（近戰傷害）；另補一個唯讀 `bool IsPenetrationExhausted => PenetrationCount >= _tuning.MaxPenetrations`（供 §2 分派）。**既有五條測試、四個穿透數值、`Tick`／`TryPenetrate`／`Kill` 的算式一字不動**（這是本批唯一動到批 2 §3 六個手感核心檔的地方，理由＝裁定 3 的合帳）
- 新增 `Assets/Tests/EditMode/ProjectileFlightLogicTests.cs`、`RockShieldLogicTests.cs`、`TapPickLogicTests.cs`
- 修改 `Tools/DotnetCheck/mutation_check.py` —— 追加批 3 突變（只增不改既有突變）
- `Tools/DotnetCheck/PureLogic.Tests/PureLogic.Tests.csproj` **預期零改動**：`Core/Logic/**/*.cs`（:14）與 `Assets/Tests/EditMode/**/*.cs`（:20）都是 glob；`Core/Contracts` 是逐檔列舉（:15-19），所以 **`Core/Logic` 的新檔不得 `using` 任何 Contracts 型別**（`Faction` 定義在含 `using UnityEngine` 的 `ICombatTarget.cs`，收不進純邏輯專案）→ 純邏輯一律用 `int` 陣營代碼，Unity 端做 `(int)Faction` 轉換
- **Checkpoint A**：`verify.sh` 全綠、突變全抓到；Unity 端未接線，遊戲行為與 v0.4.1 相同

### 步驟 B：Unity 端接線

- 修改 `Combat/CombatTargetBehaviour.cs` —— 血量存取虛擬化（§2）＋`OwnerFaction` 序列化欄位＋實作新契約 `IFactionOwned`＋死亡宣告收斂 `NotifyDeath()`
- 修改 `Combat/RuneWall.cs` —— 覆寫血量鉤子改讀寫 `_logic`（合帳）；`Activate` 設 `OwnerFaction`；壽命到期改走 `NotifyDeath()`；移除自有的 `OwnerFaction` 屬性（`RuneWall.cs:20`，全專案原本沒人讀）
- 新增 `Core/Contracts/IFactionOwned.cs`、`Core/Contracts/IRockShield.cs`（新增介面，不動既有契約）
- 新增 `Combat/RockShieldBehaviour.cs`（掛英雄，訂 `HeroController.OnAttackHitResolved`，`HeroController.cs` **零改動**）、`Combat/HeroShieldBar.cs`（頭上護盾條，`QuadMeshFactory` 預建）
- 新增 `Combat/TestTurret.cs`、`Combat/Projectile.cs`（預建池、掃掠、零配置）
- 新增 `Combat/EnemyWallSpawner.cs` —— 除錯鈕用的紅隊牆池（獨立於 `RuneCaster` 名冊）
- 修改 `Input/PlayerInputService.cs:263-275` —— `Physics.Raycast` 換 `RaycastNonAlloc` ＋ `TapPickLogic`；新增 `SetLocalFaction(Faction)`
- 修改 `UI/DebugHud.cs` —— 同一列兩顆鈕「ENEMY WALL」「TURRET: OFF／ON」＋一列護盾數值（六處：`:44-51`／`:67-96`／`:147-150`／`:165-178`／`:199-201`／`:247-267`）
- 修改 `Bootstrap/Phase1Bootstrap.cs` —— 池分割（見 §4-6）、注入陣營與護盾、接砲台／敵牆生成器
- 修改 `Editor/VOWPhase1SceneBuilder.cs` —— **刪 `:282` 的 `SetLayerRecursively(wall, IgnoreRaycastLayer)`**（符印牆回 Default 層）；新增砲台、子彈池、敵方牆池、護盾條載體；符印牆池與敵方牆池各給一個父物件
- 修改 `Assets/Tests/PlayMode/RuneWallPlayTests.cs`（僅 §5「既有測試衝擊」點名的那一條）；新增 `Assets/Tests/PlayMode/ShieldAndProjectilePlayTests.cs`；修改 `ZeroAllocationTests.cs`（只加 Driver 與活性斷言，門檻與正向對照零改動）
- **Checkpoint B**：Unity batchmode EditMode＋PlayMode 全綠

### 步驟 C：上線

- `VowVersion.cs` → `0.5.0`；`docs/PHASE1_ACCEPTANCE_GUIDE.md` 補 §13
- fresh-context opus 對抗審查 → 修 → 三態覆審（上限 3 輪）→ merge → push → `Tools/deploy-webgl.bat` → 真實瀏覽器驗線上版

## 2. 介面（先寫死再實作；`Core/Logic` 全部零 UnityEngine、執行期零配置、不用 Linq）

```csharp
// Core/Logic/ProjectileFlightLogic.cs
public enum SweepHitKind { Ignore = 0, FriendlyWall = 1, BlockingWall = 2, Target = 3 }
public struct SweepHit { public float Distance; public int Id; public SweepHitKind Kind; }
public sealed class ProjectileFlightLogic
{
    public ProjectileFlightLogic(int maxPenetratedWalls);   // 固定容量陣列，執行期零配置
    public float DamageMultiplier { get; }                  // Reset 後為 1；每次穿透乘上該牆回傳的倍率
    public void Reset();
    public static void SortByDistance(SweepHit[] hits, int count);  // 插入排序；同距離取 Id 小者（決定性）
    public bool HasPenetrated(int wallId);
    public void RecordPenetration(int wallId, float multiplier);    // 同 id 重複呼叫不得再乘一次
    public static float StepLength(float speed, float deltaSeconds);
}

// Core/Logic/RockShieldLogic.cs
public sealed class RockShieldLogic
{
    public RockShieldLogic(ProjectileTuning tuning);
    public float Amount { get; }  public float RemainingSeconds { get; }  public bool IsActive { get; }
    public void Grant();                       // 一律刷新回滿值＋滿倒數，不疊加
    public void Tick(float deltaSeconds);      // 倒數歸零 → Amount 歸零
    public float Absorb(float incomingDamage); // 回傳穿過護盾的殘餘傷害；扣減 Amount，Amount 歸零不代表倒數結束
    // 授予規則（純邏輯、int 陣營代碼；ownerKnown=false 一律不給＝fail-closed）
    public static bool ShouldGrantOnMeleeKill(bool targetKilled, bool targetIsWall,
                                              bool ownerKnown, int ownerFactionId, int attackerFactionId);
}

// Core/Logic/TapPickLogic.cs
public static class TapPickLogic
{
    // distances/ownWall 為平行陣列。回傳「ownWall==false 的命中中距離最小者」的索引；全被跳過回 -1。
    public static int SelectNearestAcceptable(float[] distances, bool[] ownWall, int count);
}

// Core/Contracts/IFactionOwned.cs（新增介面，ICombatTarget 一字不動）
public interface IFactionOwned { Faction OwnerFaction { get; } }
// Core/Contracts/IRockShield.cs
public interface IRockShield { float Amount { get; } float RemainingSeconds { get; } void Grant(); }
```

`CombatTargetBehaviour` 血量存取虛擬化（合帳的載體，**行為對非符印牆目標逐行等價**）：新增
`protected virtual float ReadHealth()` / `ReadMaxHealth()` / `protected virtual float ConsumeDamage(float amount)`（回傳實扣量）/ `protected virtual void ResetHealth(float maxHealth)`；`Health`／`MaxHealth`／`IsAlive`／`HealthNormalized` 全部改讀鉤子；`Awake`（`:44`）、`Configure`（`:51`）、`Revive`（`:79`）改呼叫 `ResetHealth`；`ReceiveDamage`（`:61-75`）改呼叫 `ConsumeDamage` 並在結尾走新的 `protected void NotifyDeath()`（一次生命只宣告一次，`ResetHealth` 重置旗標）。
`RuneWall` 覆寫四個鉤子全部轉給 `_logic`，`ResetHealth` 為 no-op。
**寫入點的分母（先數再收斂）**：石牆血量目前有 **9** 個寫入點分屬兩本帳——`CombatTargetBehaviour.cs:44`（`Awake`）、`:51`（`Configure`）、`:66`（`_health -= applied`）、`:71`（`_health = 0`）、`:79`（`Revive`）＝5 個；`RuneWallLogic.cs:24`（`Activate`）、`:43`（`ApplyDamage`）、`:57`（`TryPenetrate`）、`:65`（`Kill`）＝4 個。合帳後前 5 個對 `RuneWall` 全部被覆寫成 no-op／轉發，**石牆血量的寫入點收斂為 `RuneWallLogic` 內部那 4 個**（分母歸一，非靠測試涵蓋）；對木樁與測試牆則仍走原本那 5 個、逐行等價。驗收：`grep -n "_health" Assets/` 除 `CombatTargetBehaviour.cs` 外零命中（V6）。

## 3. 不做什麼

- 英雄血量、會攻擊英雄的敵人、護盾真的吸傷（`Absorb` 本批無生產呼叫點，理由與批 1 的 `TryPenetrate` 同，見 §4 Simplicity 例外表）
- 批 4 元素反應；正式美術、粒子、著色器（冷庫協議）
- **不新增任何自訂圖層**、不動 `ProjectSettings/TagManager.asset`、不動 `Physics` 碰撞矩陣
- 不動 `HeroCombatBrain`／`PlayerStateMachine`／`CadenceSim`／`RuneGestureTracker`／`RuneCastLogic` 任何一行；**不動 `HeroController.cs`**（護盾走既有事件 `HeroController.cs:39,227`）
- 子彈不進 NavGrid、砲台不給 Collider（不擋路、不吃點擊、不改任何既有導航與點擊行為）
- 遠程（子彈）打碎中立／敵方牆**不給任何人護盾**——護盾是近戰特權（`GDD.md:95`）
- 不動既有突變、`verify.sh` 紅線 grep、`ZeroAllocationTests` 的門檻與正向對照

## 4. 假設與取捨（自行採用、可推翻，會同步寫進驗收指南 §13）

**1. Ignore Raycast 的替換：採「射線命中後做陣營校驗、己方牆沿射線往後找」，符印牆回 Default 層。**
先數分母（`grep` 全 `Assets/`＋`Tools/`）：會發射線／做物理查詢的入口 **N＝5**——`PlayerInputService.cs:267` `ScreenPointToRay`（不吃 mask）、`:268` `Physics.Raycast(DefaultRaycastLayers)`（**全專案唯一的選取路徑**）、`HeroLocomotion.cs:385` `SphereCastNonAlloc(AllLayers)`（身體阻擋）、`RuneWallPlayTests.cs:292` `ScreenPointToRay`、`:294` `Physics.Raycast(DefaultRaycastLayers)`（測試）。`RaycastAll`／`Overlap*`／`BoxCast`／`Linecast`／`GraphicRaycaster`／`EventSystem` 皆 0 命中；`Physics.queriesHitTriggers`／`IgnoreLayerCollision`／`Physics2D` 皆未使用。
→ 把符印牆從層 2 移回層 0 的**效果**只落在 2 個入口：生產的 `:268`（正是要改的）與測試的 `:294`（§5 點名）。`HeroLocomotion` 用 `AllLayers`，兩種層都擋得住身體；層 2↔0 的碰撞矩陣預設皆全開，實體碰撞不變。
**否決分陣營圖層**的理由：(a) 要寫 `TagManager.asset`，而層 2 目前同時住著**英雄本體**（`VOWPhase1SceneBuilder.cs:219`）、邊界牆（`:156`）、GRID 疊圖（`:308`）、血條四邊形（`QuadMeshFactory.cs:40`）、技能預警（`SkillTelegraphService.cs:169`）——把層 2 加進點擊 mask 會讓這五類全部變成可點，分母比移牆大得多；(b) 專案全無 `LayerMask` 使用慣例（0 命中），得從零建立；(c) 牆的擁有者在 `Activate` 當下才決定，圖層得跟著改，多一個會漂移的狀態；(d) 圖層是全域面，任何未來的物理查詢都隱式依賴它，違反「防線按危險的效果寫」。
**實作**：`Physics.RaycastNonAlloc(ray, _tapHits, 500f, DefaultRaycastLayers, QueryTriggerInteraction.Ignore)`，`_tapHits` 為預配置 `RaycastHit[16]`（零配置）；逐筆用 `ColliderTargetRegistry` 解析，凡 `TargetFaction == DestructibleWall` 且 `OwnerFaction == 本地陣營` 者標記為「己方牆」；交給 `TapPickLogic.SelectNearestAcceptable` 取最近的非己方牆命中（**不需排序**：被跳過的條件只看命中自身，與順序無關）。命中數＝緩衝上限時 `Debug.LogWarning`（緩衝溢位會讓最近的合法命中被丟掉）。「點敵方牆後方的地板」＝敵方牆較近 → 命中敵方牆 → 鎖定去砸（與點任何敵方目標一致）。
**2. 中立／己方的判定**：`CombatTargetBehaviour` 新增序列化 `_ownerFaction`（預設 `Neutral`）。`TestWall_A/B` 用預設值＝中立（`VOWPhase1SceneBuilder.cs:250` 的 `_faction` 仍是 `DestructibleWall`，不動）；`RuneWall.Activate` 寫入實際擁有者；除錯鈕生的牆＝`RedTeam`。木樁（`RedTeam`）不是牆，不受影響。
**3. 護盾歸屬＝最後一擊的近戰者**：`RockShieldBehaviour` 訂閱 `HeroController.OnAttackHitResolved`（`HeroController.cs:39`，於 `:227` 送出），條件＝`!target.IsAlive && target.TargetFaction==DestructibleWall && target is IFactionOwned o && o.OwnerFaction != hero.HeroFaction`。因此**壽命到期／穿透耗死／被第 3 面牆擠掉**在結構上就走不到授予路徑（它們不經過英雄的攻擊結算）——§5 仍逐條驗，因為「把護盾掛在牆的 `OnDied` 事件上」是很自然的另一種實作，那樣三條全會給盾。
**4. 不變量掃描（動手前）**：① `OwnerFaction != attackerFaction` 對**己方符印牆恆假**（單機只有藍隊英雄）→ true 分支只能由中立測試牆與除錯鈕的紅隊牆行使；**除錯鈕是驗收設施，不是附加功能**，沒有它這條判斷式在遊戲內零鑑別力。② `targetIsWall` 非恆真（木樁是 `RedTeam`）。③ `StepLength(speed>0, dt>0) > 0` 恆真 → 不得當判斷式，只當算式。④ **`PenetrationCount >= MaxPenetrations`（10 發上限）在任何射速下都不可達**。推導：第 n 發於 t=(n−1)T 命中，該發結算後剩餘壽命 R(n)=5−(n−1)T−0.5n；要達到 n=10 需 5−9T−5.0>0 ⟺ T<0，不可能。也就是說壽命成本（10×0.5s＝5.0s＝整條壽命）**永遠先於**發數上限歸零 → 這條判斷式是冗餘的，拿掉它既有測試也抓不到（`!IsAlive` 會先攔，`RuneWallLogic.cs:51,58`）。**批 3 不得寫任何以「第 10 發牆塌」為內容的驗收條文（零鑑別力）**。
⑤ 由 ④ 的同一條公式（加上第 1 發的到達延遲 t0）：R(n)=5−t0−(n−1)T−0.5n。原暫定 T=1.0s → R(4)=−t0≤0，第 4 發到達時牆**恰好**壽命歸零——穿透數是 3 還是 4 取決於幀時序（刀鋒條件，寫成驗收條文必然忽紅忽綠），而且 `DecayedDamageMultiplier`（第 6 發起 ×0.85）永遠行使不到。**主對話審稿修訂：射速定為 T=0.25s。** 此時第 7 發到達前剩餘壽命＝0.5−t0（t0<0.5s 即 >0），第 7 發穿透後 R(7)=−t0−0.0<0 → **第 6 發 ×0.85 必然看得到、牆必然在第 7 發被打塌**，時序餘裕 ≥0.25s（要求測試把牆放在距砲台 ≤3m 處＝飛行 ≤0.15s，且「先放牆、再開砲台」，使 t0≤0.2s）。
**5. 穿隧與掃掠（推導）**：子彈速度 20 m/s，牆厚 0.6m（`RuneTuning.WallThickness`；測試牆 scale.z 同為 0.6）。每幀位移 Δs＝20·Δt：120fps→0.167m、60fps→0.333m、30fps→**0.667m**、閾值 Δs>0.6 ⟺ fps<33.3。WebGL 手機低於 33fps 是常態 → **點查詢必然漏穿**，一律用 `Physics.RaycastNonAlloc(prev, dir, buf, Δs, AllLayers, Ignore)` 掃掠，再 `ProjectileFlightLogic.SortByDistance` 按距離升冪處理（此處順序決定結果，與點擊不同）。非 `ICombatTarget` 的命中（地板、英雄、邊界、疊圖）一律 `Ignore` 續飛。
**6. 池的分割**：`Phase1Bootstrap.cs:96` 現在把 `FindObjectsOfType<RuneWall>()` **整批**當成玩家池丟給 `RuneCaster`；敵方牆也是 `RuneWall`，不分割就會佔用玩家的 2 面上限。改為由 `SceneBuilder` 建的兩個父物件（`RuneWallPool`／`EnemyWallPool`）分割，`FindObjectsOfType` 的順序不得被依賴。敵方池 2 面、FIFO 擠掉最舊；敵方牆照樣登記 NavGrid（`Phase1Bootstrap.cs:143-147` 的 `is RuneWall` 分支自動涵蓋）與 `ColliderTargetRegistry`（`:73-74`）。
**7. 場景幾何與開關**：**砲台預設關閉**（`TestTurret.IsFiring=false`），由除錯 HUD 的 `TURRET: OFF／ON` 鈕或測試呼叫 `SetFiring(bool)` 開關——理由：木樁血 600（`VOWPhase1SceneBuilder.cs:231`），砲台常開會以 80 DPS 持續打死木樁，污染 Phase 1 手感試玩（斬殺反饋、連擊節奏）與所有斷言木樁血量的既有測試；預設關閉＝不按鈕時場上行為與 v0.4.1 逐值相同。砲台 `TestTurret` 固定在 **(−8, 1.0, 6)**，開火方向於 `Initialize` 時由「砲台→木樁 (0,1,6)」算出並固定（木樁不移動；木樁不在場則不開火）＝**+X，距離 8.0m，飛行 0.400s**。此彈道（z=6 的橫向走廊）與英雄出生點 (0,0,0)（`VOWPhase1SceneBuilder.cs:186`）相距 6m、與 `TestWall_A`（z∈[2.7,3.3]）、`TestWall_B`（x∈[6.7,7.3]）皆不相交，所以不改變任何既有測試的幾何。子彈最大射程 30m／壽命 1.5s，池 4 發。
**8. 多面牆的倍率**：沿途每面己方牆回傳的倍率**連乘**（兩面各 ×0.85 → 0.7225）。`GDD.md:291` 禁的是「穿透疊加**增**傷」，衰減連乘方向一致。【試玩必調】
**9. 穿不動的己方牆**：`TryPenetrateBullet` 回 `false`（牆已死或已達 10 發上限）→ 子彈**續飛且倍率不再變動**（那面牆正在崩解，不該再擋自己人）。
**10. 暫定數值表**（全部進 `ProjectileTuning`，【試玩必調】）：砲台射速 **0.25 s／發**（預設關閉）；子彈速度 20 m/s、傷害 20、半徑 0（純射線）、射程 30m；護盾 150 點、2.50s；敵方牆壽命 5.0s（＝`RuneTuning.WallLifespanSeconds`）、正前方 4.0m（＝`QuickCastDistance`）、敵方池 2 面；子彈池 4；`_tapHits` 16、子彈掃掠緩衝 8、單發已穿透牆名冊 8。

Simplicity 例外：

| 違反了什麼 | 為何必要 | 更簡單的方案為何被否決 |
|---|---|---|
| 寫 `Absorb` 但本批無生產呼叫點 | 裁定 2 明指「吸收規則寫在純邏輯層並有測試」；`IRockShield` 契約要能編譯 | 留空殼＝後面批次容易漏掉，且刷新／疊加的語意沒有地方守 |
| 新增砲台與敵方牆除錯鈕 | §4-4 的不變量掃描：沒有它們，陣營校驗與穿透兩條主線在遊戲內零鑑別力 | 只寫純邏輯測試＝線上版驗不到，`02 §6.1` 第 5 條（量測位置）不過 |
| 血量存取虛擬化（動 `CombatTargetBehaviour` 基底） | 兩本帳合一必須有單一讀寫收斂點，否則寫入點 9 個散在兩個類別 | 只在 `RuneWall` 攔 `ReceiveDamage`：`ReceiveDamage` 非 virtual，且 `IsAlive`／血條仍讀舊帳，帳還是兩本 |

## 5. 端到端驗證步驟（凍結）

每條括號＝「什麼樣的壞實作會讓這條變紅」。數值一律讀 tuning，不得在測試裡寫死寬鬆常數。

**V1** `bash Tools/DotnetCheck/verify.sh` 全數通過：既有純邏輯測試（現 176 個測試方法中屬 EditMode 的那批）一個不少且 0 失敗、compile-only 0 error、紅線 grep 全過。（`Core/Logic` 新檔用了 UnityEngine／Linq；`Contracts` 型別漏進純邏輯；石牆用了 `NavMeshObstacle`）

**V2** 新增純邏輯測試全綠，至少涵蓋：
- a. **授予規則真值表**（`ShouldGrantOnMeleeKill`）：(killed,wall,owner=Red,attacker=Blue)→true；owner=Neutral→true；**owner=Blue→false**；killed=false→false；wall=false→false；ownerKnown=false→false。（把比較改成恆真；把「未死亡」也放行；fail-open）
- b. **護盾**：`Grant` 後 `Amount==150`、`RemainingSeconds==2.5`；Tick 2.49s 仍 `IsActive`、2.50s 後 `Amount==0`；**重複 Grant**（剩 0.3s 時再 Grant）→ `Amount==150`（**不是 300**）且倒數回 2.5。（疊加；倒數不刷新；倒數不遞減）
- c. **吸收**：`Absorb(100)` 回 0 且 `Amount==50`；再 `Absorb(80)` 回 30 且 `Amount==0`；`IsActive==false` 時 `Absorb(50)` 回 50。（護盾不扣；超額傷害被吞掉）
- d. **排序**：`SortByDistance` 對逆序、同距離、單筆、0 筆皆正確且同距離取 Id 小者。（不排序；反向）
- e. **每發只算一次**：同一 `wallId` 連續 `RecordPenetration(id, 0.85)` 兩次 → `DamageMultiplier == 0.85`（不是 0.7225）；不同 id 兩次 → `0.7225`。（`HasPenetrated` 恆假；倍率覆寫而非連乘）
- f. `StepLength(20, 1/30f)` ≈ 0.6667 > 0.6；`StepLength(20, 1/120f)` ≈ 0.1667 < 0.6（把 §4-5 的推導釘成測試）。
- g. **挑選**：`SelectNearestAcceptable` —— 己方牆最近、地板次之 → 回地板；敵方牆最近 → 回敵方牆；全是己方牆 → −1；同距離取索引小者；count=0 → −1。（不跳過己方牆；跳過後取最遠）
- h. `RuneWallLogicTests` 既有 5 條**原封不動**全綠（它們是合帳後單一事實來源的基準）。

**V3** `python Tools/DotnetCheck/mutation_check.py`：既有全部（已知良好狀態當下的筆數，v0.4.1 為 56）仍被抓到；新增 ≥12 筆全部被抓到，至少含——授予規則比較恆真／未死亡也給盾／非牆也給盾／`ownerKnown=false` 改 fail-open／`Grant` 改疊加／倒數不遞減／`Absorb` 不扣護盾／`SortByDistance` 反向／`HasPenetrated` 恆假／倍率覆寫不連乘／`SelectNearestAcceptable` 不跳過己方牆／取最遠。**已知涵蓋缺口（明寫）**：`mutation_check.py` 只能打到 `Core/Logic` 與 `Input` 的逐檔清單（`PureLogic.Tests.csproj:14-20`），**碰不到 `RuneWall.cs`／`CombatTargetBehaviour.cs`**，合帳本身只能靠 V4-a/b 把關。

**V4** Unity batchmode `-runTests`：EditMode 全綠；PlayMode **既有 176 個測試方法中除下述一條外全部原封不動全綠**，另加且全綠：
- a. **合帳（近戰）**：對一面存活的己方符印牆連續 `ReceiveDamage(60, Physical, hero)`（＝`AttackDamage`）→ 每一下之後 `RuneWallLogic.Health` 與 `((ICombatTarget)wall)` 的 `Health` 逐值相同（300→240→…）；第 5 下牆死、`Collider.enabled==false`、名冊釋放名額（可再放 2 面）。（近戰仍扣舊帳＝兩本數字會分岔；死亡時序被改壞）
- b. **合帳（穿透＋近戰交互）**：牆先被子彈穿 2 次（血 300→240），再近戰 3 下（240→60）→ 第 4 下才死。**對照**：合帳前的碼會在近戰第 5 下才死。（合帳沒做完：兩條傷害管道各扣各的）
- c. **合帳不得污染別的目標**：木樁與 `TestWall_A` 的受擊、死亡、重生、血條（`TargetOverheadDisplay`）行為與 v0.4.1 逐值相同（既有 `GreyboxSmokeTests`／`WallDetourPlayTests` 全綠即為證，另加一條顯式斷言木樁 `HealthNormalized` 序列）。
- d. **壽命到期仍走完整死亡流程**：己方牆放置後靜置至壽命到期 → `Collider.enabled==false`、`OnDied` 恰 1 次、名冊名額已釋放。（合帳後 `ForceKill` 的 `if (!IsAlive)` 提早 return，`HandleDeath` 不再被呼叫 → 牆永遠擋路、名額永遠不還；`RuneWall.cs:76,107-115` 是這個陷阱的所在）
- e. **點擊（己方牆穿透）**：英雄與地板之間隔一面**己方**牆，對牆的螢幕座標呼叫 `PlayerInputService.OnWorldTap` → 送出 `OnMoveDestinationSelected`（且落點在牆後地板）、**未**送出 `OnCombatTargetSelected`。
- f. **點擊（敵方／中立可點）**：同樣手法對除錯鈕生成的紅隊牆、對 `TestWall_A` → 送出 `OnCombatTargetSelected` 且目標正是該面牆。（只做了跳過、忘了放行；或把所有牆一律跳過＝回到 v0.4.1）
- g. **護盾（正向）**：英雄走到 `TestWall_A`（中立）旁下攻擊指令 → 牆碎當幀 `shield.Amount==150`、`RemainingSeconds` 在 (2.4, 2.5]；2.6s 後 `Amount==0`。
- h. **護盾（己方牆 0 盾）**：測試直接呼叫 `hero.ResolveAttackHit(ownWall)`（＝大腦真正呼叫的那條路徑，`HeroController.cs:182`）把己方牆打碎 → `shield.Amount==0`。（陣營比較恆真）
- i. **三條不得給盾**：①己方牆壽命到期 ②己方牆被友軍子彈穿到崩解（走真實路徑：放在彈道上，第 7 發穿透崩解，見 k）③放第 3 面牆擠掉最舊那面 → 三者之後 `shield.Amount==0`；且**中立牆被子彈打碎**（l 的 `TestWall_B` 持續挨打至死）之後 `shield.Amount==0`。（把護盾掛在 `OnDied`／`HandleDeath` 上；或讓遠程也吃近戰特權）
- j. **護盾刷新不疊加（實機路徑）**：連砸兩面中立牆（`TestWall_A`、`TestWall_B`）→ `Amount` 始終 ≤150。
- k. **穿透與衰減（己方牆，正式射速 T=0.25s）**：先在砲台彈道 (z=6, +X) 上距砲台 ≤3m 處放一面己方牆、**再**開砲台 → 逐發斷言：第 1～5 發木樁各受 20、**第 6 發受 17**（×0.85）；牆 `CurrentPenetrationCount` 逐發 +1、`Health` 每發 −30（270/240/210/180/150/120）、每發 `RemainingLifespan` 比「同一時刻未被穿透的對照值」少 0.5s（容差 0.05s）；**第 7 發穿透時牆崩解**（`Collider.enabled==false`、`OnDied` 恰 1 次），第 7 發對木樁的傷害以 `RuneWallLogic.TryPenetrate` 既有語意為準（測試用一個全新的 `RuneWallLogic` 跑同一序列取得期望倍率，不寫死）；整段不得出現第 8 次穿透。（子彈被己方牆擋下；倍率沒套到最終傷害；壽命成本沒扣；第 6 發仍 ×1.0；把 `UndecayedPenetrations` 當成 0 或 10）
- l. **擋下（敵方／中立牆）**：彈道上放一面敵方牆（除錯鈕）／`TestWall_B` → 木樁血量在 2s 內不變、該牆 `Health` 每發少 20。（敵方牆不擋；擋了卻不扣血）
- m. **每發只算一次（實機）**：一發子彈通過一面己方牆後，該牆 `CurrentPenetrationCount` 增量恰為 1（即使子彈在牆內跨了多幀）。（每幀重複計數 → 一發就吃掉 3~4 次額度）
- n. **不穿隧**：把 `Time.captureDeltaTime` 設成 1/20s（Δs＝1.0m > 牆厚 0.6m）跑 20 發 → 每一發都被登記（穿透數＋擋下數 == 發射數）。**對照**：把掃掠改成「只查當幀端點」的樸素實作，同條件會漏。（點查詢；掃掠起點用當幀位置而非上一幀）
- o. **敵方牆不佔己方名冊**：連按除錯鈕 3 次後，玩家仍能放滿 2 面己方牆，且己方第 3 面擠掉的是己方最舊那面、不是敵方牆。（`FindObjectsOfType` 整批當池）
- p. **砲台／子彈不改變既有物理**：砲台無 `Collider`；子彈不進 NavGrid（`BlockGrid.BlockedCount` 在整段量測中只隨牆變動）；英雄站在彈道上不會被子彈打到（英雄不是 `ICombatTarget`）。
- r. **砲台預設關閉**：載入場景後靜置 3s → 發射數＝0、木樁血量＝600 不變；`SetFiring(true)` 後 1s 內發射數 ≥3；`SetFiring(false)` 後不再發射、已在飛的子彈照常結算。（砲台常開＝污染既有測試與 Phase 1 試玩）
- q. **HUD**：`TURRET` 鈕切換 OFF／ON 且文字跟著變；ENEMY WALL 鈕按下 → 場上多一面紅色牆；護盾列在取得護盾後顯示 150 並隨倒數遞減至 0。

**V5** 零配置：`ZeroAllocationTests.Combat_UpdateAndLateUpdate_AllocateNothing` 的**同一個量測窗口**內另外發生並各自斷言次數 ≥1（活性，比照既有 `runeDriver.Casts` 模式）：（Driver 在窗口內自行 `SetFiring(true)`）砲台開火 ≥2、子彈穿透己方牆 ≥1、子彈被牆擋下 ≥1、護盾取得 ≥1 且護盾條 Renderer 可見、`OnWorldTap` 命中己方牆後方地板 ≥1。`UpdateBytes==0`／`LateUpdateBytes==0` 門檻、`Frames` 下限與既有正向對照**一字不動**。新行為一律由夾區內元件自己的 `Update()` 觸發（`ZeroAllocationTests.cs:67-79` 的 ±32000 探針夾區、`:115-117` 的 H2 約束）；護盾條必須在同一顆 Scene、不得 `DontDestroyOnLoad`。

**V6 範圍與既有測試衝擊**：`git diff --stat <已知良好狀態>..` 逐檔對應 §1；§3 點名的五個手感核心檔與 `HeroController.cs` 零改動；全部 asmdef 零改動（PlayMode 測試 asmdef 已含 `Vow.Core`／`Vow.Combat`／`Vow.Bootstrap`，新元件放 `Vow.Combat` 即可，**不得為了測試加引用**）；`grep -n "_health" Assets/` 只在 `CombatTargetBehaviour.cs` 命中（§2 的分母收斂）。既有測試**逐條讀過（176 個方法）**，需要調整的只有下列 1 條，其餘 175 條零改動：
- `Assets/Tests/PlayMode/RuneWallPlayTests.cs:277-311` `Wall_IsInvisibleToWorldTapRaycast_ButStillBlocksTheHeroPhysically`。**改前**（`:294-298`）：裸 `Physics.Raycast(..., DefaultRaycastLayers)` 後斷言 `GetComponentInParent<RuneWall>() == null`——它量的是「符印牆在不在層 2」這個實作手段。**改後**：改走 `PlayerInputService.OnWorldTap` 的真實路徑，斷言「點己方牆 → 收到 `OnMoveDestinationSelected`、未收到 `OnCombatTargetSelected`」（即 V4-e），同方法後半段的物理阻擋斷言（`:300-310`）一字不動。**為什麼不是移動及格線**：要守的行為（點自家牆等於點到牆後地板、牆照樣擋身體）逐字不變，只是把量測位置從實作手段移到使用者真的會經歷的路徑（`02 §6.1` 第 5 條）；且新斷言**更嚴**——舊版只要牆在層 2 就過，新版要求陣營判斷正確，並由 V4-f 補上舊版沒有的反向（敵方牆必須點得到）。此外 `WallDetourPlayTests.cs:255` 有一行「牆在 Ignore Raycast 層」的**背景註解**（非斷言）會過時，只改註解、零斷言改動。

**V7** fresh-context opus 對抗審查（prompt 必含：合帳後是否還有第二處讀寫石牆血量；壽命到期／穿透耗死／擠掉三條路徑是否仍完整走 `HandleDeath`；`RaycastNonAlloc` 緩衝溢位與 `QueryTriggerInteraction` 是否被改掉；陣營比較是否在單機恆假而測試靠治具偽造；測試裡的數字常數是否與 §4-10 一致；是否有測試放寬既有容差）：CRITICAL／HIGH 全修或經使用者簽准；修完三態覆審，上限 3 輪。

**V8** 送達與實機：`git log origin/gh-pages -1` 顯示 v0.5.0；線上首頁版本列＝0.5.0；Playwright（含 `hasTouch` 手機模擬）開線上網址——①先確認不按 `TURRET` 時木樁血條不動；按下後截圖可見砲台射出子彈打到木樁、木樁血條下降 ②放一面己方牆在彈道上，截圖可見子彈穿過、牆血條下降 ③按 ENEMY WALL 生紅牆、點它、英雄走過去砸碎 → 截圖可見護盾條與 HUD 數值 150 並在 2.5s 內歸零 ④點自家牆，英雄走到牆後（不砸牆）⑤console 無 error／exception。

## 6. 風險與未決（主對話審稿後）

- **Q1 射速（已由主對話自決，可逆暫定值、試玩即改）**：起草時發現原暫定 1 發/s 會讓第 6 發起的 ×0.85 衰減永遠看不到，且第 4 發落在壽命歸零的刀鋒上（§4-4⑤）。定為 **0.25 s/發**＋**砲台預設關閉、HUD 開關**（§4-7）。附帶發現：`MaxPenetrations=10` 在任何 T>0 都不可達，是冗餘判斷式——批 3 不得寫以它為內容的驗收條文；規格數值不動，記入驗收指南。
- **Q2（已知限制，不處理）**：中立測試牆每 6s 重生（`TestWallTarget.cs:13`）＝可重複取得護盾。灰盒沒有英雄血量也沒有對手，刷盾無利可圖；正式地圖的中立牆不會 6 秒重生。寫進驗收指南 §13 已知限制。
- **Q3（已知限制，照裁定 2）**：線上版驗不到「護盾吸收傷害」，`Absorb` 只有純邏輯測試背書；不加「砲台射英雄」除錯模式（會提前引入英雄血量）。
- **R3-1 的另一半（已知限制，批 3 覆審要點名複查）**：替代點快取的 key 已含連續目的地座標（R13），但 R1a 的路徑成本以英雄格為源——英雄自己走路也可能讓答案改變（r3 覆審隨機盤面 84 盤中 17 盤、最大 1.414m）。把英雄位置放進 key 會讓快取每幀失效（V11-c 退回），所以沒做；批 3 的砲台與敵方牆不會動、不是追擊目標，預期仍踩不到，有實際症狀再評估。
- 目前**沒有**需要使用者再裁定的題目。

> 本節以外的所有選擇（§4 各項、§2 簽名、§5 條文）皆為可逆的實作決定，依 `03 R3` 自決並已在本檔載明。

## 7. r1 對抗審查後的修訂（2026-09-19；報告 `vow-toolchain/REVIEW-p2b3-r1.md`，標的 `f9c2b35`：CRITICAL 1／HIGH 3／MEDIUM 7／LOW 5）

全部是**加嚴或還原**，沒有任何一條放寬 §5。修完送三態覆審（這是第 1 輪，上限 3 輪）。

### R1（CRITICAL-1）ENEMY WALL 鈕在兩面敵方牆都活著時沒有反應
- 成因：敵方池 2 面／名冊 cap 2，`FindFreeSlot()` 只回已死的格 → 池滿時 `Spawn()` 回 null，§4-6 的「FIFO 擠掉最舊」是死碼。修法：敵方池比照玩家池改 **3 面／名冊 cap 2**（§4-6、§4-10 的「敵方池 2 面」語意＝同時存活上限 2）。
- **V4-o 加嚴**：記下第 1 次生出來的那面；連按 3 次之後：第 3 次 `Spawn()` 回傳非 null、第 1 面 `IsAlive==false`、存活數＝2、第 3 面在英雄正前方 4m。連按 6 次（間隔 1 幀）每次都回傳非 null。紅燈條件：`f9c2b35` 的碼（第 3 次回 null）。原本的 `AliveCount()==2` 斷言保留。

### R2（HIGH-1）`ZeroAllocationTests` 的 `deadline` 還原成 `20f`
- 覆審實測量測窗口 10.03s 跑完，`20f` 跑得完 → `40f` 是不必要的放寬，**還原**，連同被刪掉的那行註解。之後窗口若因 R3 變長而真的跑不完，回報主對話走 `02 §2.1`，不得自行改。

### R3（HIGH-2）V5 的護盾活性要落在探針夾區之內
- 成因：授予由動畫事件驅動，落在 `AllocationProbeEnd.Update` 與 `AllocationProbeBegin.LateUpdate` 之間，不在夾區 → 在 `Grant()` 塞配置測試照樣綠。
- 修法：窗口內的 Driver 在自己的 `Update()` 直接行使護盾授予路徑（`RockShieldBehaviour` 的授予入口，含 `Grant()` 與護盾條顯示更新），次數 ≥2，並保留既有「真實大腦路徑授予 ≥1」的活性。**鑑別力證據（必附）**：在 `RockShieldBehaviour.Grant()` 注入一個每次配置 → 零配置測試必須紅；注入後還原（用備份副本重寫）。
- 已知限制（寫進驗收指南 §13）：動畫事件階段（近戰命中結算、真實路徑的護盾授予處理常式）自批 1 起就不在 Update／LateUpdate 夾區內，零配置量不到；本批不改探針結構（測試協程本體也落在那個階段，會污染量測）。

### R4（HIGH-3）V4-m 換成能紅的量法
- 成因：Unity 射線不回報「起點在其內部」的 Collider，單一 BoxCollider 的牆不可能被同一發子彈回報兩次 → 原 V4-m 恆真，`Projectile.cs` 的 `HasPenetrated` 守衛在任何一層都沒有防線。
- **新增 V4-m2**：測試替一面己方牆動態加第二個 `BoxCollider`（沿彈道方向錯開、同屬這面牆且解析得到同一個 `RuneWall`）→ 一發子彈通過後 `CurrentPenetrationCount` 增量恰為 1、木樁受到的傷害＝子彈傷害×單次倍率（不是連乘兩次）。**必附紅燈**：拿掉 `Projectile.cs` 的守衛 → 增量 2。原 V4-m 保留並在註解寫明它對守衛零鑑別力。若做不出「第二個 Collider 解析到同一面牆」，回報主對話，不得改成別的較寬的量法。

### R5（MEDIUM／LOW，一併處理）
- M4／L1：符印牆與敵方牆加上既有的 `TargetOverheadDisplay` 血條（SceneBuilder 編輯期預建、零配置；V8-② 的「牆血條下降」才量得到）。不得影響 V5 的 0 bytes。新增 PlayMode 斷言：己方牆被穿透一次後血條比例＝270/300。
- M5：子彈掃掠緩衝溢位時 `Debug.LogWarning`（比照點擊路徑）。
- M7：新增 PlayMode 斷言——同一面池牆歷經「近戰打死→再啟用→壽命到期→再啟用→被擠掉」之後，`BlockGrid.NegativeStampCount==0` 且所有牆消失後 `BlockedCount` 回到開場基線。
- M2：新增一條走真實輸入分流的測試——用 `TURRET`／`ENEMY WALL` 鈕矩形內的螢幕座標送觸控（經 `InputRoutingManager`），砲台被打開／生出紅牆，且**沒有**送出移動指令。做不到（asmdef 看不到）就回報，不得加 asmdef 引用。
- M1：`ZeroAllocationTests` 把 `FindObjectsOfType<RuneWall>()` 換成 `caster.Pool` 在此補授權——批 3 之後前者會把敵方池掃進來；語意不變＝「玩家池的牆」。
- M6（記錄不修）：牆在子彈已進入牆體之後才啟用時，那一發不被擋也不記穿透——只影響除錯砲台的觀感，寫進驗收指南 §13 已知限制。L2／L3／L5 記錄不修；L4 的 V4-h① 保留並加註解「本半條恆真，鑑別力在②」。

## 8. r2 三態覆審結果（2026-09-20；報告 `vow-toolchain/REVIEW-p2b3-r2.md`，標的 `9194b51`）
- 三態：真的修好 10／表面修好 1（L4）／沒修到 1（M3）；新 finding：CRITICAL 0／HIGH 0／MEDIUM 3／LOW 5。r1 的 CRITICAL-1、HIGH-1／2／3 四條覆審員各自重做突變／注入實驗，全部紅在行為斷言。凍結判準未被移動（`e5768c4..9194b51` 的測試刪除行 10 行，全在 V6／§7 範圍）。
- **MEDIUM-N1（已修，主對話）**：`EnemyWallSpawner.Initialize` 在池的物件數 ≤ 同時存活上限時 `Debug.LogError`；測試 `N1_EnemyWallSpawner_WithAPoolNotLargerThanTheAliveCap_LogsAnError`，沒有這段防呆時紅在 `Expected log did not appear`（`vow-toolchain/unity-p2b3-n1-red.xml`）。
- **r1 M3（記錄不修，r2 指出 §7 漏記）**：`DebugHud.OnGUI` 的 IMGUI 路徑（含護盾數值列）結構上不在 Update／LateUpdate 零配置夾區內；現行實作用 `IntStringCache` 與常數字串、讀碼無每幀配置，但沒有機械防線。除錯 HUD 不進正式版，記入驗收指南 §13 已知限制。
- **MEDIUM-N3／r1 M1：`ZeroAllocationTests` 的石牆池來源 `FindObjectsOfType<RuneWall>()`→`caster.Pool`——依 `02 §2.1` 例外條款自行修正、事後回報使用者**。原標準錯在哪：該測試把找到的 `RuneWall` 整批當成「玩家池」重新交給 `RuneCaster.Initialize`；批 3 依 §4-6 新增了同為 `RuneWall` 型別的敵方牆池，原寫法會把敵方牆塞進玩家池。為什麼現在才知道：批 3 之前場上只有玩家池一種 `RuneWall`。主對話實測（`9194b51` 只還原這一行）：完整 PlayMode 64／65，該測試紅在「量測期間從未進入 Follow 轉向（實測 0 幀）」＝治具壞掉，與受測實作對錯無關（`vow-toolchain/unity-p2b3-m1probe-play.xml`）。修正後語意不變＝「玩家池的牆」；門檻 `UpdateBytes==0`／`LateUpdateBytes==0`／`Frames>=240`、deadline `20f`、正向對照一字未動。
- MEDIUM-N2（記錄不修）：零配置窗口裡「等真實授予」的中途閘被 Driver 每幀 `Grant()` 蓋過，只剩結尾 `GrantCount 增量 >= DriverGrants + 1` 在守（覆審實測關掉真實路徑會紅在 61 vs 62）；潛在假警報，覆審 3 次＋主對話 2 次完整 PlayMode 未觀察到波動。LOW-N4～N8 與 L4 的「回傳內部陣列」記錄不修（只為驗收開的三個生產面 `SendScreenTap`／`RefreshColliderCache`／`TargetRegistry` 生產碼零呼叫點；場邊按 ENEMY WALL 會把牆生到場外、不進格點；己方／敵方牆血條同色）。
