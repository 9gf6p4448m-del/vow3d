#!/usr/bin/env bash
# VOW WebGL 試玩版：建置 → 檢查產物 → 推上 gh-pages → 核對線上版本。
# 用法： bash Tools/deploy-webgl.sh            （建置＋部署）
#        SKIP_BUILD=1 bash Tools/deploy-webgl.sh （沿用現有 Builds/WebGL，只部署）
# 需要：含 WebGL Build Support 的 Unity 2022.3.62f1（可用環境變數 UNITY_EXE 指定路徑）、git、curl。
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$(pwd -W 2>/dev/null || pwd)"

UNITY_EXE="${UNITY_EXE:-}"
if [ -z "$UNITY_EXE" ]; then
  for candidate in \
    "/c/Users/$USERNAME/Unity/Hub/Editor/2022.3.62f1/Editor/Unity.exe" \
    "/c/Program Files/Unity/Hub/Editor/2022.3.62f1/Editor/Unity.exe"; do
    if [ -d "$(dirname "$candidate")/Data/PlaybackEngines/WebGLSupport" ]; then UNITY_EXE="$candidate"; break; fi
  done
fi
[ -n "$UNITY_EXE" ] || { echo "[deploy] 找不到含 WebGLSupport 的 Unity 2022.3.62f1；請設定 UNITY_EXE"; exit 1; }

VERSION="$(grep -oE 'Version = "[^"]+"' Assets/Scripts/Core/VowVersion.cs | head -1 | cut -d'"' -f2)"
SHA="$(git rev-parse --short HEAD)"
OUT="Builds/WebGL"
echo "[deploy] version=$VERSION commit=$SHA unity=$UNITY_EXE"

if [ -n "$(git status --porcelain)" ]; then
  echo "[deploy] 工作區有未提交的變更——首頁版本列寫的 commit 會與實際內容不符。請先 commit。"; exit 1
fi

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  echo "[deploy] 建置 WebGL（首次約 10~20 分鐘）…"
  rm -rf "$OUT"
  "$UNITY_EXE" -batchmode -quit -projectPath "$ROOT" -buildTarget WebGL \
    -executeMethod Vow.EditorTools.VOWWebGLBuilder.Build -logFile "$ROOT/Builds/webgl-build.log" \
    || { echo "[deploy] Unity 建置失敗，見 Builds/webgl-build.log"; grep -E "error CS|\[VOW\]|Exception|Build completed with a result" Builds/webgl-build.log | tail -15; exit 1; }
fi

# ── 產物檢查：不能只看 Unity 的 exit code ──
[ -f "$OUT/index.html" ] || { echo "[deploy] 缺 $OUT/index.html"; exit 1; }
grep -q "v$VERSION" "$OUT/index.html" || { echo "[deploy] index.html 沒有版本字串 v$VERSION"; exit 1; }
grep -q "__VOW_BUILD_STAMP__" "$OUT/index.html" && { echo "[deploy] 建置時間戳記沒有被寫入"; exit 1; }
ls "$OUT"/Build/*.wasm* >/dev/null 2>&1 || { echo "[deploy] 缺 wasm"; exit 1; }
touch "$OUT/.nojekyll"
echo "[deploy] 產物："; du -sh "$OUT" | cut -f1; grep -o 'build [0-9-]* [0-9:]* UTC · [0-9a-f]*' "$OUT/index.html" | head -1

# ── 推上 gh-pages：gh-pages 是純生成物分支，每次整份覆蓋 ──
REMOTE="$(git remote get-url origin)"
STAGE="$(mktemp -d)"
cp -r "$OUT"/. "$STAGE"/
(
  cd "$STAGE"
  git init -q
  git checkout -q -b gh-pages
  git add -A
  git -c user.name="$(git -C "$ROOT" config user.name || echo vow-deploy)" \
      -c user.email="$(git -C "$ROOT" config user.email || echo vow-deploy@users.noreply.github.com)" \
      commit -q -m "deploy: VOW v$VERSION from $SHA"
  git push -q -f "$REMOTE" gh-pages:gh-pages
)
rm -rf "$STAGE"
echo "[deploy] 已推上 gh-pages：$(git ls-remote origin gh-pages | cut -c1-7)"

# ── 送達證明：等 Pages 發佈後，確認線上首頁真的是這一版 ──
URL="https://9gf6p4448m-del.github.io/vow3d/"
for i in $(seq 1 30); do
  LIVE="$(curl -s -H 'Cache-Control: no-cache' "$URL?t=$(date +%s)" | grep -o 'build [0-9-]* [0-9:]* UTC · [0-9a-f]*' | head -1 || true)"
  WANT="$(grep -o 'build [0-9-]* [0-9:]* UTC · [0-9a-f]*' "$OUT/index.html" | head -1)"
  if [ "$LIVE" = "$WANT" ]; then echo "[deploy] 線上已更新：$URL  →  v$VERSION · $LIVE"; exit 0; fi
  sleep 10
done
echo "[deploy] 已推送，但 5 分鐘內線上首頁仍未顯示新版（線上：${LIVE:-無}）。請稍後再確認 $URL"
exit 2
