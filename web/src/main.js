import * as THREE from 'three';

// --- Scene, Camera, Renderer Setup ---
const container = document.getElementById('canvas-container');
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x0a0c14);
scene.fog = new THREE.FogExp2(0x0a0c14, 0.025);

const camera = new THREE.PerspectiveCamera(50, window.innerWidth / window.innerHeight, 0.1, 1000);
camera.position.set(0, 22, 16);
camera.lookAt(0, 0, 0);

const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
container.appendChild(renderer.domElement);

// --- Lighting ---
const ambientLight = new THREE.AmbientLight(0x404b69, 1.2);
scene.add(ambientLight);

const sunLight = new THREE.DirectionalLight(0x90caf9, 2.0);
sunLight.position.set(15, 30, 20);
sunLight.castShadow = true;
sunLight.shadow.mapSize.width = 1024;
sunLight.shadow.mapSize.height = 1024;
sunLight.shadow.camera.near = 0.5;
sunLight.shadow.camera.far = 80;
sunLight.shadow.camera.left = -25;
sunLight.shadow.camera.right = 25;
sunLight.shadow.camera.top = 25;
sunLight.shadow.camera.bottom = -25;
scene.add(sunLight);

// --- Arena Floor (Hexagonal Leyline Grid) ---
const floorGeo = new THREE.PlaneGeometry(60, 60);
const floorMat = new THREE.MeshStandardMaterial({
  color: 0x121520,
  roughness: 0.8,
  metalness: 0.2,
});
const floor = new THREE.Mesh(floorGeo, floorMat);
floor.rotation.x = -Math.PI / 2;
floor.receiveShadow = true;
scene.add(floor);

// Grid Helper
const gridHelper = new THREE.GridHelper(60, 30, 0x00f2fe, 0x1e293b);
gridHelper.position.y = 0.02;
scene.add(gridHelper);

// --- Leyline Spires (Glowing Hex Nodes) ---
const spirePositions = [
  new THREE.Vector3(-10, 0, -8),
  new THREE.Vector3(10, 0, -8),
  new THREE.Vector3(0, 0, 5),
];

spirePositions.forEach((pos, idx) => {
  const baseGeo = new THREE.CylinderGeometry(1.6, 2.0, 0.4, 6);
  const baseMat = new THREE.MeshStandardMaterial({ color: 0x2d3748, roughness: 0.4 });
  const base = new THREE.Mesh(baseGeo, baseMat);
  base.position.copy(pos);
  base.position.y = 0.2;
  scene.add(base);

  const crystalGeo = new THREE.OctahedronGeometry(0.8, 0);
  const crystalMat = new THREE.MeshStandardMaterial({
    color: 0x00f2fe,
    emissive: 0x00f2fe,
    emissiveIntensity: 0.8,
    roughness: 0.1
  });
  const crystal = new THREE.Mesh(crystalGeo, crystalMat);
  crystal.position.set(pos.x, 2.2, pos.z);
  crystal.castShadow = true;
  scene.add(crystal);

  // Animate crystal hovering
  crystal.userData = { initialY: 2.2, phase: idx * 2 };
});

// --- Entities ---
// 1. Player (Cyan Hero)
const playerGroup = new THREE.Group();
const bodyGeo = new THREE.CapsuleGeometry(0.6, 1.2, 8, 16);
const bodyMat = new THREE.MeshStandardMaterial({ color: 0x00f2fe, roughness: 0.3, metalness: 0.7 });
const playerMesh = new THREE.Mesh(bodyGeo, bodyMat);
playerMesh.position.y = 1.2;
playerMesh.castShadow = true;
playerGroup.add(playerMesh);

// Player Direction Visor
const visorGeo = new THREE.BoxGeometry(0.4, 0.25, 0.5);
const visorMat = new THREE.MeshBasicMaterial({ color: 0xffffff });
const visorMesh = new THREE.Mesh(visorGeo, visorMat);
visorMesh.position.set(0, 1.5, 0.5);
playerGroup.add(visorMesh);

// Selection Ring
const ringGeo = new THREE.RingGeometry(0.9, 1.1, 32);
const ringMat = new THREE.MeshBasicMaterial({ color: 0x00f2fe, side: THREE.DoubleSide });
const playerRing = new THREE.Mesh(ringGeo, ringMat);
playerRing.rotation.x = -Math.PI / 2;
playerRing.position.y = 0.05;
playerGroup.add(playerRing);

