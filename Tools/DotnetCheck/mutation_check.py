# -*- coding: utf-8 -*-
"""突變檢查：證明純邏輯測試有鑑別力——把實作故意改壞，對應的測試必須變紅。

用法（repo 根目錄）： python Tools/DotnetCheck/mutation_check.py
需要 .NET 8 SDK（預設找 %LOCALAPPDATA%\\Microsoft\\dotnet\\dotnet.exe，否則用 PATH 上的 dotnet）。

每個突變：改檔 → 完整重編 → 跑測試 → 記下變紅的測試 → 還原。
還原一律「重寫原內容」而不是複製備份檔：保留舊修改時間會讓 MSBuild 增量編譯沿用突變後的 binary 而誤報。
結束碼：全部突變都被抓到且還原後全綠 = 0，否則 = 1。
"""
import io
import os
import re
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PROJ = os.path.join(ROOT, "Tools", "DotnetCheck", "PureLogic.Tests", "PureLogic.Tests.csproj")
LOGIC = "Assets/Scripts/Core/Logic/"
INPUT = "Assets/Scripts/Input/"

_local = os.path.join(os.environ.get("LOCALAPPDATA", ""), "Microsoft", "dotnet", "dotnet.exe")
DOTNET = _local if os.path.isfile(_local) else "dotnet"
ENV = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", DOTNET_CLI_UI_LANGUAGE="en")

