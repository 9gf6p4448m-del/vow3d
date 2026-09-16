import * as THREE from 'three';

// --- Scene, Camera, Renderer Setup ---
const container = document.getElementById('canvas-container');
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x0a0c16);
scene.fog = new THREE.FogExp2(0x0a0c16, 0.022);

const camera = new THREE.PerspectiveCamera(48, window.innerWidth / window.innerHeight, 0.1, 1000);
camera.position.set(0, 24, 18);
camera.lookAt(0, 0, 0);

const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false, powerPreference: 'high-performance' });
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
container.appendChild(renderer.domElement);

// --- Lighting ---
const ambientLight = new THREE.AmbientLight(0x5a6988, 1.4);
scene.add(ambientLight);

const sunLight = new THREE.DirectionalLight(0xaad4ff, 2.2);
sunLight.position.set(18, 32, 22);
sunLight.castShadow = true;
sunLight.shadow.mapSize.width = 1024;
sunLight.shadow.mapSize.height = 1024;
scene.add(sunLight);

// --- Arena Floor ---
const floorGeo = new THREE.PlaneGeometry(70, 70);
const floorMat = new THREE.MeshStandardMaterial({
  color: 0x111420,
  roughness: 0.85,
  metalness: 0.2,
});
const floor = new THREE.Mesh(floorGeo, floorMat);
floor.rotation.x = -Math.PI / 2;
floor.receiveShadow = true;
scene.add(floor);

// Grid Overlay
const gridHelper = new THREE.GridHelper(70, 35, 0x00f2fe, 0x1a233a);
gridHelper.position.y = 0.02;
scene.add(gridHelper);

// --- Leyline Spires (Energy Nodes) ---
const spirePositions = [
  new THREE.Vector3(-12, 0, -10),
  new THREE.Vector3(12, 0, -10),
  new THREE.Vector3(0, 0, 8),
];
spirePositions.forEach((pos, idx) => {
  const base = new THREE.Mesh(
    new THREE.CylinderGeometry(1.8, 2.2, 0.5, 6),
    new THREE.MeshStandardMaterial({ color: 0x242d42, roughness: 0.5 })
  );
  base.position.set(pos.x, 0.25, pos.z);
  scene.add(base);

  const crystal = new THREE.Mesh(
    new THREE.OctahedronGeometry(0.9, 0),
    new THREE.MeshStandardMaterial({
      color: 0x00f2fe,
      emissive: 0x00f2fe,
      emissiveIntensity: 0.9,
      roughness: 0.1
    })
  );
  crystal.position.set(pos.x, 2.4, pos.z);
  crystal.userData = { initialY: 2.4, phase: idx * 2 };
  scene.add(crystal);
});

// --- Entities ---
// 1. Player (Cyan Hero)
const playerGroup = new THREE.Group();
const bodyGeo = new THREE.CapsuleGeometry(0.65, 1.3, 8, 16);
const playerMat = new THREE.MeshStandardMaterial({ color: 0x00f2fe, roughness: 0.2, metalness: 0.8 });
const playerMesh = new THREE.Mesh(bodyGeo, playerMat);
playerMesh.position.y = 1.3;
playerMesh.castShadow = true;
playerGroup.add(playerMesh);

// Player Visor
const visorMesh = new THREE.Mesh(
  new THREE.BoxGeometry(0.45, 0.3, 0.6),
  new THREE.MeshBasicMaterial({ color: 0xffffff })
);
visorMesh.position.set(0, 1.6, 0.5);
playerGroup.add(visorMesh);

// Player Ground Ring
const ringGeo = new THREE.RingGeometry(1.0, 1.25, 32);
const playerRing = new THREE.Mesh(
  ringGeo,
  new THREE.MeshBasicMaterial({ color: 0x00f2fe, side: THREE.DoubleSide })
);
playerRing.rotation.x = -Math.PI / 2;
playerRing.position.y = 0.05;
playerGroup.add(playerRing);

playerGroup.position.set(0, 0, 10);
scene.add(playerGroup);

