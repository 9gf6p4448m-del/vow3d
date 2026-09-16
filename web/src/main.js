import * as THREE from 'three';

// --- Scene, Camera, Renderer Setup ---
const container = document.getElementById('canvas-container');
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x0a0c16);
scene.fog = new THREE.FogExp2(0x0a0c16, 0.022);

// Vainglory Top-Down MOBA Camera
const camera = new THREE.PerspectiveCamera(46, window.innerWidth / window.innerHeight, 0.1, 1000);
camera.position.set(0, 24, 18);
camera.lookAt(0, 0, 0);

const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false, powerPreference: 'high-performance' });
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
container.appendChild(renderer.domElement);

// --- Lighting ---
const ambientLight = new THREE.AmbientLight(0x505c75, 1.3);
scene.add(ambientLight);

const sunLight = new THREE.DirectionalLight(0x99ccff, 2.2);
sunLight.position.set(16, 30, 20);
sunLight.castShadow = true;
sunLight.shadow.mapSize.width = 1024;
sunLight.shadow.mapSize.height = 1024;
scene.add(sunLight);

// --- Arena Floor ---
const floorGeo = new THREE.PlaneGeometry(65, 65);
const floorMat = new THREE.MeshStandardMaterial({
  color: 0x11131e,
  roughness: 0.85,
  metalness: 0.15,
});
const floor = new THREE.Mesh(floorGeo, floorMat);
floor.rotation.x = -Math.PI / 2;
floor.receiveShadow = true;
scene.add(floor);

// Leyline Hex Grid Overlay
const gridHelper = new THREE.GridHelper(65, 30, 0x00f2fe, 0x1a2238);
gridHelper.position.y = 0.02;
scene.add(gridHelper);

// Energy Spires
const spirePositions = [
  new THREE.Vector3(-12, 0, -9),
  new THREE.Vector3(12, 0, -9),
  new THREE.Vector3(0, 0, 8),
];
spirePositions.forEach((pos, idx) => {
  const base = new THREE.Mesh(
    new THREE.CylinderGeometry(1.6, 2.0, 0.4, 6),
    new THREE.MeshStandardMaterial({ color: 0x222a3d, roughness: 0.5 })
  );
  base.position.set(pos.x, 0.2, pos.z);
  scene.add(base);

  const crystal = new THREE.Mesh(
    new THREE.OctahedronGeometry(0.85, 0),
    new THREE.MeshStandardMaterial({
      color: 0x00f2fe,
      emissive: 0x00f2fe,
      emissiveIntensity: 0.85,
      roughness: 0.1
    })
  );
  crystal.position.set(pos.x, 2.2, pos.z);
  crystal.userData = { initialY: 2.2, phase: idx * 2 };
  scene.add(crystal);
});

// --- Entities ---
// 1. Player (Cyan Hero)
const playerGroup = new THREE.Group();
const bodyGeo = new THREE.CapsuleGeometry(0.6, 1.25, 8, 16);
const playerMat = new THREE.MeshStandardMaterial({ color: 0x00f2fe, roughness: 0.25, metalness: 0.75 });
const playerMesh = new THREE.Mesh(bodyGeo, playerMat);
playerMesh.position.y = 1.25;
playerMesh.castShadow = true;
playerGroup.add(playerMesh);

// Player Visor
const visorMesh = new THREE.Mesh(
  new THREE.BoxGeometry(0.4, 0.25, 0.55),
  new THREE.MeshBasicMaterial({ color: 0xffffff })
);
visorMesh.position.set(0, 1.55, 0.45);
playerGroup.add(visorMesh);

// Selection Base Ring
const ringGeo = new THREE.RingGeometry(0.95, 1.15, 32);
const playerRing = new THREE.Mesh(
  ringGeo,
  new THREE.MeshBasicMaterial({ color: 0x00f2fe, side: THREE.DoubleSide })
);
playerRing.rotation.x = -Math.PI / 2;
playerRing.position.y = 0.05;
playerGroup.add(playerRing);

playerGroup.position.set(0, 0, 9);
scene.add(playerGroup);