playerGroup.position.set(0, 0, 8);
scene.add(playerGroup);

// 2. Enemy Hero (Crimson PvP Target)
const enemyHeroGroup = new THREE.Group();
const enemyBodyMat = new THREE.MeshStandardMaterial({ color: 0xff3b30, roughness: 0.3, metalness: 0.6 });
const enemyMesh = new THREE.Mesh(bodyGeo, enemyBodyMat);
enemyMesh.position.y = 1.2;
enemyMesh.castShadow = true;
enemyHeroGroup.add(enemyMesh);

const enemyRing = new THREE.Mesh(ringGeo, new THREE.MeshBasicMaterial({ color: 0xff3b30, side: THREE.DoubleSide }));
enemyRing.rotation.x = -Math.PI / 2;
enemyRing.position.y = 0.05;
enemyHeroGroup.add(enemyRing);

enemyHeroGroup.position.set(0, 0, -5);
enemyHeroGroup.userData = { type: 'Hero', name: '敵方英雄 (PvP)', maxHp: 1000, hp: 1000 };
scene.add(enemyHeroGroup);

// 3. Minion (Golden PvE Target)
const minionGroup = new THREE.Group();
const minionGeo = new THREE.BoxGeometry(0.8, 0.8, 0.8);
const minionMat = new THREE.MeshStandardMaterial({ color: 0xffcc00, roughness: 0.5, metalness: 0.5 });
const minionMesh = new THREE.Mesh(minionGeo, minionMat);
minionMesh.position.y = 0.5;
minionMesh.castShadow = true;
minionGroup.add(minionMesh);
minionGroup.position.set(-6, 0, 0);
minionGroup.userData = { type: 'Minion', name: '地脈魔偶 (PvE)', maxHp: 300, hp: 300 };
scene.add(minionGroup);

// Targets list
const combatTargets = [enemyHeroGroup, minionGroup];

// --- Walls Storage ---
const activeWalls = [];

// --- Game State & Cadence Core ---
const CADENCE = {
  JUST_FRAME_MS: 120,      // 120ms 完美銜接窗口
  FLICK_MIN_DIST: 15,      // 15px 微彈指門檻
  FLICK_MAX_DIST: 80,      // 彈指上限
  DASH_DISTANCE: 3.2,      // 滑步衝刺距離
  ANTI_MASH_THRESHOLD: 3,  // 0.3秒內最多允許 2 次點擊
  ANTI_MASH_WINDOW: 300,
  JAM_PENALTY_MS: 250,     // 卡刀硬直時間
};

let playerState = {
  targetPos: playerGroup.position.clone(),
  isMoving: false,
  moveSpeed: 7.5,
  attackTarget: null,
  attackRange: 4.5,
  isAttacking: false,
  attackCooldown: 0,
  attackInterval: 0.85,
  
  // 走A 節奏窗口
  justFrameActive: false,
  justFrameStartTime: 0,
  lastTargetHit: null,
  comboCount: 0,

  // 防亂點卡刀
  clickTimestamps: [],
  isJammed: false,
  jammedUntil: 0,
};

// UI Elements
const cadenceRingEl = document.getElementById('cadence-ring');
const comboBannerEl = document.getElementById('combo-banner');
const jamAlertEl = document.getElementById('jam-alert');
const runeButtonEl = document.getElementById('rune-button');

// Raycaster & Coordinates
const raycaster = new THREE.Raycaster();
const mouse = new THREE.Vector2();