// 2. Enemy Hero (Crimson PvP Target)
const enemyHeroGroup = new THREE.Group();
const enemyMat = new THREE.MeshStandardMaterial({ color: 0xff3344, roughness: 0.3, metalness: 0.7 });
const enemyMesh = new THREE.Mesh(bodyGeo, enemyMat);
enemyMesh.position.y = 1.3;
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
const minionMesh = new THREE.Mesh(new THREE.BoxGeometry(0.9, 0.9, 0.9), minionMat);
minionMesh.position.y = 0.6;
minionMesh.castShadow = true;
minionGroup.add(minionMesh);

const minionRing = new THREE.Mesh(
  new THREE.RingGeometry(0.7, 0.9, 24),
  new THREE.MeshBasicMaterial({ color: 0xffcc00, side: THREE.DoubleSide })
);
minionRing.rotation.x = -Math.PI / 2;
minionRing.position.y = 0.05;
minionGroup.add(minionRing);

minionGroup.position.set(-7, 0, 2);
minionGroup.userData = {
  type: 'Minion',
  name: '地脈石偶 (PvE)',
  maxHp: 500,
  hp: 500,
  mesh: minionMesh,
  origMat: minionMat,
};
scene.add(minionGroup);

const combatTargets = [enemyHeroGroup, minionGroup];

// Target Lock Ring (indicates selected target)
const targetLockRing = new THREE.Mesh(
  new THREE.RingGeometry(1.3, 1.55, 32),
  new THREE.MeshBasicMaterial({ color: 0xffd700, side: THREE.DoubleSide, transparent: true, opacity: 0.8 })
);
targetLockRing.rotation.x = -Math.PI / 2;
targetLockRing.position.y = 0.08;
scene.add(targetLockRing);

// --- 3D Ghost Wall (Live Aiming Preview) ---
const ghostWallGroup = new THREE.Group();
const ghostWallWidth = 1.0;
const ghostWallLength = 6.0;
const ghostWallHeight = 2.4;

const ghostPillars = 3;
for (let i = 0; i < ghostPillars; i++) {
  const gMesh = new THREE.Mesh(
    new THREE.BoxGeometry((ghostWallLength / ghostPillars) * 0.92, ghostWallHeight, ghostWallWidth),
    new THREE.MeshBasicMaterial({
      color: 0x00f2fe,
      transparent: true,
      opacity: 0.45,
      wireframe: false
    })
  );
  gMesh.position.set((i - 1) * (ghostWallLength / ghostPillars), ghostWallHeight / 2, 0);
  ghostWallGroup.add(gMesh);
}
// Aim Trajectory Line
const aimLineMat = new THREE.LineBasicMaterial({ color: 0x00f2fe, transparent: true, opacity: 0.7 });
const aimLineGeo = new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), new THREE.Vector3()]);
const aimLine = new THREE.Line(aimLineGeo, aimLineMat);
scene.add(aimLine);

ghostWallGroup.visible = false;
aimLine.visible = false;
scene.add(ghostWallGroup);

// --- Active Physical Stone Walls Storage ---
const activeWalls = [];