// 2. Enemy Hero (Crimson PvP Target)
const enemyHeroGroup = new THREE.Group();
const enemyMat = new THREE.MeshStandardMaterial({ color: 0xff3344, roughness: 0.3, metalness: 0.65 });
const enemyMesh = new THREE.Mesh(bodyGeo, enemyMat);
enemyMesh.position.y = 1.25;
enemyMesh.castShadow = true;
enemyHeroGroup.add(enemyMesh);

const enemyRing = new THREE.Mesh(
  ringGeo,
  new THREE.MeshBasicMaterial({ color: 0xff3344, side: THREE.DoubleSide })
);
enemyRing.rotation.x = -Math.PI / 2;
enemyRing.position.y = 0.05;
enemyHeroGroup.add(enemyRing);

enemyHeroGroup.position.set(0, 0, -4);
enemyHeroGroup.userData = {
  type: 'Hero',
  name: '敵方英雄 (PvP)',
  maxHp: 1200,
  hp: 1200,
  mesh: enemyMesh,
  origMat: enemyMat,
};
scene.add(enemyHeroGroup);

// 3. Minion (Golden PvE Target)
const minionGroup = new THREE.Group();
const minionMat = new THREE.MeshStandardMaterial({ color: 0xffcc00, roughness: 0.4, metalness: 0.6 });
const minionMesh = new THREE.Mesh(new THREE.BoxGeometry(0.85, 0.85, 0.85), minionMat);
minionMesh.position.y = 0.55;
minionMesh.castShadow = true;
minionGroup.add(minionMesh);

const minionRing = new THREE.Mesh(
  new THREE.RingGeometry(0.7, 0.88, 24),
  new THREE.MeshBasicMaterial({ color: 0xffcc00, side: THREE.DoubleSide })
);
minionRing.rotation.x = -Math.PI / 2;
minionRing.position.y = 0.05;
minionGroup.add(minionRing);

minionGroup.position.set(-6, 0, 2);
minionGroup.userData = {
  type: 'Minion',
  name: '野怪魔偶 (PvE)',
  maxHp: 450,
  hp: 450,
  mesh: minionMesh,
  origMat: minionMat,
};
scene.add(minionGroup);

const combatTargets = [enemyHeroGroup, minionGroup];

// Target Selection Indicator (Golden rotating ring under selected target)
const targetLockRing = new THREE.Mesh(
  new THREE.RingGeometry(1.25, 1.45, 32),
  new THREE.MeshBasicMaterial({ color: 0xffd700, side: THREE.DoubleSide, transparent: true, opacity: 0.85 })
);
targetLockRing.rotation.x = -Math.PI / 2;
targetLockRing.position.y = 0.06;
scene.add(targetLockRing);

// --- 3D Ghost Wall for Rune Smart Cast ---
const ghostWallGroup = new THREE.Group();
const ghostWallWidth = 1.0;
const ghostWallLength = 6.5;
const ghostWallHeight = 2.4;
const ghostPillars = 3;

for (let i = 0; i < ghostPillars; i++) {
  const gMesh = new THREE.Mesh(
    new THREE.BoxGeometry((ghostWallLength / ghostPillars) * 0.94, ghostWallHeight, ghostWallWidth),
    new THREE.MeshBasicMaterial({ color: 0x00f2fe, transparent: true, opacity: 0.5 })
  );
  gMesh.position.set((i - 1) * (ghostWallLength / ghostPillars), ghostWallHeight / 2, 0);
  ghostWallGroup.add(gMesh);
}
ghostWallGroup.visible = false;
scene.add(ghostWallGroup);

// --- Active Physical Stone Walls ---
const activeWalls = [];