// --- Attack Visual Effect ---
function spawnSlashEffect(fromPos, toPos, isCrit) {
  const dir = new THREE.Vector3().subVectors(toPos, fromPos).normalize();
  const hitPoint = new THREE.Vector3().copy(toPos).add(new THREE.Vector3(0, 1.0, 0));

  const beamGeo = new THREE.CylinderGeometry(0.08, 0.18, hitPoint.distanceTo(fromPos), 8);
  const beamMat = new THREE.MeshBasicMaterial({ color: isCrit ? 0xffd700 : 0x00f2fe });
  const beam = new THREE.Mesh(beamGeo, beamMat);

  beam.position.copy(fromPos).lerp(hitPoint, 0.5);
  beam.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir);
  scene.add(beam);

  // Flash ring at impact
  const impactGeo = new THREE.SphereGeometry(0.35, 8, 8);
  const impactMat = new THREE.MeshBasicMaterial({ color: 0xffffff });
  const impact = new THREE.Mesh(impactGeo, impactMat);
  impact.position.copy(hitPoint);
  scene.add(impact);

  setTimeout(() => {
    scene.remove(beam);
    scene.remove(impact);
    beam.geometry.dispose();
    beam.material.dispose();
    impact.geometry.dispose();
    impact.material.dispose();
  }, 100);
}

// --- Spawn OBB Stone Wall ---
function spawnStoneWall(startPoint, endPoint) {
  const length = Math.max(startPoint.distanceTo(endPoint), 2.5);
  const mid = new THREE.Vector3().addVectors(startPoint, endPoint).multiplyScalar(0.5);
  const dir = new THREE.Vector3().subVectors(endPoint, startPoint).normalize();
  const angle = Math.atan2(dir.x, dir.z);

  const wallWidth = 1.0;
  const wallHeight = 2.4;

  const group = new THREE.Group();
  group.position.set(mid.x, 0, mid.z);
  group.rotation.y = angle;

  // 3 Pillar Blocks
  const segments = 3;
  const segLength = length / segments;
  for (let i = 0; i < segments; i++) {
    const geo = new THREE.BoxGeometry(segLength * 0.95, wallHeight, wallWidth);
    const mat = new THREE.MeshStandardMaterial({
      color: 0x4a5568,
      roughness: 0.9,
      metalness: 0.1,
    });
    const mesh = new THREE.Mesh(geo, mat);
    mesh.position.set((i - 1) * segLength, wallHeight / 2, 0);
    mesh.castShadow = true;
    mesh.receiveShadow = true;
    group.add(mesh);
  }

  scene.add(group);

  const wallObj = {
    group,
    mid,
    length,
    width: wallWidth,
    angle,
    birthTime: performance.now(),
    duration: 5000,
  };
  activeWalls.push(wallObj);

  // Dust VFX & Haptic
  if (navigator.vibrate) navigator.vibrate([40, 30, 40]);
}

// --- Wall Collision Detection ---
function checkWallCollision(newPos) {
  for (const wall of activeWalls) {
    const dx = newPos.x - wall.mid.x;
    const dz = newPos.z - wall.mid.z;
    // Rotate relative point back
    const cos = Math.cos(-wall.angle);
    const sin = Math.sin(-wall.angle);
    const localX = cos * dx - sin * dz;
    const localZ = sin * dx + cos * dz;

    const halfL = wall.length / 2 + 0.6; // player radius
    const halfW = wall.width / 2 + 0.6;

    if (Math.abs(localX) < halfL && Math.abs(localZ) < halfW) {
      return true; // Colliding with wall
    }
  }
  return false;
}

// --- Input Handling & Touch Gestures ---
let touchStartPoint = null;
let touchStartTime = 0;
let isAimingRune = false;
let runeVectorLine = null;
let runeAimStart = null;
let runeAimEnd = null;

function getGroundPoint(screenX, screenY) {
  mouse.x = (screenX / window.innerWidth) * 2 - 1;
  mouse.y = -(screenY / window.innerHeight) * 2 + 1;
  raycaster.setFromCamera(mouse, camera);
  const intersects = raycaster.intersectObject(floor);
  if (intersects.length > 0) {
    return intersects[0].point;
  }
  return null;
}

function getIntersectedTarget(screenX, screenY) {
  mouse.x = (screenX / window.innerWidth) * 2 - 1;
  mouse.y = -(screenY / window.innerHeight) * 2 + 1;
  raycaster.setFromCamera(mouse, camera);

  const meshes = [];
  combatTargets.forEach(tgt => tgt.traverse(child => {
    if (child.isMesh) {
      child.userData.parentEntity = tgt;
      meshes.push(child);
    }
  }));

  const intersects = raycaster.intersectObjects(meshes);
  if (intersects.length > 0) {
    return intersects[0].object.userData.parentEntity;
  }
  return null;
}

