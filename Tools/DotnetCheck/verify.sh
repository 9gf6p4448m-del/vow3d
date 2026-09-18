#!/usr/bin/env bash
# VOW Phase 1 —— 不需要 Unity Editor 的三段式驗證。
#   1) 純邏輯測試（NUnit，實際執行）
#   2) 全部 Unity 腳本對參考組件 compile-only 檢查（API 名稱／簽章／模組依賴方向）
#   3) 六大紅線靜態掃描
# 一律完整重編（--no-incremental）：增量編譯以檔案修改時間判斷，還原舊檔時會沿用過期的 binary 而誤報。
#
# 前置：.NET 8 SDK；Unity 參考組件目錄（預設 ../vow-toolchain/refs，可用環境變數 UNITY_REFS_DIR 覆寫）。
set -u
cd "$(dirname "$0")/../.."

DOTNET="${DOTNET:-$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe}"
[ -x "$DOTNET" ] || DOTNET="dotnet"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
REFS_ARG=()
[ -n "${UNITY_REFS_DIR:-}" ] && REFS_ARG=("-p:UnityRefsDir=$UNITY_REFS_DIR")

fail=0

echo "=== 1/3 純邏輯測試 ==="
"$DOTNET" build Tools/DotnetCheck/PureLogic.Tests/PureLogic.Tests.csproj --nologo --no-incremental -v q 2>&1 | grep -E " error |個錯誤" | sed 's/\[.*//' | sort -u
"$DOTNET" test Tools/DotnetCheck/PureLogic.Tests/PureLogic.Tests.csproj --nologo --no-build 2>&1 | tail -2
[ "${PIPESTATUS[0]}" -eq 0 ] || fail=1

echo "=== 2/3 Unity 腳本編譯檢查（Vow.Editor 依賴全部模組，編它等於編全部）==="
"$DOTNET" build Tools/DotnetCheck/UnityCompile/Vow.Editor/Vow.Editor.csproj --nologo --no-incremental -v q "${REFS_ARG[@]}" 2>&1 | grep -E " error |個錯誤" | sed 's/\[.*//' | sort -u
[ "${PIPESTATUS[0]}" -eq 0 ] || fail=1

echo "=== 3/3 紅線靜態掃描 ==="
scan() { # 名稱, 期望命中數, 實際命中數
  if [ "$3" -eq "$2" ]; then echo "  PASS  $1 ($3)"; else echo "  FAIL  $1：期望 $2，實際 $3"; fail=1; fi
}
scan "C1 舊版 UnityEngine.Input API" 0 "$(grep -rnE '\bInput\.(GetMouseButton|GetKey|GetAxis|GetButton|touch|touches|mousePosition|GetTouch)' Assets --include=*.cs | wc -l)"
scan "C2 carving = true"            0 "$(grep -rnE 'carving\s*=\s*true' Assets --include=*.cs | wc -l)"
scan "C2 NavMeshObstacle 元件"      0 "$(grep -rn 'NavMeshObstacle' Assets --include=*.cs | grep -v '//' | wc -l)"
scan "C3 TriggerCadenceJam"         0 "$(grep -rn 'TriggerCadenceJam' Assets --include=*.cs | wc -l)"
scan "C4 runtime 使用 System.Linq"  0 "$(grep -rln 'using System.Linq' Assets/Scripts | wc -l)"
scan "C5 Application.targetFrameRate 設定" 1 "$(grep -rn 'Application.targetFrameRate = ' Assets/Scripts --include=*.cs | wc -l)"
scan "C5 InputSystem.pollingFrequency 設定" 1 "$(grep -rn 'InputSystem.pollingFrequency = ' Assets/Scripts --include=*.cs | wc -l)"
scan "Hitstop 不得動 Time.timeScale" 0 "$(grep -rn 'timeScale' Assets/Scripts --include=*.cs | grep -v '//' | wc -l)"

echo
if [ "$fail" -eq 0 ]; then echo "RESULT: ALL PASS"; else echo "RESULT: FAILED"; fi
exit "$fail"