function spawnStoneWall(position, angle) {
  const group = new THREE.Group();
  group.position.set(position.x, 0, position.z);
  group.rotation.y = angle;

  const segLength = ghostWallLength / ghostPillars;
  for (let i = 0; i < ghostPillars; i++) {
    const mesh = new THREE.Mesh(
      new THREE.BoxGeometry(segLength * 0.95, ghostWallHeight, ghostWallWidth),
      new THREE.MeshStandardMaterial({ color: 0x3d475c, roughness: 0.85, metalness: 0.15 })
    );
    mesh.position.set((i - 1) * segLength, ghostWallHeight / 2, 0);
    mesh.castShadow = true;
    mesh.receiveShadow = true;
    group.add(mesh);
  }

  scene.add(group);

  const wallObj = {
    group,
    pos: position.clone(),
    angle,
    length: ghostWallLength,
    width: ghostWallWidth,
    birthTime: performance.now(),
    duration: 5000,
  };
  activeWalls.push(wallObj);

  if (navigator.vibrate) navigator.vibrate([40, 25, 40]);
  spawnDamageText(position, '⚡ 岩壁破土！', 'dash');
}

function checkWallCollision(newPos) {
  for (const wall of activeWalls) {
    const dx = newPos.x - wall.pos.x;
    const dz = newPos.z - wall.pos.z;
    const cos = Math.cos(-wall.angle);
    const sin = Math.sin(-wall.angle);
    const localX = cos * dx - sin * dz;
    const localZ = sin * dx + cos * dz;

    const halfL = wall.length / 2 + 0.65;
    const halfW = wall.width / 2 + 0.65;
    if (Math.abs(localX) < halfL && Math.abs(localZ) < halfW) {
      return true;
    }
  }
  return false;
}

// --- Ghost Dash Trails (殘影) ---
function spawnGhostTrail(pos, rotY) {
  const clone = playerMesh.clone();
  clone.material = new THREE.MeshBasicMaterial({ color: 0x00f2fe, transparent: true, opacity: 0.5 });
  clone.position.copy(pos);
  clone.position.y = 1.25;
  clone.rotation.y = rotY;
  scene.add(clone);

  const fadeStart = performance.now();
  const interval = setInterval(() => {
    const elapsed = performance.now() - fadeStart;
    if (elapsed > 220) {
      scene.remove(clone);
      clone.geometry.dispose();
      clone.material.dispose();
      clearInterval(interval);
    } else {
      clone.material.opacity = (1 - elapsed / 220) * 0.45;
    }
  }, 25);
}

// --- UI Elements ---
const effectLayer = document.getElementById('effect-layer');
const comboBannerEl = document.getElementById('combo-banner');
const jamAlertEl = document.getElementById('jam-alert');
const cadenceRingEl = document.getElementById('cadence-ring');
const runeButtonEl = document.getElementById('rune-button');
const runeTipEl = document.getElementById('rune-tip');

// Damage Popup
function spawnDamageText(worldPos, text, type = 'normal') {
  const screenPos = worldPos.clone().project(camera);
  const x = (screenPos.x * 0.5 + 0.5) * window.innerWidth;
  const y = (-(screenPos.y * 0.5) + 0.5) * window.innerHeight;

  const el = document.createElement('div');
  el.className = `damage-popup ${type}`;
  el.innerText = text;
  el.style.left = `${x}px`;
  el.style.top = `${y}px`;
  effectLayer.appendChild(el);

  setTimeout(() => { el.remove(); }, 700);
}

// Tap Ripple on Ground
function spawnTapRipple(clientX, clientY) {
  const el = document.createElement('div');
  el.className = 'tap-ripple';
  el.style.left = `${clientX}px`;
  el.style.top = `${clientY}px`;
  effectLayer.appendChild(el);
  setTimeout(() => { el.remove(); }, 350);
}

// Overhead HP Bars
const hpBars = [];
function createHpBar(entity, label, isEnemy, isMinion) {
  const wrap = document.createElement('div');
  wrap.className = 'hp-bar-wrap';
  const labelEl = document.createElement('div');
  labelEl.className = 'hp-label';
  labelEl.innerText = label;
  const fill = document.createElement('div');
  fill.className = `hp-bar-fill ${isEnemy ? 'enemy' : (isMinion ? 'minion' : '')}`;
  wrap.appendChild(labelEl);
  wrap.appendChild(fill);
  effectLayer.appendChild(wrap);
  hpBars.push({ entity, wrap, fill });
}
createHpBar(playerGroup, '誓約者 (我方)', false, false);
createHpBar(enemyHeroGroup, '敵方英雄 (PvP)', true, false);
createHpBar(minionGroup, '野怪魔偶 (PvE)', false, true);