function spawnStoneWall(position, angle) {
  const group = new THREE.Group();
  group.position.set(position.x, -ghostWallHeight, position.z); // Start underground for erupt effect
  group.rotation.y = angle;

  const segLength = ghostWallLength / ghostPillars;
  for (let i = 0; i < ghostPillars; i++) {
    const mesh = new THREE.Mesh(
      new THREE.BoxGeometry(segLength * 0.95, ghostWallHeight, ghostWallWidth),
      new THREE.MeshStandardMaterial({ color: 0x3b4458, roughness: 0.85, metalness: 0.15 })
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
    targetY: 0,
    currentY: -ghostWallHeight,
    birthTime: performance.now(),
    duration: 5500, // 5.5s decay
  };
  activeWalls.push(wallObj);

  // Eruption Haptic & Sound feel
  if (navigator.vibrate) navigator.vibrate([40, 20, 60]);
  spawnDamageText(position, '⚡ 岩壁升起！', 'dash');
}

function checkWallCollision(newPos) {
  for (const wall of activeWalls) {
    if (wall.currentY < -0.5) continue; // Not fully risen yet
    const dx = newPos.x - wall.pos.x;
    const dz = newPos.z - wall.pos.z;
    const cos = Math.cos(-wall.angle);
    const sin = Math.sin(-wall.angle);
    const localX = cos * dx - sin * dz;
    const localZ = sin * dx + cos * dz;

    const halfL = wall.length / 2 + 0.7;
    const halfW = wall.width / 2 + 0.7;
    if (Math.abs(localX) < halfL && Math.abs(localZ) < halfW) {
      return true;
    }
  }
  return false;
}

// --- Ghost Dash Trails (殘影特效) ---
function spawnGhostTrail(pos, rotY) {
  for (let step = 1; step <= 2; step++) {
    const clone = playerMesh.clone();
    clone.material = new THREE.MeshBasicMaterial({
      color: 0x00f2fe,
      transparent: true,
      opacity: 0.5 - step * 0.15,
    });
    clone.position.copy(pos);
    clone.position.y = 1.3;
    clone.rotation.y = rotY;
    scene.add(clone);

    const fadeStart = performance.now();
    const interval = setInterval(() => {
      const elapsed = performance.now() - fadeStart;
      if (elapsed > 250) {
        scene.remove(clone);
        clone.geometry.dispose();
        clone.material.dispose();
        clearInterval(interval);
      } else {
        clone.material.opacity = (1 - elapsed / 250) * 0.4;
      }
    }, 30);
  }
}

// --- Floating UI Elements (HP bars & Damage popups) ---
const hpLayer = document.getElementById('hp-layer');
const guideTextEl = document.getElementById('guide-text');
const comboBannerEl = document.getElementById('combo-banner');
const jamAlertEl = document.getElementById('jam-alert');
const cadenceRingEl = document.getElementById('cadence-ring');
const attackPulseRingEl = document.getElementById('attack-pulse-ring');
const aimToastEl = document.getElementById('aim-toast');

function spawnDamageText(worldPos, text, type = 'normal') {
  const screenPos = worldPos.clone().project(camera);
  const x = (screenPos.x * 0.5 + 0.5) * window.innerWidth;
  const y = (-(screenPos.y * 0.5) + 0.5) * window.innerHeight;

  const el = document.createElement('div');
  el.className = `damage-popup ${type}`;
  el.innerText = text;
  el.style.left = `${x}px`;
  el.style.top = `${y}px`;
  hpLayer.appendChild(el);

  setTimeout(() => { el.remove(); }, 800);
}

// Entity HP Bars
const hpBarElements = [];
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
  hpLayer.appendChild(wrap);

  hpBarElements.push({ entity, wrap, fill });
}

createHpBar(playerGroup, '我方英雄', false, false);
createHpBar(enemyHeroGroup, '敵方英雄 (PvP)', true, false);
createHpBar(minionGroup, '野怪魔偶 (PvE)', false, true);

function updateHpBars() {
  hpBarElements.forEach(item => {
    const pos = item.entity.position.clone().add(new THREE.Vector3(0, 2.5, 0));
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

// --- Player State & Cadence Core ---
const CADENCE = {
  JUST_FRAME_MS: 380,     // 寬容節奏窗口 380ms
  DASH_DISTANCE: 3.8,     // 殘影滑步距離
  ANTI_MASH_THRESHOLD: 4, // 0.3秒最多 3 次點擊
  ANTI_MASH_WINDOW: 320,
  JAM_PENALTY_MS: 300,
};

let playerState = {
  moveSpeed: 8.0,
  isMoving: false,
  moveVector: new THREE.Vector2(0, 0), // from joystick
  targetWalkPos: null,               // from ground tap
  attackTarget: enemyHeroGroup,       // default lock
  attackRange: 4.8,
  attackCooldown: 0,
  attackInterval: 0.82,

  // 走A 節奏判定
  justFrameActive: false,
  justFrameStart: 0,
  comboCount: 0,

  // 防亂點卡刀
  clickTimestamps: [],
  isJammed: false,
  jammedUntil: 0,
};

// --- Attack Visual Effect ---
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
    new THREE.CylinderGeometry(0.09, 0.22, startPoint.distanceTo(hitPoint), 8),
    new THREE.MeshBasicMaterial({ color: 0x00f2fe })
  );
  beam.position.copy(startPoint).lerp(hitPoint, 0.5);
  beam.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir);
  scene.add(beam);

  setTimeout(() => {
    scene.remove(beam);
    beam.geometry.dispose();
    beam.material.dispose();
  }, 100);

  // Damage Calculation
  const isCrit = Math.random() < 0.25;
  const dmg = isCrit ? 260 : 140;
  target.userData.hp = Math.max(0, target.userData.hp - dmg);
  if (target.userData.hp === 0) {
    target.userData.hp = target.userData.maxHp; // respawn dummy
  }

  spawnDamageText(target.position, isCrit ? `💥 暴擊 -${dmg}` : `-${dmg}`, isCrit ? 'crit' : 'normal');

  // Flash white on hit
  if (target.userData.mesh) {
    target.userData.mesh.material = new THREE.MeshBasicMaterial({ color: 0xffffff });
    setTimeout(() => {
      target.userData.mesh.material = target.userData.origMat;
    }, 90);
  }

  // Open Just-Frame Cadence Window!
  playerState.justFrameActive = true;
  playerState.justFrameStart = performance.now();

  // Pulse rings
  attackPulseRingEl.style.opacity = '1';
  attackPulseRingEl.style.transform = 'scale(1.25)';
  cadenceRingEl.style.opacity = '1';
  cadenceRingEl.style.transform = 'translate(-50%, -50%) scale(1.6)';

  guideTextEl.innerHTML = `🔥 <span>節奏命中！</span> 立即推搖桿或滑動 ➔ 觸發 <span>【滑步衝刺走A】</span>！`;

  setTimeout(() => {
    if (playerState.justFrameActive) {
      playerState.justFrameActive = false;
      attackPulseRingEl.style.opacity = '0';
      cadenceRingEl.style.opacity = '0';
      cadenceRingEl.style.transform = 'translate(-50%, -50%) scale(0.3)';
      guideTextEl.innerHTML = `左手推搖桿走位 • 點右下 <span>【普攻】</span> 攻擊 • 命中後推搖桿立即 <span>【滑步走A】</span>`;
    }
  }, CADENCE.JUST_FRAME_MS);
}