# (代號, 說明, 檔案, 原文, 改壞後, 至少要變紅的測試)
MUTATIONS = [
    ("M1", "衰減表失效（三次都滑 1.4m）", LOGIC + "CombatTuning.cs",
     "public float[] DashDistances = { 1.4f, 0.9f, 0.5f };",
     "public float[] DashDistances = { 1.4f, 1.4f, 1.4f };",
     "ThreeChainedDashes_Decay_1_4_Then_0_9_Then_0_5_Total_2_8"),
    ("M2", "切後搖可縮短攻擊週期（紅線 2）", LOGIC + "HeroCombatBrain.cs",
     "if (_body.TryBeginCadenceDash(dirX, dirZ)) SetState(PlayerState.CadenceDashing);",
     "if (_body.TryBeginCadenceDash(dirX, dirZ)) { SetState(PlayerState.CadenceDashing); _nextAttackReadyTime = _clock; }",
     "DashCancellingEveryHit_NeverShortensAttackPeriod"),
    ("M3", "預輸入緩衝沒有 120ms 時限", LOGIC + "HeroCombatBrain.cs",
     "if (_clock - _bufferedFlickTime <= _tuning.InputBufferSeconds)", "if (true)",
     "FlickOlderThan120msBeforeHit_IsDiscarded"),
    ("M4", "收招／滑步中拒收移動指令（卡刀，紅線 1）", LOGIC + "HeroCombatBrain.cs",
     "                case PlayerState.AttackRecovery:\n                case PlayerState.CadenceDashing:\n"
     "                    SetPendingMove(destination);\n                    break;",
     "                case PlayerState.AttackRecovery:\n                case PlayerState.CadenceDashing:\n"
     "                    break;",
     "MoveCommand_IsHonouredFromEveryPhase1State"),
    ("M5", "狀態機放行所有轉換", LOGIC + "PlayerStateMachine.cs",
     "return (AllowedMask[f] & (1 << t)) != 0;", "return true;",
     "IllegalTransitions_AreRejected_StateAndEventsUntouched"),
    ("M6", "0 充能仍可滑步", LOGIC + "CadenceSim.cs",
     "if (state.Charges <= 0)\n                {", "if (state.Charges < -99)\n                {",
     "Charges_CapAtThree_RejectAtZero_RecoverEvery2_5s"),
    ("M7", "邊緣死區不含底邊", LOGIC + "GestureMath.cs",
     "return x < marginPixels || x > screenWidth - marginPixels || y < marginPixels;",
     "return x < marginPixels || x > screenWidth - marginPixels;",
     "EdgeDeadzone_RejectsLeftRightBottom_Within8px"),
    ("M8", "射程內改鎖會重新起手（審查 r1 M-5）", LOGIC + "HeroCombatBrain.cs",
     "                        _currentTarget = target;\n                        _body.FaceTarget(target);\n                        return;",
     "                        _currentTarget = target;\n                        EngageCurrentTarget();\n                        return;",
     "AlternatingBetweenTwoTargets_DoesNotStallTheAttack"),
    ("R1", "切模式時直接釋放槽位（審查 r1 M-1）", INPUT + "TouchGestureRouter.cs",
     "                    if (!_slotUsed[i]) continue;\n                    _slotRoute[i] = TouchRoute.Rejected;\n"
     "                    _trackers[i].Cancel();",
     "                    _slotUsed[i] = false;",
     "SwitchingMode_WhileAFingerIsStillDown_DoesNotConjureAWorldTap"),
    ("R2", "只記得一根已結束手指（審查 r1 M-2）", INPUT + "TouchGestureRouter.cs",
     "private const int EndedHistorySize = MaxTouches;", "private const int EndedHistorySize = 1;",
     "TwoFingersEndingInTheSameFrame_NeitherIsReplayedAsANewTap"),
    ("R3", "微輪盤離手補發點擊（高壓紅線）", INPUT + "TouchGestureRouter.cs",
     "                case TouchRoute.Pip:\n                    _pip.End();\n                    break;\n\n"
     "                case TouchRoute.UiRegion:",
     "                case TouchRoute.Pip:\n                    _pip.End();\n                    _sink.OnWorldTap(x, y);\n"
     "                    break;\n\n                case TouchRoute.UiRegion:",
     "ModeB_PipNeverProducesAWorldTap_NoMatterWhatTheThumbDoes"),
    ("R4", "不回收消失的手指（槽位洩漏）", INPUT + "TouchGestureRouter.cs",
     "if (_slotUsed[i] && !_slotSeen[i]) CancelTouch(i);", "if (false) CancelTouch(i);",
     "FingerVanishingWithoutEnded_FreesItsSlot_SoSlotsNeverLeak"),
    ("N1", "延遲注入形同虛設（事件立即到期）", LOGIC + "DelayedEventQueue.cs",
     "double due = now + (delaySeconds > 0.0 ? delaySeconds : 0.0);", "double due = now;",
     "Event_IsNeverDeliveredBeforeItsDelayElapses"),
    ("N2", "延遲佇列後進先出（後發的指令先到）", LOGIC + "DelayedEventQueue.cs",
     "            item = _items[_head];\n            _items[_head] = default; // 釋放可能持有的物件引用\n"
     "            _head = (_head + 1) % _items.Length;\n            _count--;\n            return true;",
     "            int last = (_head + _count - 1) % _items.Length;\n            item = _items[last];\n"
     "            _items[last] = default;\n            _count--;\n            return true;",
     "Order_IsPreserved_EvenWhenALaterEventDrawsAShorterDelay"),
    ("N3", "延遲佇列滿了就丟事件（吃指令）", LOGIC + "DelayedEventQueue.cs",
     "                evicted = _items[_head];\n", "",
     "WhenFull_TheOldestEventIsHandedBack_NothingIsEverDropped"),
    ("H1", "重震也被疲勞節流", LOGIC + "HapticFatiguePolicy.cs",
     "if (cue == HapticCue.CadenceDash || cue == HapticCue.WallBreak) return HapticStrength.Heavy;",
     "if (cue == HapticCue.WallBreak) return HapticStrength.Heavy;",
     "DashAndWallBreak_AreAlwaysHeavy_NeverThrottled"),
    ("H2", "普攻輕震永不靜音", LOGIC + "HapticFatiguePolicy.cs",
     "return _consecutiveBasicHits <= LightPulsesBeforeMute ? HapticStrength.Light : HapticStrength.None;",
     "return HapticStrength.Light;",
     "BasicAttacks_BuzzLightlyThreeTimes_ThenGoQuiet_UntilThePlayerRests"),
    ("H3", "停手後疲勞不重置", LOGIC + "HapticFatiguePolicy.cs",
     "if (_hasHitBefore && now - _lastBasicHitTime >= ResetAfterIdleSeconds) _consecutiveBasicHits = 0;", "",
     "BasicAttacks_BuzzLightlyThreeTimes_ThenGoQuiet_UntilThePlayerRests"),
    ("U1", "符印輕點滲透成點地（Phase 2：阻斷符印事件向下滲透）", INPUT + "TouchGestureRouter.cs",
     "                case RuneGestureOutcome.QuickCast: _sink.OnRuneQuickCast(); break;",
     "                case RuneGestureOutcome.QuickCast: _sink.OnRuneQuickCast(); _sink.OnWorldTap(0f, 0f); break;",
     "TapOnRune_QuickCastsExactlyOnce_AndNeverReachesTheWorld"),
    ("U2", "符印區不分流（手指落到世界層）", INPUT + "InputRoutingManager.cs",
     "            if (_hasRuneZone && _runeZone.Contains(x, y))\n                return TouchRoute.Rune;\n", "",
     "TapOnRune_QuickCastsExactlyOnce_AndNeverReachesTheWorld"),
    ("U4", "滑回原點鬆手仍然成牆", LOGIC + "RuneGestureTracker.cs",
     "            if (InsideOrigin) return RuneGestureOutcome.Cancelled;\n",
     "",
     "DragThenSlideBackToOrigin_Cancels"),
    ("U13", "滑回原點時不亮取消旗標（虛影不會提示玩家此刻放手＝取消）", LOGIC + "RuneGestureTracker.cs",
     "public bool CancelArmed => Held && Dragging && InsideOrigin;",
     "public bool CancelArmed => false;",
     "DragThenSlideBackToOrigin_Cancels"),
    ("U5", "還沒拖曳就被系統中斷卻放出極速石牆", LOGIC + "RuneGestureTracker.cs",
     "return Dragging ? RuneGestureOutcome.Cancelled : RuneGestureOutcome.None;",
     "return Dragging ? RuneGestureOutcome.Cancelled : RuneGestureOutcome.QuickCast;",
     "SystemCancelDuringDrag_Cancels_AndBeforeDragStaysSilent"),
    ("U6", "符印可被兩根手指同時持有", INPUT + "TouchGestureRouter.cs",
     "if (_rune.Held) _slotRoute[slot] = TouchRoute.Rejected; // 符印一次只認一根手指\n                    else _rune.Begin(touchId, x, y, now);",
     "_rune.Begin(touchId, x, y, now);",
     "WhileRuneIsHeld_SecondFingerOnRuneIsIgnored_AndWorldTapsStillWork"),
    ("W1", "石牆壽命不倒數（永久水泥牆）", LOGIC + "RuneWallLogic.cs",
     "            RemainingLifespan -= deltaSeconds;\n", "",
     "Wall_LivesForFiveSeconds"),
    ("W2", "第 6 發以後穿透仍不衰減", LOGIC + "RuneWallLogic.cs",
     "damageMultiplier = PenetrationCount <= _tuning.UndecayedPenetrations ? 1f : _tuning.DecayedDamageMultiplier;",
     "damageMultiplier = 1f;",
     "Penetration_FirstFiveUndecayed_NextFiveAt85Percent_EachShotWearsTheWall"),
    ("W3", "友軍穿透不損耗石牆（無解的自閉陣地）", LOGIC + "RuneWallLogic.cs",
     "            Health -= MaxHealth * _tuning.PenetrationHealthFraction;\n", "",
     "Penetration_FirstFiveUndecayed_NextFiveAt85Percent_EachShotWearsTheWall"),
    ("W4", "全隊石牆上限變 3 面", LOGIC + "RuneCastLogic.cs",
     "_slots = new int[cap < 1 ? 1 : cap];",
     "_slots = new int[cap + 1];",
     "Roster_ThirdWallEvictsTheOldest"),
    ("W5", "已消失的牆仍佔名額", LOGIC + "RuneCastLogic.cs",
     "                RemoveAt(i);\n                return;", "                return;",
     "Roster_ExpiredWallsFreeTheirSeat"),
    ("W6", "冷卻失效（石牆可連放）", LOGIC + "RuneCastLogic.cs",
     "_readyTime = now + _tuning.CooldownSeconds;", "",
     "Cooldown_BlocksForEightSeconds_ThenReadyAgain"),
    ("W7", "查詢落點也會吃掉冷卻（取消施法被罰冷卻）", LOGIC + "RuneCastLogic.cs",
     "            return TryPlace(posX, posZ, dirX, dirZ, distance, out placement);",
     "            _readyTime = 1e9; return TryPlace(posX, posZ, dirX, dirZ, distance, out placement);",
     "WithoutTryBeginCast_NothingIsConsumed"),
    ("W8", "拉伸量不夾住（牆被丟到射程外）", LOGIC + "RuneCastLogic.cs",
     "float t = distance01 < 0f ? 0f : (distance01 > 1f ? 1f : distance01);",
     "float t = distance01;",
     "DragCast_MapsStretchToTwoThroughEightMetres"),
    ("L1", "按鈕邊距的 16px 下限被拿掉（極低 dpi 時按鈕貼進邊緣死區旁）", INPUT + "RuneButtonLayout.cs",
     "if (margin < MinEdgeMarginPixels) margin = MinEdgeMarginPixels;", "",
     "Layout_OnAnExtremelyLowDpiScreen_FallsBackToTheSixteenPixelFloor"),
    ("W9", "貼身帶失效（貼身放牆又得把手指停在取消圈邊緣）", LOGIC + "RuneCastLogic.cs",
     "if (u < 0f) u = 0f;",
     "u = t;",
     "DragCast_NearBand_HoldsTheMinimumDistanceForTheFirstThirdOfTheStroke"),
    ("U7", "短促輕點的拇指滾動被當成拖曳（按了沒反應）", LOGIC + "RuneGestureTracker.cs",
     "if (InsideOrigin && time - StartTime <= tapMaxSeconds && MaxReach <= tapSlopPx) return RuneGestureOutcome.QuickCast;", "",
     "QuickTapWithThumbRoll_IsStillAQuickCast"),
    ("U8", "只看時間不看位移：快速甩出的拖曳被吞成輕點", LOGIC + "RuneGestureTracker.cs",
     "if (InsideOrigin && time - StartTime <= tapMaxSeconds && MaxReach <= tapSlopPx) return RuneGestureOutcome.QuickCast;",
     "if (InsideOrigin && time - StartTime <= tapMaxSeconds) return RuneGestureOutcome.QuickCast;",
     "ThumbRollForgiveness_DoesNotSwallowDeliberateGestures"),
    ("U10", "快速短距離的方向施放被吞成正前方的極速石牆（方向丟失）", LOGIC + "RuneGestureTracker.cs",
     "if (InsideOrigin && time - StartTime <= tapMaxSeconds && MaxReach <= tapSlopPx) return RuneGestureOutcome.QuickCast;",
     "if (time - StartTime <= tapMaxSeconds && MaxReach <= tapSlopPx) return RuneGestureOutcome.QuickCast;",
     "QuickShortAimedDrag_KeepsItsDirection"),
]