function updateHpBars() {
  hpBars.forEach(item => {
    const pos = item.entity.position.clone().add(new THREE.Vector3(0, 2.4, 0));
    const screenPos = pos.project(camera);
    const x = (screenPos.x * 0.5 + 0.5) * window.innerWidth;
    const y = (-(screenPos.y * 0.5) + 0.5) * window.innerHeight;
    item.wrap.style.left = `${x}px`;
    item.wrap.style.top = `${y}px`;

    if (item.entity.userData.maxHp) {
      const pct = Math.max(0, (item.entity.userData.hp / item.entity.userData.maxHp) * 100);
      item.fill.style.width = `${pct}%`;
    }
  });
}

// --- Player Combat & Cadence Config (Original GDD Spec) ---
const CADENCE = {
  JUST_FRAME_MS: 180,      // 180ms 目押窗口 (配合手機觸控調校)
  MIN_FLICK_PX: 15,        // 15px 微彈指門檻
  MAX_FLICK_PX: 70,        // 70px 微彈指上限
  MICRO_DASH_DIST: 1.8,    // 1.8米微衝刺
  ANTI_MASH_THRESHOLD: 3,  // 0.3秒最多 2 次點擊
  ANTI_MASH_WINDOW: 300,
  JAM_PENALTY_MS: 250,     // 0.25秒卡刀硬直
};

let playerState = {
  moveSpeed: 7.5,
  isMoving: false,
  targetPos: playerGroup.position.clone(),
  attackTarget: null,
  attackRange: 5.0,
  attackCooldown: 0,
  attackInterval: 0.85,

  // 走A 目押窗口
  justFrameActive: false,
  justFrameStart: 0,
  comboCount: 0,

  // 防亂點狂戳
  clickTimestamps: [],
  isJammed: false,
  jammedUntil: 0,
};

// Raycaster & Screen Projection
const raycaster = new THREE.Raycaster();
const mouse = new THREE.Vector2();

function getGroundPoint(screenX, screenY) {
  mouse.x = (screenX / window.innerWidth) * 2 - 1;
  mouse.y = -(screenY / window.innerHeight) * 2 + 1;
  raycaster.setFromCamera(mouse, camera);
  const intersects = raycaster.intersectObject(floor);
  return intersects.length > 0 ? intersects[0].point : null;
}

function getIntersectedTarget(screenX, screenY) {
  mouse.x = (screenX / window.innerWidth) * 2 - 1;
  mouse.y = -(screenY / window.innerHeight) * 2 + 1;
  raycaster.setFromCamera(mouse, camera);

  const meshes = [];
  combatTargets.forEach(tgt => tgt.traverse(c => {
    if (c.isMesh) {
      c.userData.parentEntity = tgt;
      meshes.push(c);
    }
  }));

  const intersects = raycaster.intersectObjects(meshes);
  return intersects.length > 0 ? intersects[0].object.userData.parentEntity : null;
}

// --- Anti-Mashing Check ---
function checkAntiMashing() {
  const now = performance.now();
  playerState.clickTimestamps = playerState.clickTimestamps.filter(t => now - t < CADENCE.ANTI_MASH_WINDOW);
  playerState.clickTimestamps.push(now);

  if (playerState.clickTimestamps.length >= CADENCE.ANTI_MASH_THRESHOLD) {
    playerState.isJammed = true;
    playerState.jammedUntil = now + CADENCE.JAM_PENALTY_MS;
    playerState.comboCount = 0;
    playerState.justFrameActive = false;
    cadenceRingEl.style.opacity = '0';

    jamAlertEl.style.opacity = '1';
    if (navigator.vibrate) navigator.vibrate(100);
    setTimeout(() => { jamAlertEl.style.opacity = '0'; }, CADENCE.JAM_PENALTY_MS + 200);
    return true;
  }
  return false;
}