// --- Anti-Mashing Check ---
function checkAntiMashing() {
  const now = performance.now();
  playerState.clickTimestamps = playerState.clickTimestamps.filter(t => now - t < CADENCE.ANTI_MASH_WINDOW);
  playerState.clickTimestamps.push(now);

  if (playerState.clickTimestamps.length >= CADENCE.ANTI_MASH_THRESHOLD) {
    // Trigger Jam!
    playerState.isJammed = true;
    playerState.jammedUntil = now + CADENCE.JAM_PENALTY_MS;
    playerState.comboCount = 0;
    
    jamAlertEl.style.opacity = '1';
    setTimeout(() => { jamAlertEl.style.opacity = '0'; }, CADENCE.JAM_PENALTY_MS + 200);

    if (navigator.vibrate) navigator.vibrate(100);
    return true;
  }
  return false;
}

// --- Execute Attack ---
function executeAttack(target) {
  if (!target) return;
  playerState.isAttacking = true;
  playerState.attackCooldown = playerState.attackInterval;

  // Face target
  playerGroup.lookAt(target.position.x, playerGroup.position.y, target.position.z);

  // Visual beam
  spawnSlashEffect(playerGroup.position, target.position, false);

  // Open 120ms Just-Frame Window for Micro-Flick Stutter-Step
  playerState.justFrameActive = true;
  playerState.justFrameStartTime = performance.now();
  playerState.lastTargetHit = target;

  // Show UI ring on screen
  const screenPos = target.position.clone().project(camera);
  const screenX = (screenPos.x * 0.5 + 0.5) * window.innerWidth;
  const screenY = (-(screenPos.y * 0.5) + 0.5) * window.innerHeight;
  cadenceRingEl.style.left = `${screenX}px`;
  cadenceRingEl.style.top = `${screenY}px`;
  cadenceRingEl.style.opacity = '1';
  cadenceRingEl.style.transform = 'translate(-50%, -50%) scale(1.4)';

  setTimeout(() => {
    playerState.justFrameActive = false;
    cadenceRingEl.style.opacity = '0';
    cadenceRingEl.style.transform = 'translate(-50%, -50%) scale(0.5)';
  }, CADENCE.JUST_FRAME_MS);
}

// --- Micro-Flick Trigger ---
function tryTriggerMicroFlick(deltaX, deltaY) {
  const flickDist = Math.hypot(deltaX, deltaY);
  if (flickDist < CADENCE.FLICK_MIN_DIST || flickDist > CADENCE.FLICK_MAX_DIST) return false;

  const now = performance.now();
  const timeSinceHit = now - playerState.justFrameStartTime;

  if (playerState.justFrameActive && timeSinceHit <= CADENCE.JUST_FRAME_MS) {
    // Only Hero (PvP) permits Micro-Dash
    const target = playerState.lastTargetHit;
    if (target && target.userData.type === 'Hero') {
      // Dash in swipe direction
      const swipeAngle = Math.atan2(deltaY, deltaX);
      const forward = new THREE.Vector3(Math.cos(swipeAngle), 0, Math.sin(swipeAngle));
      const dashDest = playerGroup.position.clone().add(forward.multiplyScalar(CADENCE.DASH_DISTANCE));

      if (!checkWallCollision(dashDest)) {
        playerGroup.position.copy(dashDest);
        playerState.targetPos.copy(dashDest);
        playerState.isMoving = false;
        
        // Haptic feedback & combo increment
        if (navigator.vibrate) navigator.vibrate(25);
        playerState.comboCount++;
        comboBannerEl.innerText = `★ PERFECT CADENCE x${playerState.comboCount} ★`;
        comboBannerEl.style.opacity = '1';
        setTimeout(() => { comboBannerEl.style.opacity = '0'; }, 800);

        // Cancel remaining backswing
        playerState.attackCooldown = 0.15;
        playerState.justFrameActive = false;
        return true;
      }
    }
  }
  return false;
}