// --- Trigger Micro-Flick Stutter-Step (走A滑步) ---
function tryTriggerStutterDash(dashDir) {
  if (!playerState.justFrameActive) return false;
  const elapsed = performance.now() - playerState.justFrameStart;
  if (elapsed > CADENCE.JUST_FRAME_MS) return false;

  // Compute dash destination
  const dest = playerGroup.position.clone().add(
    new THREE.Vector3(dashDir.x, 0, dashDir.z).normalize().multiplyScalar(CADENCE.DASH_DISTANCE)
  );

  if (!checkWallCollision(dest)) {
    // Spawn Ghost Afterimages
    spawnGhostTrail(playerGroup.position, playerGroup.rotation.y);

    // Instant Dash
    playerGroup.position.copy(dest);
    playerState.targetWalkPos = null;

    // Haptic & Visuals
    if (navigator.vibrate) navigator.vibrate([25, 15, 25]);
    playerState.comboCount++;
    comboBannerEl.innerText = `★ PERFECT 走A x${playerState.comboCount} (後搖取消) ★`;
    comboBannerEl.style.opacity = '1';
    comboBannerEl.style.transform = 'translateX(-50%) scale(1.2)';
    setTimeout(() => {
      comboBannerEl.style.opacity = '0';
      comboBannerEl.style.transform = 'translateX(-50%) scale(1.0)';
    }, 700);

    spawnDamageText(playerGroup.position, `⚡ 完美滑步 x${playerState.comboCount}!`, 'dash');

    // Reset backswing & grant attack speed boost!
    playerState.attackCooldown = 0.12;
    playerState.justFrameActive = false;
    attackPulseRingEl.style.opacity = '0';
    cadenceRingEl.style.opacity = '0';
    return true;
  }
  return false;
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
    jamAlertEl.style.opacity = '1';
    if (navigator.vibrate) navigator.vibrate(100);
    setTimeout(() => { jamAlertEl.style.opacity = '0'; }, CADENCE.JAM_PENALTY_MS + 200);
    return true;
  }
  return false;
}

// --- Left Joystick Implementation ---
const joystickZone = document.getElementById('joystick-zone');
const joystickThumb = document.getElementById('joystick-thumb');
let joystickActive = false;
let joystickCenter = { x: 0, y: 0 };
const maxRadius = 45;