// --- Execute Attack & Open Just-Frame ---
function executeAttack(target) {
  if (!target) return;
  playerState.attackCooldown = playerState.attackInterval;

  // Face target
  playerGroup.lookAt(target.position.x, playerGroup.position.y, target.position.z);

  // Attack beam VFX
  const hitPoint = target.position.clone().add(new THREE.Vector3(0, 1.2, 0));
  const startPoint = playerGroup.position.clone().add(new THREE.Vector3(0, 1.2, 0));
  const dir = new THREE.Vector3().subVectors(hitPoint, startPoint).normalize();

  const beam = new THREE.Mesh(
    new THREE.CylinderGeometry(0.08, 0.2, startPoint.distanceTo(hitPoint), 8),
    new THREE.MeshBasicMaterial({ color: 0x00f2fe })
  );
  beam.position.copy(startPoint).lerp(hitPoint, 0.5);
  beam.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir);
  scene.add(beam);

  setTimeout(() => {
    scene.remove(beam);
    beam.geometry.dispose();
    beam.material.dispose();
  }, 90);

  // Damage
  const isCrit = Math.random() < 0.2;
  const dmg = isCrit ? 220 : 120;
  target.userData.hp = Math.max(0, target.userData.hp - dmg);
  if (target.userData.hp === 0) target.userData.hp = target.userData.maxHp;
  spawnDamageText(target.position, isCrit ? `💥 暴擊 -${dmg}` : `-${dmg}`, isCrit ? 'crit' : 'normal');

  // Flash white
  if (target.userData.mesh) {
    target.userData.mesh.material = new THREE.MeshBasicMaterial({ color: 0xffffff });
    setTimeout(() => { target.userData.mesh.material = target.userData.origMat; }, 80);
  }

  // Open Just-Frame Window!
  playerState.justFrameActive = true;
  playerState.justFrameStart = performance.now();

  // Show Cadence Ring under player
  const screenPos = playerGroup.position.clone().project(camera);
  cadenceRingEl.style.left = `${(screenPos.x * 0.5 + 0.5) * window.innerWidth}px`;
  cadenceRingEl.style.top = `${(-(screenPos.y * 0.5) + 0.5) * window.innerHeight}px`;
  cadenceRingEl.style.opacity = '1';
  cadenceRingEl.style.transform = 'translate(-50%, -50%) scale(1.4)';

  setTimeout(() => {
    if (playerState.justFrameActive) {
      playerState.justFrameActive = false;
      cadenceRingEl.style.opacity = '0';
      cadenceRingEl.style.transform = 'translate(-50%, -50%) scale(0.4)';
    }
  }, CADENCE.JUST_FRAME_MS);
}

// --- Execute Micro-Flick Dash (走A 微衝刺) ---
function executeMicroDash(screenDeltaX, screenDeltaY) {
  // Convert screen delta to 3D world direction
  // Screen right (+X) -> World right (+X)
  // Screen down (+Y) -> World forward (+Z)
  const worldDir = new THREE.Vector3(screenDeltaX, 0, screenDeltaY).normalize();
  const dest = playerGroup.position.clone().add(worldDir.multiplyScalar(CADENCE.MICRO_DASH_DIST));

  if (!checkWallCollision(dest)) {
    spawnGhostTrail(playerGroup.position, playerGroup.rotation.y);
    playerGroup.position.copy(dest);
    playerState.targetPos.copy(dest);
    playerState.isMoving = false;

    // Haptic & Combo
    if (navigator.vibrate) navigator.vibrate(25);
    playerState.comboCount++;
    comboBannerEl.innerText = `★ PERFECT CADENCE x${playerState.comboCount} ★`;
    comboBannerEl.style.opacity = '1';
    comboBannerEl.style.transform = 'scale(1.15)';
    setTimeout(() => {
      comboBannerEl.style.opacity = '0';
      comboBannerEl.style.transform = 'scale(1.0)';
    }, 600);

    spawnDamageText(playerGroup.position, `⚡ 微彈指滑步!`, 'dash');

    // Cancel backswing immediately!
    playerState.attackCooldown = 0.1;
    playerState.justFrameActive = false;
    cadenceRingEl.style.opacity = '0';
    return true;
  }
  return false;
}

