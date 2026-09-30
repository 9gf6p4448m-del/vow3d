using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Tests.PlayMode
{
    // 凍結 V0140 §9-B；固定幀窗口，所有操作使用正式輸入或計畫明定的五個 editor-only 入口。
    public sealed class CanyonPlayTests
    {
        private Phase1Bootstrap _b;
        private HeroController _hero;
        private TrainingOpponent _red;
        private HeroLocomotion _move, _redMove;
        private CanyonTerrainSpec T => CanyonTerrainSpec.V0140;
        private int _oldWidth, _oldHeight;
        private float _oldCaptureDt;
        private int _revealOffset;

        [SetUp] public void SetUp()
        {
            _oldWidth = Screen.width; _oldHeight = Screen.height;
            _oldCaptureDt = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
        }
        [TearDown] public void TearDown()
        {
            Time.captureDeltaTime = _oldCaptureDt;
            Screen.SetResolution(_oldWidth, _oldHeight, false);
        }
        private IEnumerator Load()
        {
            SceneManager.LoadScene("VOW_Phase1_Greybox", LoadSceneMode.Single);
            yield return null; yield return null;
            _b = UnityEngine.Object.FindObjectOfType<Phase1Bootstrap>();
            _hero = UnityEngine.Object.FindObjectOfType<HeroController>();
            _red = UnityEngine.Object.FindObjectOfType<TrainingOpponent>();
            Assert.IsNotNull(_b); Assert.IsNotNull(_hero); Assert.IsNotNull(_red);
            _move = _hero.GetComponent<HeroLocomotion>();
            _redMove = _red.GetComponent<HeroLocomotion>();
        }
        private IEnumerator StartCapture()
        {
            yield return Load();
            TapCapture();
            Assert.AreEqual(CaptureMatchState.Lobby, _b.CaptureState);
            for (int i = 0; i < 30; i++) yield return null;
            Tap(_red.transform.position + Vector3.up);
            Assert.AreEqual(CaptureMatchState.Active, _b.CaptureState);
            yield return null;
        }
        private void TapCapture()
        {
            Assert.IsTrue(_b.TryGetCaptureButtonScreenPoint(out float x, out float y));
            AssertScreen(new Vector3(x, y, 1f));
            _b.WorldTapInput.SendScreenTap(x, y);
        }
        private static void AssertScreen(Vector3 p)
        {
            Assert.Greater(p.z, 0f);
            Assert.That(p.x, Is.InRange(16f, Screen.width - 16f), "點擊必須離左右邊界16px");
            Assert.That(p.y, Is.InRange(16f, Screen.height - 16f), "點擊必須離上下邊界16px");
        }
        private void Tap(Vector3 world)
        {
            Vector3 p = Camera.main.WorldToScreenPoint(world); AssertScreen(p);
            _b.WorldTapInput.SendScreenTap(p.x, p.y);
            Debug.Log("CanyonTap screen="+p+" target="+(_hero.CurrentTarget==null?"none":_hero.CurrentTarget.GetType().Name));
        }
        private IEnumerator Settle(int frames = 30)
        { for (int i = 0; i < frames; i++) yield return null; }
        private static float Distance(Vector3 a, Vector3 b)
        { return new Vector2(a.x-b.x, a.z-b.z).magnitude; }
        private static bool InTwo(Vector3 p)
        { return Distance(p, new Vector3(6.5625f, 1f, 3.7890625f)) <= 2.5f; }
        private void StepOnTerrain(Vector3 before, Vector3 after)
        {
            Assert.IsFalse(T.CrossesCliff(before.x, before.z, after.x, after.z), "相鄰幀不得穿崖");
            Assert.AreEqual(T.HeightAt(after.x, after.z, 0), after.y, 1e-3f, "每幀高度必須跟隨地形");
        }
        private static bool InRamp(Vector3 p, int ramp)
        {
            float cx, cz, ax, az;
            switch (ramp)
            {
                case 0: cx=9.84375f; cz=5.68359375f; ax=-0.86601f; az=-0.50002f; break;
                case 1: cx=9.84375f; cz=-5.68359375f; ax=-0.86601f; az=0.50002f; break;
                case 4: cx=0f; cz=11.3671875f; ax=0f; az=1f; break;
                default: cx=0f; cz=-11.3671875f; ax=0f; az=-1f; break;
            }
            float dx=p.x-cx, dz=p.z-cz;
            return Mathf.Abs(dx*ax+dz*az)<=2f && Mathf.Abs(-dx*az+dz*ax)<=1.75f;
        }
        private void KeepRedDown()
        { if (_red.IsAlive) _red.ReceiveDamage(1000f, DamageType.Physical, _hero.gameObject); }
        private void KeepHeroDown()
        { if (_hero.IsAlive) _hero.TakeDuelDamage(1000f); }
        private void Owners(int side = 2, int tile = -1, int tileSide = 2)
        {
            var owners = new int[19];
            for (int i=0;i<19;i++) owners[i]=side;
            if(tile>=0) owners[tile]=tileSide;
            _b.SeedCaptureOwnershipForTest(owners);
        }
        private Transform Root(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == name) return root.transform;
            Assert.Fail("缺少獨立根物件 " + name); return null;
        }
        private static bool InsideProjection(BoxCollider box, float x, float z)
        {
            Vector3 local=box.transform.InverseTransformPoint(new Vector3(x,box.transform.position.y,z))-box.center;
            return Mathf.Abs(local.x)<=box.size.x*0.5f && Mathf.Abs(local.z)<=box.size.z*0.5f;
        }

        [UnityTest] public IEnumerator B01_CollidersAndHexProjection_MatchTerrain()
        {
            yield return Load();
            Transform terrain=Root("CanyonTerrain"), cliffs=Root("CliffBarriers");
            Assert.IsFalse(cliffs.IsChildOf(terrain));
            BoxCollider[] boxes=terrain.GetComponentsInChildren<BoxCollider>(true);
            Assert.AreEqual(67,boxes.Length);
            Assert.AreEqual(67,terrain.GetComponentsInChildren<Collider>(true).Length);
            BoxCollider[] walls=cliffs.GetComponentsInChildren<BoxCollider>(true);
            Assert.AreEqual(44,walls.Length);
            Assert.AreEqual(0,UnityEngine.Object.FindObjectsOfType<MeshCollider>(true).Length);
            Assert.AreEqual(0,UnityEngine.Object.FindObjectsOfType<NavMeshObstacle>(true).Length); // 只查詢，不建立障礙元件
            // 啟用後 bounds 才有幾何意義；不使用預期名猜 collider。
            TapCapture(); yield return null;
            for(int i=0;i<44;i++)
            {
                T.GetCliffSegment(i,out float x0,out float z0,out float x1,out float z1);
                BoxCollider wall=walls[i];
                Vector3 center=wall.transform.TransformPoint(wall.center);
                Vector3 half=wall.transform.TransformVector(Vector3.forward*wall.size.z*0.5f);
                Vector3 p0=center-half,p1=center+half;
                float direct=Distance(p0,new Vector3(x0,0,z0))+Distance(p1,new Vector3(x1,0,z1));
                float reverse=Distance(p0,new Vector3(x1,0,z1))+Distance(p1,new Vector3(x0,0,z0));
                if(reverse<direct){Vector3 swap=p0;p0=p1;p1=swap;}
                Assert.LessOrEqual(Distance(p0,new Vector3(x0,0,z0)),1e-3f,"崖壁端0第"+i);
                Assert.LessOrEqual(Distance(p1,new Vector3(x1,0,z1)),1e-3f,"崖壁端1第"+i);
                Assert.LessOrEqual(wall.bounds.min.y,-2f); Assert.GreaterOrEqual(wall.bounds.max.y,4f);
                Assert.AreEqual(LayerMask.NameToLayer("Ignore Raycast"),wall.gameObject.layer);
            }
            float[] xs={0,0,6.5625f,6.5625f,0,-6.5625f,-6.5625f,0,6.5625f,13.125f,13.125f,13.125f,6.5625f,0,-6.5625f,-13.125f,-13.125f,-13.125f,-6.5625f};
            float[] zs={0,7.578125f,3.7890625f,-3.7890625f,-7.578125f,-3.7890625f,3.7890625f,15.15625f,11.3671875f,7.578125f,0,-7.578125f,-11.3671875f,-15.15625f,-11.3671875f,-7.578125f,0,7.578125f,11.3671875f};
            int checkedPoints=0,insidePoints=0,outsidePoints=0;
            for(int tile=0;tile<19;tile++) for(int ix=0;ix<21;ix++) for(int iz=0;iz<21;iz++)
            {
                float x=xs[tile]-4.5f+ix*0.45f,z=zs[tile]-4.5f+iz*0.45f;
                // 略過離六角邊界1e-4內的點，四向探針都同塊才計入。
                int at=_b.CaptureSpec.TileAt(x,z);
                if(_b.CaptureSpec.TileAt(x+1e-4f,z)!=at || _b.CaptureSpec.TileAt(x-1e-4f,z)!=at ||
                   _b.CaptureSpec.TileAt(x,z+1e-4f)!=at || _b.CaptureSpec.TileAt(x,z-1e-4f)!=at) continue;
                bool projected=false;
                for(int k=0;k<3;k++) projected|=InsideProjection(boxes[tile*3+k],x,z);
                Assert.AreEqual(at==tile,projected,"六角投影 tile="+tile+" x="+x+" z="+z);
                checkedPoints++; if(projected)insidePoints++;else outsidePoints++;
            }
            Assert.Greater(checkedPoints,7000); Assert.Greater(insidePoints,0); Assert.Greater(outsidePoints,0);
        }
        [UnityTest] public IEnumerator B02_ModeSwitch_StampsAndUnstampsExactlyOnce()
        {
            yield return Load();
            Transform terrain=Root("CanyonTerrain"),cliffs=Root("CliffBarriers");
            Collider ground=GameObject.Find("Ground_40x40").GetComponent<Collider>();
            TestWallTarget[] initialWalls=UnityEngine.Object.FindObjectsOfType<TestWallTarget>();
            Assert.AreEqual(2,initialWalls.Length);Assert.AreEqual(396,_b.NavGrid.BlockedCount);
            foreach(TestWallTarget wall in initialWalls)Assert.IsTrue(wall.IsAlive);
            Assert.IsFalse(terrain.gameObject.activeSelf); Assert.IsFalse(cliffs.gameObject.activeSelf);
            Assert.IsTrue(ground.enabled); Assert.AreEqual(396,_b.NavGrid.BlockedCount); // 316 邊界 + 80 存活測試牆，凍結條文允許計入。
            TapCapture(); yield return null;
            Assert.IsTrue(terrain.gameObject.activeSelf); Assert.IsTrue(cliffs.gameObject.activeSelf);
            Assert.IsFalse(ground.enabled); Assert.AreEqual(1080,_b.NavGrid.BlockedCount);
            TapCapture(); yield return null;
            Assert.AreEqual(CaptureMatchState.Off,_b.CaptureState);
            Assert.IsFalse(terrain.gameObject.activeSelf); Assert.IsFalse(cliffs.gameObject.activeSelf);
            Assert.IsTrue(ground.enabled); Assert.AreEqual(396,_b.NavGrid.BlockedCount);
            Assert.AreEqual(0,_b.NavGrid.NegativeStampCount); Assert.AreEqual(0f,_hero.transform.position.y,1e-3f);
        }
        [UnityTest] public IEnumerator B03_R5_HeightAndNavMeshFollowEveryFrame()
        {
            yield return StartCapture(); _b.HoldOpponentForTest(true);
            Vector3 dest=new Vector3(0,-1,-8.3671875f);
            Tap(dest); int rampFrames=0; bool arrived=false;
            NavMeshAgent agent=_hero.GetComponent<NavMeshAgent>();
            for(int f=1;f<=360;f++)
            {
                yield return null; Vector3 p=_hero.transform.position;
                Assert.AreEqual(T.HeightAt(p.x,p.z,0),p.y,1e-3f); Assert.IsTrue(agent.isOnNavMesh);
                if(InRamp(p,5))rampFrames++;
                if(Distance(p,dest)<=0.05f){arrived=true;break;}
            }
            Assert.IsTrue(arrived,"360幀內抵達"); Assert.GreaterOrEqual(rampFrames,20);
            Assert.AreEqual(-1f,_hero.transform.position.y,1e-3f);
        }
        private IEnumerator RouteToTwo(float dt,int window,int earliest,bool requireRamps)
        {
            yield return StartCapture(); Time.captureDeltaTime=dt;
            _move.WarpTo(new Vector3(0,-1,0)); KeepRedDown();
            for(int f=0;f<30;f++){yield return null;KeepRedDown();}
            Tap(new Vector3(6.5625f,1,3.7890625f));
            Vector3 before=_hero.transform.position; int firstR4=-1,firstR0=-1; bool arrived=false;
            for(int f=1;f<=window;f++)
            {
                yield return null; KeepRedDown(); Vector3 p=_hero.transform.position; StepOnTerrain(before,p);before=p;
                if(InRamp(p,4)&&firstR4<0)firstR4=f; if(InRamp(p,0)&&firstR0<0)firstR0=f;
                if(f<earliest)Assert.IsFalse(InTwo(p),"不可走直線提前進圈 f="+f);
                if(InTwo(p)){arrived=true;if(earliest>0)break;}
            }
            Assert.IsTrue(arrived,"固定窗口內必須抵達2號光圈");
            if(earliest==0)Assert.IsTrue(InTwo(_hero.transform.position),"低fps第45幀必在2號光圈");
            if(requireRamps){Assert.Greater(firstR4,0);Assert.Greater(firstR0,firstR4);}
        }
        [UnityTest] public IEnumerator B04_HeroRoutesViaR4ThenR0_WithoutCrossingCliffs()
        { yield return RouteToTwo(1f/60f,600,198,true); }
        [UnityTest] public IEnumerator B05a_ThreeFps_RouteArrivesWithin45Frames()
        { yield return RouteToTwo(1f/3f,45,0,false); }
        [UnityTest] public IEnumerator B05a_FourFps_RouteArrivesWithin45Frames()
        { yield return RouteToTwo(1f/4f,45,0,false); }
        private IEnumerator ThreeFlicks(float dt,bool cliff)
        {
            yield return StartCapture(); Time.captureDeltaTime=dt; _b.HoldOpponentForTest(true);
            _move.WarpTo(cliff?new Vector3(4.59375f,1,2.0703125f):new Vector3(3.25f,-1,0));
            // 同樓 held red 提供合法普攻；後續滑步仍朝凍結指定的崖邊方向。
            _redMove.WarpTo(cliff?new Vector3(6.5625f,1,3.7890625f):new Vector3(2f,-1,0));
            yield return Settle();
            int flicks=0;_b.InputService.OnCadenceVectorFlicked+=_=>flicks++;
            int accepted=0;float acceptedDistance=0f;float[] expectedDistances={1.4f,0.9f,0.5f};
            _hero.Mover.OnDashExecuted+=distance=>{accepted++;acceptedDistance=distance;};
            // 真動畫命中的公開通知發生於 Release；低fps一幀長於窗口，不能以幀輪詢錯過它。
            Action<ICombatTarget> flickOnHit=target=>
            {
                if(accepted>=3)return;
                Assert.AreSame(_red,target);
                Assert.AreEqual(PlayerState.AttackRelease,_hero.StateMachine.CurrentState);
                _move.FaceDirection(cliff?Vector3.left:Vector3.right);
                Vector3 p=Camera.main.WorldToScreenPoint(_hero.transform.position);
                AssertScreen(p);Vector3 end=p+(cliff?Vector3.left:Vector3.right)*120f;AssertScreen(end);
                _b.BeginScreenHold(p.x,p.y);_b.MoveScreenHold(end.x,end.y);_b.EndScreenHold();
            };
            _hero.OnAttackHitResolved+=flickOnHit;
            Tap(_red.transform.position+Vector3.up);
            Assert.AreSame(_red,_hero.CurrentTarget,"真實點選必須鎖定同樓對手");
            Vector3 before=_hero.transform.position;Vector3 origin=before;
            for(int dash=0;dash<3;dash++)
            {
                int hitFrames=0;
                    while(accepted<dash+1&&hitFrames<8)
                    {
                        yield return null;hitFrames++;
                        Vector3 observed=_hero.transform.position;StepOnTerrain(before,observed);before=observed;
                        Assert.AreEqual(cliff?1f:-1f,observed.y,1e-3f);
                        if(cliff)Assert.AreEqual(TerrainClass.Cliff,T.ClassAt(observed.x,observed.z,0));
                    }
                Assert.AreEqual(dash+1,flicks,"真實手勢需發出微滑步");
                Assert.AreEqual(dash+1,accepted,"每次真實滑步必須被接受");
                Assert.AreEqual(expectedDistances[dash],acceptedDistance,1e-5f,"三次滑步距離字面值");
                // 0.12秒微滑步；兩種低fps皆在下一幀落定，連段間隔保持小於1秒。
                for(int f=0;f<1;f++)
                {
                    yield return null;Vector3 now=_hero.transform.position;StepOnTerrain(before,now);before=now;
                    Assert.AreEqual(cliff?1f:-1f,now.y,1e-3f);
                    if(cliff)Assert.AreEqual(TerrainClass.Cliff,T.ClassAt(now.x,now.z,0));
                }
            }
            _hero.OnAttackHitResolved-=flickOnHit;
            Assert.Greater(Distance(origin,before),0.05f,"活性：三次滑步確實位移");
            if(cliff)
            {
                float nearest=float.MaxValue;
                // 0|2 線段為 (4.375,0)→(2.1875,3.7890625)。
                Vector2 a=new Vector2(4.375f,0),v=new Vector2(-2.1875f,3.7890625f),q=new Vector2(before.x,before.z)-a;
                nearest=(q-v*Mathf.Clamp01(Vector2.Dot(q,v)/v.sqrMagnitude)).magnitude;
                Assert.LessOrEqual(nearest,0.55f,"活性：已貼到崖壁");
            }
        }
        [UnityTest] public IEnumerator B05b_ThreeFps_ValleyFlicksCannotCrossCliff(){yield return ThreeFlicks(1f/3f,false);}
        [UnityTest] public IEnumerator B05b_FourFps_ValleyFlicksCannotCrossCliff(){yield return ThreeFlicks(1f/4f,false);}
        [UnityTest] public IEnumerator B05d_ThreeFps_CliffFlicksCannotDropIntoValley(){yield return ThreeFlicks(1f/3f,true);}
        [UnityTest] public IEnumerator B05d_FourFps_CliffFlicksCannotDropIntoValley(){yield return ThreeFlicks(1f/4f,true);}
        [UnityTest] public IEnumerator B05c_RuneEjection_StaysOnSameFloor_WithUnrestrictedControl()
        {
            yield return StartCapture(); _b.HoldOpponentForTest(true);
            Vector3 p0=new Vector3(3.875f,-1,0);_move.WarpTo(p0);
            _b.SpawnRuneWallForTest(0,3.625f,0,1,0);
            Vector3 p=_hero.transform.position;
            Assert.Greater(Distance(p,p0),0f,"活性：英雄實際被推出"); StepOnTerrain(p0,p);
            Assert.AreEqual(-1f,p.y,1e-3f);Assert.AreEqual(-1f,T.HeightAt(p.x,p.z,0));
            Assert.IsTrue(_b.NavGrid.TryFindNearestFree(3.875f,0,20,out int cx,out int cz));
            _b.NavGrid.CellCenter(cx,cz,out float x,out float z);
            Assert.AreEqual(1f,T.HeightAt(x,z,0),"活性：無條件搜尋會選到崖台");
        }
        [UnityTest] public IEnumerator B06_RedRoutesToTwo_ViaValleyAndCliffRamps()
        {
            yield return StartCapture();Owners(1,2,2);
            _move.WarpTo(new Vector3(-17,0,-17));KeepHeroDown();_redMove.WarpTo(new Vector3(0,-1,0));
            Vector3 before=_red.transform.position;bool valley=false,cliff=false,channel=false;
            for(int f=1;f<=900;f++)
            {
                yield return null;KeepHeroDown();Vector3 p=_red.transform.position;StepOnTerrain(before,p);before=p;
                valley|=InRamp(p,4)||InRamp(p,5);cliff|=InRamp(p,0)||InRamp(p,1);
                if(f<272)Assert.IsFalse(InTwo(p));
                if(InTwo(p)&&_b.CaptureView.RedChannelProgress>0f){channel=true;break;}
            }
            Assert.IsTrue(channel,"900幀內進圈且開始引導");Assert.IsTrue(valley);Assert.IsTrue(cliff);
        }
        [UnityTest] public IEnumerator B06_RedOnVent180Frames_DoesNotLaunch_ButHeroDoes()
        {
            yield return StartCapture();_b.HoldOpponentForTest(true);
            _move.WarpTo(new Vector3(-17,0,-17));_redMove.WarpTo(new Vector3(2.09375f,-1,2.0625f));
            for(int f=1;f<=180;f++)
            {
                yield return null; Assert.LessOrEqual(Distance(_red.transform.position,new Vector3(2.09375f,-1,2.0625f)),0.375f);
                Assert.AreEqual(-1f,_red.transform.position.y,1e-3f);Assert.AreEqual(0,_b.VentLogic.LaunchCount);
            }
            _redMove.WarpTo(new Vector3(-17,0,17));_move.WarpTo(new Vector3(2.09375f,-1,2.0625f));
            for(int f=1;f<=38;f++)yield return null;
            Assert.AreEqual(1,_b.VentLogic.LaunchCount,"活性：同踏點英雄會觸發");
        }
        [UnityTest] public IEnumerator B07_LaunchLanding_InputLock_AndEightSecondCooldown()
        {
            yield return StartCapture();KeepRedDown();_move.WarpTo(new Vector3(2.09375f,-1,2.0625f));
            int launch=-1;
            for(int f=1;f<=38;f++)
            {
                yield return null;KeepRedDown();
                if(f==35){Assert.AreEqual(0,_b.VentLogic.LaunchCount);Assert.AreEqual(-1f,_hero.transform.position.y,1e-3f);}
                if(_b.VentLogic.LaunchCount==1){launch=f;break;}
            }
            Assert.That(launch,Is.InRange(36,38));int landed=-1;
            for(int f=1;f<=32;f++)
            {
                yield return null;KeepRedDown();
                if(f==10)Tap(new Vector3(6.5625f,1,3.7890625f));
                if(!_move.IsVentFlying){landed=f;break;}
            }
            Assert.That(landed,Is.InRange(30,32));Vector3 landing=new Vector3(4.59375f,1,2.0703125f);
            Assert.LessOrEqual(Distance(_hero.transform.position,landing),0.05f);Assert.AreEqual(1f,_hero.transform.position.y,1e-3f);
            Assert.AreEqual(TerrainClass.Cliff,T.ClassAt(_hero.transform.position.x,_hero.transform.position.z,0));
            for(int f=1;f<=5;f++){yield return null;KeepRedDown();Assert.LessOrEqual(Distance(_hero.transform.position,landing),0.05f,"飛行點地無效");}
            _move.WarpTo(new Vector3(2.09375f,-1,2.0625f));_move.Stop();
            int sinceLaunch=landed+5;
            for(int f=sinceLaunch+1;f<=530;f++)
            {yield return null;KeepRedDown();if(f<510)Assert.AreEqual(1,_b.VentLogic.LaunchCount);if(_b.VentLogic.LaunchCount==2)break;}
            Assert.AreEqual(2,_b.VentLogic.LaunchCount,"530幀內第二次發射");
        }
        [UnityTest] public IEnumerator B07_LandingInsideLiveWall_EjectsOnCliffFloor()
        {
            yield return StartCapture();KeepRedDown();
            Vector3 landing=new Vector3(4.59375f,1f,2.0703125f);
            _b.SpawnRuneWallForTest(0,landing.x,landing.z,1f,0f);
            Assert.IsTrue(_b.NavGrid.TryWorldToCell(landing.x,landing.z,out int lx,out int lz));
            Assert.IsTrue(_b.NavGrid.IsBlocked(lx,lz),"活性：落點已被存活牆蓋住");
            _move.WarpTo(new Vector3(2.09375f,-1f,2.0625f));
            for(int f=1;f<=38&&_b.VentLogic.LaunchCount==0;f++){yield return null;KeepRedDown();}
            Assert.AreEqual(1,_b.VentLogic.LaunchCount);Assert.IsTrue(_move.IsVentFlying);
            for(int f=1;f<=32&&_move.IsVentFlying;f++){yield return null;KeepRedDown();}
            Assert.IsFalse(_move.IsVentFlying);
            Vector3 actual=_hero.transform.position;
            Assert.Greater(Distance(actual,landing),0.01f,"活性：落地時確實被推出牆");
            Assert.AreEqual(1f,actual.y,1e-3f);
            Assert.AreEqual(TerrainClass.Cliff,T.ClassAt(actual.x,actual.z,0));
            Assert.IsFalse(T.CrossesCliff(landing.x,landing.z,actual.x,actual.z));
            Assert.IsTrue(_b.NavGrid.TryWorldToCell(actual.x,actual.z,out int cx,out int cz));
            Assert.IsFalse(_b.NavGrid.IsBlocked(cx,cz));
            Assert.IsTrue(_b.NavGrid.IsBlocked(lx,lz),"落地時牆仍存活，不能靠牆到期假綠");
        }
        [UnityTest] public IEnumerator B07_DamageAt20_InterruptsVentUntilAfter55()
        {
            yield return StartCapture();KeepRedDown();_move.WarpTo(new Vector3(2.09375f,-1,2.0625f));
            float health=_hero.Health;int launch=-1;
            for(int f=1;f<=64;f++)
            {
                yield return null;KeepRedDown();
                if(f==20){_hero.TakeDuelDamage(1f);Assert.Less(_hero.Health,health);}
                if(f<=55)Assert.AreEqual(0,_b.VentLogic.LaunchCount);
                if(_b.VentLogic.LaunchCount==1){launch=f;break;}
            }
            Assert.That(launch,Is.InRange(56,64));
        }
        private IEnumerator HeroRange(bool cliff)
        {
            yield return StartCapture();_b.HoldOpponentForTest(true);
            _move.WarpTo(cliff?new Vector3(4.59375f,1,2.0703125f):new Vector3(13.125f,0,0));
            _redMove.WarpTo(cliff?new Vector3(0,-1,0):new Vector3(8.0859375f,1,0));
            Owners(0);yield return Settle();Vector3 origin=_hero.transform.position;float health=_red.Health;
            Tap(_red.transform.position+Vector3.up);
            Assert.AreSame(_red,_hero.CurrentTarget,"真實點擊必須鎖到對手，不能只驗輸入送出");
            bool hit=false;float maximum=0;
            for(int f=1;f<=(cliff?90:300);f++)
            {
                yield return null;maximum=Mathf.Max(maximum,Distance(origin,_hero.transform.position));
                if(cliff)Assert.LessOrEqual(maximum,0.02f,"崖台5.5m射程應原地命中");
                if(_red.Health<health){hit=true;break;}
            }
            Assert.IsTrue(hit,"固定窗口內第一次實際扣血");
            if(!cliff)Assert.GreaterOrEqual(maximum,0.03f,"平原不可拿目標崖台射程加成");
        }
        [UnityTest] public IEnumerator B08a_CliffHero_HitsWithoutMoving(){yield return HeroRange(true);}
        [UnityTest] public IEnumerator B08b_PlainHero_MustMoveBeforeHit(){yield return HeroRange(false);}
        private IEnumerator RedRange(bool cliff)
        {
            yield return StartCapture();
            _move.WarpTo(cliff?new Vector3(2.8125f,-1,1.625f):new Vector3(13.125f,0,0));
            _redMove.WarpTo(cliff?new Vector3(4.457948f,1,2.575f):new Vector3(13.125f,0,1.9f));
            Owners(1);Vector3 origin=_red.transform.position;bool warned=false;
            for(int f=1;f<=120;f++)
            {
                yield return null;
                if(_red.IsWarning){warned=true;break;}
            }
            Assert.IsTrue(warned,"120幀內對手前搖，活性：非hold且能看英雄");
            float moved=Distance(origin,_red.transform.position);
            if(cliff)Assert.LessOrEqual(moved,0.05f,"紅方崖台也必須有1.98m出手距離");
            else Assert.GreaterOrEqual(moved,0.05f,"平原1.8m出手必須先靠近");
        }
        [UnityTest] public IEnumerator B08c_CliffRed_WindsUpWithoutMoving(){yield return RedRange(true);}
        [UnityTest] public IEnumerator B08d_PlainRed_MovesBeforeWindup(){yield return RedRange(false);}
        private Renderer RedRenderer => _red.GetComponentInChildren<Renderer>(true);
        private IEnumerator VisibilityWindow(bool expected,bool tapHidden=false)
        {
            for(int f=1;f<=60;f++)
            {
                yield return null;
                if(f>=2)Assert.AreEqual(expected,RedRenderer.enabled,"renderer持續至60幀 f="+f);
                if(f==30&&tapHidden)
                {
                    Tap(_red.transform.position+Vector3.up);Assert.IsNull(_hero.CurrentTarget,"暗區點擊不可鎖定");
                    _hero.CancelCombatForDuel(); // 隱形點擊會落到地面，清除該移動以維持凍結觀看座標。
                }
            }
        }
        [UnityTest] public IEnumerator B09_VisibilityRendererTargetAndRedConsumer_Agree()
        {
            yield return StartCapture();_b.HoldOpponentForTest(true);Owners();
            _move.WarpTo(new Vector3(0,-1,0));_redMove.WarpTo(new Vector3(4.59375f,1,2.0703125f));
            yield return VisibilityWindow(false,true);
            Owners(2,2,0);yield return null;Assert.IsTrue(RedRenderer.enabled,"己方2號真視野下一幀揭露");
            Owners();_move.WarpTo(new Vector3(6.5625f,1,3.7890625f));_redMove.WarpTo(new Vector3(13.5f,0,5));
            yield return VisibilityWindow(true);
            _move.WarpTo(new Vector3(13.125f,0,0));_redMove.WarpTo(new Vector3(13.125f,0,7.04f));
            yield return VisibilityWindow(false);
            _move.WarpTo(new Vector3(4.59375f,1,2.0703125f));_redMove.WarpTo(new Vector3(0,-1,0));
            for(int f=1;f<=60;f++){yield return null;if(f>=2)Assert.IsFalse(_red.CanSeeHero());}
            Owners(2,2,1);yield return null;Assert.IsTrue(_red.CanSeeHero(),"活性：紅方持有2號會看見英雄");
        }
        private void AssertWater(float radius)
        {
            Assert.AreEqual(1,_b.ElementField.ActiveZoneCount,"活性：確有新水域");
            Assert.IsTrue(_b.ElementField.TryGetZoneBySlot(0,out ElementZone zone));
            Assert.AreEqual(ElementZoneKind.Water,zone.Kind);Assert.AreEqual(radius,zone.Radius,1e-4f);
        }
        [UnityTest] public IEnumerator B10_WaterHudAndDirectCast_BothUseCanyonRadius()
        {
            yield return StartCapture();_b.HoldOpponentForTest(true);
            _move.WarpTo(new Vector3(0,-1,-10.578125f));_move.FaceDirection(Vector3.forward);_b.PressElementWaterButton();AssertWater(4f);
            yield return StartCapture();_b.HoldOpponentForTest(true);_move.WarpTo(new Vector3(0,0,-18.15625f));_move.FaceDirection(Vector3.forward);
            _b.PressElementWaterButton();AssertWater(3f);
            yield return StartCapture();_b.HoldOpponentForTest(true);
            _b.ElementField.CastWater(new Vector3(0,-1,-7.578125f),0);AssertWater(4f);
        }
        private void MoveRedAwayAndHold()
        { _redMove.WarpTo(new Vector3(-17,0,-17));_b.HoldOpponentForTest(true); }
        private void AssertRoute(params int[] expected)
        {
            int[] route=new int[19];int count=_b.VanguardTarget.CopyRouteForTest(route);
            Assert.AreEqual(expected.Length,count);
            for(int i=0;i<count;i++)Assert.AreEqual(expected[i],route[i],"路線不得包含起點，index="+i);
        }
        [UnityTest] public IEnumerator B11_CoreAndBehemoth_UseValleyAndRampRoute()
        {
            yield return StartCapture();MoveRedAwayAndHold();
            int[] connectedOwners={2,2,2,2,0,2,2,1,1,2,2,2,0,0,0,2,2,2,1};
            _b.SeedCaptureOwnershipForTest(connectedOwners);
            Assert.AreEqual(Faction.Neutral,_b.CaptureView.OwnerOf(0));
            Assert.AreEqual(Faction.BlueTeam,_b.CaptureView.OwnerOf(4));
            Assert.AreEqual(Faction.BlueTeam,_b.CaptureView.OwnerOf(13));
            _b.SeedCaptureMatchElapsedForTest(599.75f);
            for(int f=1;f<=16;f++)yield return null;
            Assert.AreEqual(AbyssalVanguardPhase.Vanguard,_b.VanguardLogic.Phase);
            Vector3 p=_b.VanguardTarget.transform.position;
            Assert.AreEqual(0f,p.x,1e-3f);Assert.AreEqual(-1f,p.y,1e-3f);Assert.AreEqual(0f,p.z,1e-3f);
            _b.VanguardTarget.ReceiveDamage(900f,DamageType.Physical,_hero.gameObject);
            Assert.AreEqual(AbyssalVanguardPhase.Core,_b.VanguardLogic.Phase);
            _move.WarpTo(new Vector3(0,-1,0));
            for(int f=1;f<=209;f++)yield return null;
            Assert.AreEqual(AbyssalVanguardPhase.Core,_b.VanguardLogic.Phase,"3.5秒前不可取得巨獸");
            for(int f=210;f<=212;f++)yield return null;
            Assert.AreEqual(AbyssalVanguardPhase.Behemoth,_b.VanguardLogic.Phase);
            Assert.AreEqual(Faction.BlueTeam,_b.CaptureView.OwnerOf(0),"核心與0號圈同時引導翻塔");AssertRoute(1,7);
            Vector3 before=_b.VanguardTarget.transform.position;bool arrived=false;
            for(int f=1;f<=2400;f++)
            {
                yield return null;Vector3 now=_b.VanguardTarget.transform.position;StepOnTerrain(before,now);before=now;
                if(Distance(now,new Vector3(0,0,15.15625f))<0.6f){arrived=true;break;}
            }
            Assert.IsTrue(arrived,"2400幀內經R4抵達7號塔心");
        }
        [UnityTest] public IEnumerator B11_CliffBehemoth_RouteExcludesForbiddenOriginalEdge()
        {
            yield return StartCapture();MoveRedAwayAndHold();
            _b.SeedBehemothForTest(0,6.5625f,3.7890625f);yield return null;AssertRoute(9,8,7);
            Vector3 before=_b.VanguardTarget.transform.position;bool reached=false;
            for(int f=1;f<=240;f++)
            {
                yield return null;Vector3 now=_b.VanguardTarget.transform.position;StepOnTerrain(before,now);before=now;
                if(_b.CaptureSpec.TileAt(now.x,now.z)==9){reached=true;break;}
            }
            Assert.IsTrue(reached,"240幀內沿R0進9號");
        }
        private void QuickRuneTap()
        {
            Assert.IsTrue(_b.TryGetRuneButtonScreenPoint(out float x,out float y));AssertScreen(new Vector3(x,y,1));
            _b.BeginScreenHold(x,y);_b.EndScreenHold();
        }
        private IEnumerator WallHeight(bool fromCliff)
        {
            yield return StartCapture();_b.HoldOpponentForTest(true);
            _move.WarpTo(fromCliff?new Vector3(4.59375f,1,2.0703125f):new Vector3(0,-1,-3));
            _move.FaceDirection(fromCliff?Vector3.left:Vector3.forward);int blocked=_b.NavGrid.BlockedCount;
            QuickRuneTap();yield return null;
            RuneCaster caster=UnityEngine.Object.FindObjectOfType<RuneCaster>();RuneWall live=null;
            foreach(RuneWall wall in caster.Pool)if(wall.IsAlive){Assert.IsNull(live);live=wall;}
            Assert.IsNotNull(live,"活性：真實符印按鈕已成牆");Assert.Greater(_b.NavGrid.BlockedCount,blocked);
            // 地表錨點是牆底；既有 root 表示高 2m 碰撞盒的幾何中心。
            BoxCollider box=live.GetComponent<BoxCollider>();
            Assert.AreEqual(-1f,box.bounds.min.y,1e-3f);
            Assert.AreEqual(2f,box.bounds.size.y,1e-3f);
            Assert.AreEqual(0f,live.transform.position.y,1e-3f);
            if(fromCliff){Assert.AreEqual(0.59375f,live.transform.position.x,1e-3f);Assert.AreEqual(2.0703125f,live.transform.position.z,1e-3f);}
        }
        [UnityTest] public IEnumerator B12_ValleyQuickRuneWall_IsAtMinusOne(){yield return WallHeight(false);}
        [UnityTest] public IEnumerator B12_CliffCaster_WallCenterUsesValleyHeight(){yield return WallHeight(true);}
        [UnityTest] public IEnumerator B15_OffMovement_PreservesZeroHeightEveryFrame()
        {
            yield return Load();Assert.AreEqual(CaptureMatchState.Off,_b.CaptureState);
            Vector3 origin=_hero.transform.position;Tap(origin+Vector3.forward*3f);float moved=0;
            for(int f=1;f<=120;f++)
            {yield return null;Assert.AreEqual(0f,_hero.transform.position.y);moved=Mathf.Max(moved,Distance(origin,_hero.transform.position));}
            Assert.Greater(moved,0.05f,"活性：Off英雄確實移動");
        }
        private IEnumerator RevealSetup()
        {
            yield return StartCapture();Owners();_b.HoldOpponentForTest(true);
            _move.WarpTo(new Vector3(4.59375f,1,2.0703125f));_redMove.WarpTo(new Vector3(0,-1,0));
            for(int f=1;f<=30;f++){yield return null;Assert.IsFalse(_red.CanSeeHero());Assert.IsTrue(RedRenderer.enabled);}
        }
        private IEnumerator FirstBlueHit()
        {
            float health=_red.Health;Tap(_red.transform.position+Vector3.up);
            Assert.AreSame(_red,_hero.CurrentTarget,"真實點擊必須鎖到对手，才驗開火顯形");
            bool hit=false;
            for(int f=1;f<=90;f++){yield return null;if(_red.Health<health){hit=true;break;}}
            Assert.IsTrue(hit,"必須實際扣紅方血才算h");_hero.CancelCombatForDuel();
            _revealOffset=0;if(!_red.CanSeeHero()){yield return null;_revealOffset=1;}
            Assert.IsTrue(_red.CanSeeHero(),"h或h+1顯形必須先於谷底隱藏規則");
        }
        [UnityTest] public IEnumerator B16_BlueHit_RevealsAttackerThenExpires_AndRedDoesNotChaseExpired()
        {
            yield return RevealSetup();yield return FirstBlueHit();
            for(int f=_revealOffset+1;f<=95;f++)
            {yield return null;if(f==85)Assert.IsTrue(_red.CanSeeHero());if(f==95)Assert.IsFalse(_red.CanSeeHero());}
            float distance=Distance(_red.transform.position,_hero.transform.position);_b.HoldOpponentForTest(false);
            for(int f=1;f<=60;f++)yield return null;
            Assert.LessOrEqual(distance-Distance(_red.transform.position,_hero.transform.position),0.05f,"顯形過期不能追打");
        }
        [UnityTest] public IEnumerator B16_BlueHit_RedAiActuallyChasesRevealedAttacker()
        {
            yield return RevealSetup();yield return FirstBlueHit();
            Vector3 origin=_red.transform.position;_b.HoldOpponentForTest(false);float max=0;
            for(int f=1;f<=90;f++){yield return null;max=Mathf.Max(max,Distance(origin,_red.transform.position));}
            Assert.GreaterOrEqual(max,0.05f,"顯形後紅AI應追打，活性對照");
        }
        [UnityTest] public IEnumerator B16_RedHit_RevealsRedToBlue_For85ButNot95Frames()
        {
            yield return StartCapture();Owners(2,0,1);
            _move.WarpTo(new Vector3(2.8125f,-1,1.625f));_redMove.WarpTo(new Vector3(4.457948f,1,2.575f));
            Assert.IsFalse(_hero.CanEngage(_red));float health=_hero.Health;bool hit=false;
            for(int f=1;f<=120;f++){yield return null;if(_hero.Health<health){hit=true;break;}}
            Assert.IsTrue(hit,"實際紅方命中才算hprime");
            int offset=0;if(!_hero.CanEngage(_red)){yield return null;offset=1;}
            Assert.IsTrue(_hero.CanEngage(_red));
            for(int f=offset+1;f<=95;f++)
            {yield return null;if(f==85)Assert.IsTrue(_hero.CanEngage(_red));if(f==95)Assert.IsFalse(_hero.CanEngage(_red));}
        }
        [UnityTest] public IEnumerator B16_ElementHit_WithNullInstigator_RevealsBlueAttacker()
        {
            yield return RevealSetup();float health=_red.Health;
            _b.ElementField.CastFire(new Vector3(0,-1,0),0);bool hit=false;
            for(int f=1;f<=90;f++){yield return null;if(_red.Health<health){hit=true;break;}}
            Assert.IsTrue(hit,"元素實際扣血，不能只驗NotifyHit字串或Tracker");
            if(!_red.CanSeeHero())yield return null;
            Assert.IsTrue(_red.CanSeeHero(),"元素null instigator仍依施法陣營揭露藍方");
        }
    }
}