joystickZone.addEventListener('pointerdown', (e) => {
  e.preventDefault();
  joystickActive = true;
  const rect = joystickZone.getBoundingClientRect();
  joystickCenter = { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
  handleJoystickMove(e.clientX, e.clientY);
});

window.addEventListener('pointermove', (e) => {
  if (!joystickActive) return;
  handleJoystickMove(e.clientX, e.clientY);
});

function handleJoystickMove(clientX, clientY) {
  const dx = clientX - joystickCenter.x;
  const dy = clientY - joystickCenter.y;
  const dist = Math.hypot(dx, dy);
  const clampedDist = Math.min(dist, maxRadius);
  const angle = Math.atan2(dy, dx);

  const thumbX = Math.cos(angle) * clampedDist;
  const thumbY = Math.sin(angle) * clampedDist;
  joystickThumb.style.transform = `translate(${thumbX}px, ${thumbY}px)`;

  if (clampedDist > 10) {
    // Normal movement vector (screen X -> 3D X, screen Y -> 3D Z)
    playerState.moveVector.set(dx / dist, dy / dist);
    playerState.targetWalkPos = null;

    // If Just-Frame cadence is open, moving joystick immediately triggers the Micro-Dash!
    if (playerState.justFrameActive) {
      tryTriggerStutterDash(new THREE.Vector3(playerState.moveVector.x, 0, playerState.moveVector.y));
    }
  } else {
    playerState.moveVector.set(0, 0);
  }
}

window.addEventListener('pointerup', () => {
  if (!joystickActive) return;
  joystickActive = false;
  joystickThumb.style.transform = 'translate(0px, 0px)';
  playerState.moveVector.set(0, 0);
});

// --- Attack Button Implementation ---
const attackBtn = document.getElementById('attack-button');
attackBtn.addEventListener('pointerdown', (e) => {
  e.preventDefault();
  if (checkAntiMashing()) return;

  // Auto-target nearest enemy if not selected
  let nearest = enemyHeroGroup;
  let minDist = playerGroup.position.distanceTo(enemyHeroGroup.position);
  combatTargets.forEach(tgt => {
    const d = playerGroup.position.distanceTo(tgt.position);
    if (d < minDist) {
      minDist = d;
      nearest = tgt;
    }
  });
  playerState.attackTarget = nearest;

  // If in range, attack immediately!
  if (minDist <= playerState.attackRange) {
    if (playerState.attackCooldown <= 0) {
      executeAttack(nearest);
    }
  } else {
    // Walk into range
    playerState.targetWalkPos = nearest.position.clone();
  }
});

// --- Rune Wall (Smart 3D Aiming & Placement) ---
const runeBtn = document.getElementById('rune-button');
let isAimingRune = false;
let runeTouchStart = { x: 0, y: 0 };
let runeTargetPos = new THREE.Vector3();
let runeAngle = 0;

runeBtn.addEventListener('pointerdown', (e) => {
  e.preventDefault();
  isAimingRune = true;
  runeTouchStart = { x: e.clientX, y: e.clientY };

  // Place ghost wall 4m in front of player initially
  const forward = new THREE.Vector3(0, 0, -1).applyQuaternion(playerGroup.quaternion);
  runeTargetPos.copy(playerGroup.position).add(forward.multiplyScalar(4.5));
  runeAngle = playerGroup.rotation.y;

  ghostWallGroup.position.set(runeTargetPos.x, 0.1, runeTargetPos.z);
  ghostWallGroup.rotation.y = runeAngle;
  ghostWallGroup.visible = true;
  aimLine.visible = true;
  aimToastEl.style.display = 'block';

  if (navigator.vibrate) navigator.vibrate(20);
});

window.addEventListener('pointermove', (e) => {
  if (!isAimingRune) return;
  const deltaX = e.clientX - runeTouchStart.x;
  const deltaY = e.clientY - runeTouchStart.y;
  const dragDist = Math.hypot(deltaX, deltaY);

  if (dragDist > 10) {
    // Thumb drag vector maps directly to 3D aim offset
    const aimAngle = Math.atan2(deltaY, deltaX);
    const aimDist = Math.min(dragDist * 0.06, 7.5); // Max 7.5m cast range

    const offset = new THREE.Vector3(Math.cos(aimAngle), 0, Math.sin(aimAngle)).multiplyScalar(aimDist);
    runeTargetPos.copy(playerGroup.position).add(offset);
    runeAngle = aimAngle + Math.PI / 2; // Wall is perpendicular to aim vector!

    ghostWallGroup.position.set(runeTargetPos.x, 0.1, runeTargetPos.z);
    ghostWallGroup.rotation.y = runeAngle;

    // Update trajectory line
    const pts = [
      new THREE.Vector3(playerGroup.position.x, 0.2, playerGroup.position.z),
      new THREE.Vector3(runeTargetPos.x, 0.2, runeTargetPos.z)
    ];
    aimLine.geometry.setFromPoints(pts);
  }
});

window.addEventListener('pointerup', (e) => {
  if (!isAimingRune) return;
  isAimingRune = false;
  ghostWallGroup.visible = false;
  aimLine.visible = false;
  aimToastEl.style.display = 'none';

  const deltaX = e.clientX - runeTouchStart.x;
  const deltaY = e.clientY - runeTouchStart.y;
  if (Math.hypot(deltaX, deltaY) > 15) {
    spawnStoneWall(runeTargetPos, runeAngle);
  }
});

// --- Swipe on Screen for Free Dash ---
let screenTouchStart = null;
window.addEventListener('pointerdown', (e) => {
  if (e.target.closest('#joystick-zone') || e.target.closest('#action-zone')) return;
  screenTouchStart = { x: e.clientX, y: e.clientY };
});

window.addEventListener('pointerup', (e) => {
  if (!screenTouchStart) return;
  const dx = e.clientX - screenTouchStart.x;
  const dy = e.clientY - screenTouchStart.y;
  screenTouchStart = null;

  if (Math.hypot(dx, dy) > 20) {
    // Swipe detected
    if (playerState.justFrameActive) {
      tryTriggerStutterDash(new THREE.Vector3(dx, 0, dy));
    }
  }
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

  // Target lock ring animation
  if (playerState.attackTarget) {
    targetLockRing.position.set(
      playerState.attackTarget.position.x,
      0.08,
      playerState.attackTarget.position.z
    );
    targetLockRing.rotation.z += 0.03;
    targetLockRing.visible = true;
  } else {
    targetLockRing.visible = false;
  }

  // Movement Handling: 1. Joystick
  if (playerState.moveVector.lengthSq() > 0.01) {
    const moveDir = new THREE.Vector3(playerState.moveVector.x, 0, playerState.moveVector.y).normalize();
    const nextPos = playerGroup.position.clone().add(moveDir.multiplyScalar(playerState.moveSpeed * delta));

    if (!checkWallCollision(nextPos)) {
      playerGroup.position.copy(nextPos);
      playerGroup.lookAt(playerGroup.position.x + moveDir.x, playerGroup.position.y, playerGroup.position.z + moveDir.z);
    }
  }
  // Movement Handling: 2. Tap to Walk
  else if (playerState.targetWalkPos) {
    const dir = new THREE.Vector3().subVectors(playerState.targetWalkPos, playerGroup.position);
    dir.y = 0;
    if (dir.length() > 0.2) {
      dir.normalize();
      const nextPos = playerGroup.position.clone().add(dir.multiplyScalar(playerState.moveSpeed * delta));
      if (!checkWallCollision(nextPos)) {
        playerGroup.position.copy(nextPos);
        playerGroup.lookAt(playerGroup.position.x + dir.x, playerGroup.position.y, playerGroup.position.z + dir.z);
      } else {
        playerState.targetWalkPos = null;
      }
    } else {
      playerState.targetWalkPos = null;
    }
  }

  // Animate Physical Stone Walls Erupting & Sinking
  for (let i = activeWalls.length - 1; i >= 0; i--) {
    const wall = activeWalls[i];
    const age = now - wall.birthTime;

    // Erupt animation (rise quickly in 0.2s)
    if (wall.currentY < wall.targetY) {
      wall.currentY = Math.min(wall.targetY, wall.currentY + delta * 12.0);
      wall.group.position.y = wall.currentY;
    }

    // Decay animation (sink after duration)
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

  // Camera Follow Player Smoothly
  camera.position.x = THREE.MathUtils.lerp(camera.position.x, playerGroup.position.x, 0.08);
  camera.position.z = THREE.MathUtils.lerp(camera.position.z, playerGroup.position.z + 18, 0.08);
  camera.lookAt(playerGroup.position.x, 0, playerGroup.position.z);

  // Sync Overhead Health Bars
  updateHpBars();

  renderer.render(scene, camera);
}

animate();