// --- Pure Tap & Flick Input Listener ---
let touchStartPos = null;
let touchStartTime = 0;

window.addEventListener('pointerdown', (e) => {
  if (e.target.closest('#rune-button')) return; // Handled separately
  if (checkAntiMashing()) return;

  touchStartPos = { x: e.clientX, y: e.clientY };
  touchStartTime = performance.now();
});

window.addEventListener('pointerup', (e) => {
  if (!touchStartPos) return;
  const deltaX = e.clientX - touchStartPos.x;
  const deltaY = e.clientY - touchStartPos.y;
  const dist = Math.hypot(deltaX, deltaY);
  const duration = performance.now() - touchStartTime;

  // 情況 1：原地微彈指 (Micro-Flick) 檢測
  if (dist >= CADENCE.MIN_FLICK_PX && dist <= CADENCE.MAX_FLICK_PX && duration < 250) {
    if (playerState.justFrameActive && playerState.attackTarget?.userData.type === 'Hero') {
      executeMicroDash(deltaX, deltaY);
      touchStartPos = null;
      return;
    }
  }

  // 情況 2：常規點擊 (Tap)
  if (dist < CADENCE.MIN_FLICK_PX) {
    const target = getIntersectedTarget(e.clientX, e.clientY);
    if (target) {
      // 點擊怪物/敵人 ➔ 鎖定並攻擊
      playerState.attackTarget = target;
      playerState.isMoving = false;
    } else {
      // 點擊地面 ➔ 移動走位
      const pt = getGroundPoint(e.clientX, e.clientY);
      if (pt) {
        playerState.attackTarget = null;
        playerState.targetPos.copy(pt);
        playerState.isMoving = true;
        spawnTapRipple(e.clientX, e.clientY);

        // 點地取消普攻後搖 (普通走A)
        if (playerState.justFrameActive) {
          playerState.justFrameActive = false;
          cadenceRingEl.style.opacity = '0';
          playerState.attackCooldown = 0.15;
        }
      }
    }
  }

  touchStartPos = null;
});

// --- P1 核心施法器：右下符印目標軸心向量施法 (Rune-Vector Smart Cast) ---
let isHoldingRune = false;
let runeTouchStart = { x: 0, y: 0 };
let currentAnchorPointA = new THREE.Vector3();
let currentWallRotation = 0;

runeButtonEl.addEventListener('pointerdown', (e) => {
  e.preventDefault();
  isHoldingRune = true;
  runeTouchStart = { x: e.clientX, y: e.clientY };

  // 1. 自動以當前鎖定目標為「起點 A」；若無目標，定於身前 5 米
  if (playerState.attackTarget) {
    currentAnchorPointA.copy(playerState.attackTarget.position);
  } else {
    const forward = new THREE.Vector3(0, 0, -1).applyQuaternion(playerGroup.quaternion);
    currentAnchorPointA.copy(playerGroup.position).add(forward.multiplyScalar(5.0));
  }

  currentWallRotation = playerGroup.rotation.y;
  ghostWallGroup.position.set(currentAnchorPointA.x, 0.1, currentAnchorPointA.z);
  ghostWallGroup.rotation.y = currentWallRotation;
  ghostWallGroup.visible = true;
  runeTipEl.style.opacity = '1';

  if (navigator.vibrate) navigator.vibrate(20);
});

window.addEventListener('pointermove', (e) => {
  if (!isHoldingRune) return;
  const deltaX = e.clientX - runeTouchStart.x;
  const deltaY = e.clientY - runeTouchStart.y;
  const dist = Math.hypot(deltaX, deltaY);

  if (dist > 10) {
    // 拇指在符印上的滑動向量，直接決定以起點 A 為軸心的「旋轉角度」！
    const angle = Math.atan2(deltaY, deltaX);
    currentWallRotation = angle + Math.PI / 2;
    ghostWallGroup.rotation.y = currentWallRotation;
  }
});