def write_text(path, text, crlf):
    if crlf:
        text = text.replace("\n", "\r\n")
    io.open(path, "w", encoding="utf-8", newline="").write(text)


def run_tests():
    build = subprocess.run([DOTNET, "build", PROJ, "--nologo", "--no-incremental", "-v", "q"],
                           capture_output=True, text=True, encoding="utf-8", errors="replace", env=ENV)
    if build.returncode != 0:
        return None, ["<BUILD FAILED>"]
    trx_dir = os.path.join(os.path.dirname(PROJ), "TestResults")
    test = subprocess.run([DOTNET, "test", PROJ, "--nologo", "--no-build", "--logger", "trx;LogFileName=mutation.trx",
                           "--results-directory", trx_dir],
                          capture_output=True, text=True, encoding="utf-8", errors="replace", env=ENV)
    trx = io.open(os.path.join(trx_dir, "mutation.trx"), encoding="utf-8", errors="replace").read()
    failed = sorted(set(re.findall(r'testName="([^"]+)"[^>]*outcome="Failed"', trx)) |
                    set(re.findall(r'outcome="Failed"[^>]*testName="([^"]+)"', trx)))
    total = len(re.findall(r"<UnitTestResult ", trx))
    return total, [name.split(".")[-1] for name in failed]