// --- Screen Events ---
window.addEventListener('pointerdown', (e) => {
  if (e.target.closest('#rune-button')) return;
  if (checkAntiMashing()) return;

  touchStartPoint = { x: e.clientX, y: e.clientY };
  touchStartTime = performance.now();

  const target = getIntersectedTarget(e.clientX, e.clientY);
  if (target) {
    playerState.attackTarget = target;
    playerState.isMoving = false;
  } else {
    const pt = getGroundPoint(e.clientX, e.clientY);
    if (pt) {
      playerState.attackTarget = null;
      playerState.targetPos.copy(pt);
      playerState.isMoving = true;
    }
  }
});

window.addEventListener('pointerup', (e) => {
  if (!touchStartPoint) return;
  const deltaX = e.clientX - touchStartPoint.x;
  const deltaY = e.clientY - touchStartPoint.y;

  // Check if this was a micro-flick
  tryTriggerMicroFlick(deltaX, deltaY);
  touchStartPoint = null;
});

// --- Rune Button & Vector Wall Aiming ---
const aimLineMat = new THREE.LineBasicMaterial({ color: 0x00f2fe, linewidth: 3 });
const aimLineGeo = new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), new THREE.Vector3()]);
runeVectorLine = new THREE.Line(aimLineGeo, aimLineMat);
runeVectorLine.visible = false;
scene.add(runeVectorLine);

runeButtonEl.addEventListener('pointerdown', (e) => {
  e.preventDefault();
  isAimingRune = true;
  runeAimStart = playerGroup.position.clone();
  runeAimEnd = playerGroup.position.clone();
  runeVectorLine.visible = true;
});

window.addEventListener('pointermove', (e) => {
  if (!isAimingRune) return;
  const pt = getGroundPoint(e.clientX, e.clientY);
  if (pt) {
    runeAimEnd = pt;
    const pts = [
      new THREE.Vector3(runeAimStart.x, 0.2, runeAimStart.z),
      new THREE.Vector3(runeAimEnd.x, 0.2, runeAimEnd.z)
    ];
    runeVectorLine.geometry.setFromPoints(pts);
  }
});

window.addEventListener('pointerup', (e) => {
  if (!isAimingRune) return;
  isAimingRune = false;
  runeVectorLine.visible = false;
  if (runeAimStart && runeAimEnd && runeAimStart.distanceTo(runeAimEnd) > 1.5) {
    spawnStoneWall(runeAimStart, runeAimEnd);
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
  const delta = clock.getDelta();
  const now = performance.now();

  // Handle Jam
  if (playerState.isJammed) {
    if (now > playerState.jammedUntil) {
      playerState.isJammed = false;
    } else {
      renderer.render(scene, camera);
      return;
    }
  }

  // Attack cooldown countdown
  if (playerState.attackCooldown > 0) {
    playerState.attackCooldown -= delta;
  }

  // Combat Targeting & Auto-attack
  if (playerState.attackTarget) {
    const dist = playerGroup.position.distanceTo(playerState.attackTarget.position);
    if (dist <= playerState.attackRange) {
      playerState.isMoving = false;
      if (playerState.attackCooldown <= 0) {
        executeAttack(playerState.attackTarget);
      }
    } else {
      // Approach target
      playerState.targetPos.copy(playerState.attackTarget.position);
      playerState.isMoving = true;
    }
  }

  // Movement Logic
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

  // Camera Follow
  camera.position.x = THREE.MathUtils.lerp(camera.position.x, playerGroup.position.x, 0.08);
  camera.position.z = THREE.MathUtils.lerp(camera.position.z, playerGroup.position.z + 16, 0.08);
  camera.lookAt(playerGroup.position.x, 0, playerGroup.position.z);

  // Animate Leyline Crystals
  scene.traverse((obj) => {
    if (obj.userData && obj.userData.initialY !== undefined) {
      obj.rotation.y += 0.02;
      obj.position.y = obj.userData.initialY + Math.sin(now * 0.003 + obj.userData.phase) * 0.25;
    }
  });

  // Wall Lifespan and Decay Sink Animation
  for (let i = activeWalls.length - 1; i >= 0; i--) {
    const wall = activeWalls[i];
    const age = now - wall.birthTime;
    if (age > wall.duration) {
      wall.group.position.y -= delta * 3.0;
      if (wall.group.position.y < -3.0) {
        scene.remove(wall.group);
        activeWalls.splice(i, 1);
      }
    }
  }

  renderer.render(scene, camera);
}

animate();