window.addEventListener('pointerup', (e) => {
  if (!isHoldingRune) return;
  isHoldingRune = false;
  ghostWallGroup.visible = false;
  runeTipEl.style.opacity = '0';

  // 鬆手瞬間 (0.08s 極速盲操) 生成石牆！
  spawnStoneWall(currentAnchorPointA, currentWallRotation);
});

// --- Resize Handling ---
window.addEventListener('resize', () => {
  camera.aspect = window.innerWidth / window.innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(window.innerWidth, window.innerHeight);
});

// --- Main Game Loop ---
const clock = new THREE.Clock();

function animate() {
  requestAnimationFrame(animate);
  const delta = Math.min(clock.getDelta(), 0.1);
  const now = performance.now();

  // Handle Jam
  if (playerState.isJammed) {
    if (now > playerState.jammedUntil) {
      playerState.isJammed = false;
    } else {
      renderer.render(scene, camera);
      updateHpBars();
      return;
    }
  }

  // Attack cooldown
  if (playerState.attackCooldown > 0) {
    playerState.attackCooldown -= delta;
  }

  // Combat Auto-Targeting
  if (playerState.attackTarget) {
    // Update target lock ring under target
    targetLockRing.position.set(
      playerState.attackTarget.position.x,
      0.06,
      playerState.attackTarget.position.z
    );
    targetLockRing.rotation.z += 0.025;
    targetLockRing.visible = true;

    const dist = playerGroup.position.distanceTo(playerState.attackTarget.position);
    if (dist <= playerState.attackRange) {
      playerState.isMoving = false;
      if (playerState.attackCooldown <= 0) {
        executeAttack(playerState.attackTarget);
      }
    } else {
      // Walk into attack range
      playerState.targetPos.copy(playerState.attackTarget.position);
      playerState.isMoving = true;
    }
  } else {
    targetLockRing.visible = false;
  }

  // Movement Logic (Tap to Move)
  if (playerState.isMoving) {
    const currentPos = playerGroup.position.clone();
    const moveDir = new THREE.Vector3().subVectors(playerState.targetPos, currentPos);
    moveDir.y = 0;
    const dist = moveDir.length();

    if (dist > 0.15) {
      moveDir.normalize();
      const step = moveDir.multiplyScalar(playerState.moveSpeed * delta);
      const nextPos = currentPos.clone().add(step);

      if (!checkWallCollision(nextPos)) {
        playerGroup.position.copy(nextPos);
        playerGroup.lookAt(playerGroup.position.x + moveDir.x, playerGroup.position.y, playerGroup.position.z + moveDir.z);
      } else {
        playerState.isMoving = false;
      }
    } else {
      playerState.isMoving = false;
    }
  }

  // Animate Sinking/Decaying Stone Walls
  for (let i = activeWalls.length - 1; i >= 0; i--) {
    const wall = activeWalls[i];
    const age = now - wall.birthTime;
    if (age > wall.duration) {
      wall.group.position.y -= delta * 3.5;
      if (wall.group.position.y < -ghostWallHeight - 1.0) {
        scene.remove(wall.group);
        activeWalls.splice(i, 1);
      }
    }
  }

  // Animate Leyline Crystals
  scene.traverse((obj) => {
    if (obj.userData && obj.userData.initialY !== undefined) {
      obj.rotation.y += 0.02;
      obj.position.y = obj.userData.initialY + Math.sin(now * 0.003 + obj.userData.phase) * 0.25;
    }
  });

  // Camera Follow
  camera.position.x = THREE.MathUtils.lerp(camera.position.x, playerGroup.position.x, 0.08);
  camera.position.z = THREE.MathUtils.lerp(camera.position.z, playerGroup.position.z + 18, 0.08);
  camera.lookAt(playerGroup.position.x, 0, playerGroup.position.z);

  // Sync Overhead Health Bars
  updateHpBars();

  renderer.render(scene, camera);
}

animate();