def main():
    caught = 0
    for code, title, rel, original, broken, must_fail in MUTATIONS:
        path = os.path.join(ROOT, rel)
        raw = io.open(path, encoding="utf-8", newline="").read()
        crlf = "\r\n" in raw  # 記下原檔的行尾格式，還原時照原樣寫回，跑完不弄髒工作區
        source = raw.replace("\r\n", "\n")
        if source.count(original) != 1:
            print("%s  SKIP（找不到唯一的原文，突變定義已過期）：%s" % (code, title))
            continue
        try:
            write_text(path, source.replace(original, broken), crlf)
            total, failed = run_tests()
        finally:
            write_text(path, source, crlf)

        ok = must_fail in failed
        caught += ok
        print("%s  %s  %s -> %d 個測試變紅%s" % (code, "CAUGHT" if ok else "MISSED", title, len(failed),
                                            "" if ok else "（預期 %s 要紅）" % must_fail))
        for name in failed:
            print("        RED %s" % name)

    total, failed = run_tests()
    clean = total is not None and not failed
    print("\n還原後：%s 個測試，%d 個失敗" % (total, len(failed)))
    print("突變被抓到：%d / %d" % (caught, len(MUTATIONS)))
    result = caught == len(MUTATIONS) and clean
    print("RESULT: " + ("ALL MUTATIONS CAUGHT" if result else "FAILED"))
    return 0 if result else 1


if __name__ == "__main__":
    sys.exit(main())
