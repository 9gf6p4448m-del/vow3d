using System;
using System.Collections;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Core;
using Vow.Core.Logic;
using Vow.Input;
using Object = UnityEngine.Object;

namespace Vow.Tests.PlayMode
{
    // V0140 §9-C: fixed windows and literal coordinates; no production state reflection.
    public sealed class CanyonMatchPlayTests
    {
        private Phase1Bootstrap _b;
        private HeroController _hero;
        private TrainingOpponent _red;
        private HeroLocomotion _move, _redMove;
        private float _oldDt;
        private CanyonTestGameView _view;
        private static CanyonTerrainSpec Terrain => CanyonTerrainSpec.V0140;
        private static readonly float[] X = {0,0,6.5625f,6.5625f,0,-6.5625f,-6.5625f,0,6.5625f,13.125f,13.125f,13.125f,6.5625f,0,-6.5625f,-13.125f,-13.125f,-13.125f,-6.5625f};
        private static readonly float[] Z = {0,7.578125f,3.7890625f,-3.7890625f,-7.578125f,-3.7890625f,3.7890625f,15.15625f,11.3671875f,7.578125f,0,-7.578125f,-11.3671875f,-15.15625f,-11.3671875f,-7.578125f,0,7.578125f,11.3671875f};
        private static readonly int[] Windows = {372,169,629,169,629,634,169,510,184,179,169,180,179,184,195,179,180,169,179};
        private static string EvidenceDirectory => Path.GetFullPath(Path.Combine(Application.dataPath,"../../vow-toolchain"));

        [SetUp] public void SetUp() { _oldDt=Time.captureDeltaTime; Time.captureDeltaTime=1f/60f; }
        [TearDown] public void TearDown()
        {
            AllocationProbe.Measuring=false;
            Time.captureDeltaTime=_oldDt;
            _view?.Dispose(); _view=null;
        }
        private IEnumerator Load()
        {
            SceneManager.LoadScene("VOW_Phase1_Greybox",LoadSceneMode.Single);
            yield return null; yield return null;
            _b=Object.FindObjectOfType<Phase1Bootstrap>(); _hero=Object.FindObjectOfType<HeroController>();
            _red=Object.FindObjectOfType<TrainingOpponent>();
            Assert.IsNotNull(_b); Assert.IsNotNull(_hero); Assert.IsNotNull(_red);
            _move=_hero.GetComponent<HeroLocomotion>(); _redMove=_red.GetComponent<HeroLocomotion>();
            Assert.AreEqual(CaptureMatchState.Off,_b.CaptureState);
        }
        private static void AssertScreen(Vector3 p)
        {
            Assert.Greater(p.z,0f);
            Assert.That(p.x,Is.InRange(16f,Screen.width-16f)); Assert.That(p.y,Is.InRange(16f,Screen.height-16f));
        }
        private void TapCapture()
        {
            Assert.IsTrue(_b.TryGetCaptureButtonScreenPoint(out float x,out float y));
            AssertScreen(new Vector3(x,y,1)); _b.WorldTapInput.SendScreenTap(x,y);
        }
        private void TapOpponent()
        {
            Vector3 p=Camera.main.WorldToScreenPoint(_red.transform.position+Vector3.up);
            AssertScreen(p);
            int moves=0,targets=0; Vector3 moveEvent=Vector3.zero; ICombatTarget targetEvent=null;
            int uiEvents=0,uiConsumed=-1;
            var input=(PlayerInputService)_b.InputService;
            Action<Vector3> moved=point=>{moves++;moveEvent=point;};
            Action<ICombatTarget> selected=target=>{targets++;targetEvent=target;};
            Action<int> uiTapped=id=>{uiEvents++;uiConsumed=id;};
            _b.InputService.OnMoveDestinationSelected+=moved; _b.InputService.OnCombatTargetSelected+=selected;
            input.OnUiRegionTapped+=uiTapped;
            try
            {
                TouchRoute route=input.Routing.Route(p.x,p.y,Screen.width,Screen.height,input.ActiveMode,out int region);
                Debug.Log("[V14-TapOpponent-before] frame="+Time.frameCount+" state="+_b.CaptureState+" screen="+p+" dpi="+Screen.dpi
                    +" route="+route+" regionId="+region+" talentPanel="+_b.TalentPanelVisible+" hero="+_hero.transform.position+" red="+_red.transform.position);
                RaycastHit[] hits=new RaycastHit[64];
                int count=Physics.RaycastNonAlloc(Camera.main.ScreenPointToRay(p),hits,500f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
                Assert.Less(count,hits.Length,"診斷射線不得靜默截斷");
                for(int i=0;i<count;i++)
                    Debug.Log("[V14-TapOpponent-hit] index="+i+" collider="+hits[i].collider.name+" distance="+hits[i].distance
                        +" point="+hits[i].point+" transform="+hits[i].collider.transform.position+" bounds="+hits[i].collider.bounds);
                foreach(Collider collider in _red.TargetColliders)
                    Debug.Log("[V14-TapOpponent-red-collider] name="+collider.name+" enabled="+collider.enabled+" transform="+collider.transform.position+" bounds="+collider.bounds);
                _b.WorldTapInput.SendScreenTap(p.x,p.y);
                Debug.Log("[V14-TapOpponent-after] frame="+Time.frameCount+" state="+_b.CaptureState+" hero="+_hero.transform.position+" red="+_red.transform.position
                    +" moveEvents="+moves+" movePoint="+moveEvent+" targetEvents="+targets+" targetIsRed="+ReferenceEquals(targetEvent,_red)
                    +" uiEvents="+uiEvents+" consumedRegion="+uiConsumed);
            }
            finally { _b.InputService.OnMoveDestinationSelected-=moved; _b.InputService.OnCombatTargetSelected-=selected; input.OnUiRegionTapped-=uiTapped; }
            Assert.AreEqual(CaptureMatchState.Active,_b.CaptureState,"真實點對手當幀開局");
        }
        private IEnumerator Start()
        {
            yield return Load(); TapCapture();
            Assert.AreEqual(CaptureMatchState.Lobby,_b.CaptureState);
            for(int f=0;f<30;f++) yield return null;
            TapOpponent();
        }
        private static float Distance(Vector3 a,Vector3 b) => new Vector2(a.x-b.x,a.z-b.z).magnitude;
        private void CheckStep(Vector3 before,Vector3 after,int frame=-1,CaptureMatchState previousState=CaptureMatchState.Off)
        {
            // Approved C01 exception: only the prescribed Ended -> Lobby warp.
            if(previousState==CaptureMatchState.Ended && _b.CaptureState==CaptureMatchState.Lobby)
            {
                Assert.LessOrEqual(Distance(after,new Vector3(4,0,-12.5f)),0.05f,"C01 prescribed Lobby reset endpoint");
                Assert.AreEqual(0f,after.y,1e-3f,"C01 Lobby reset height");
                Assert.AreEqual(Terrain.HeightAt(after.x,after.z,0),after.y,1e-3f,"C01 reset terrain height");
                return;
            }
            Assert.IsFalse(Terrain.CrossesCliff(before.x,before.z,after.x,after.z),
                Terrain.CrossesCliff(before.x,before.z,after.x,after.z)
                    ? "相鄰幀不得穿崖 before="+before+" after="+after+" f="+frame+" unityFrame="+Time.frameCount
                        +" state="+previousState+"→"+_b.CaptureState+" redAlive="+_red.IsAlive+" redKO="+_b.CaptureView.RedKnockedOut+" respawnRemaining="+_b.CaptureView.RedRespawnRemaining
                    : "相鄰幀不得穿崖");
            Assert.AreEqual(Terrain.HeightAt(after.x,after.z,0),after.y,1e-3f,"逐幀高度");
        }
        private void KeepHeroDown() { if(_hero.IsAlive) _hero.TakeDuelDamage(1000f); }

        [UnityTest] public IEnumerator C01_IdleFullMatch_RedFlipsCliffsAndValley_AndWinsInFrozenWindow()
        {
            Time.captureDeltaTime=0.1f;
            yield return Start();
            bool[] flipped=new bool[19]; Faction[] previous=new Faction[19];
            for(int i=0;i<19;i++) previous[i]=_b.CaptureView.OwnerOf(i);
            Vector3 before=_red.transform.position;
            int ended=-1,lobby=-1; float elapsed=-1;
            for(int f=1;f<=9600;f++)
            {
                int channelTile=_b.CaptureView.RedChannelingTile, redFlips=_b.CaptureRedFlipCount;
                CaptureMatchState previousState=_b.CaptureState;
                yield return null;
                Vector3 now=_red.transform.position; CheckStep(before,now,f,previousState); before=now;
                // An isolated flip can be neutralised by BFS in the same tick; the Bootstrap
                // counter records the real flip even when the final owner is Neutral again.
                if(_b.CaptureRedFlipCount>redFlips && channelTile>=0) flipped[channelTile]=true;
                for(int i=0;i<19;i++)
                {
                    Faction owner=_b.CaptureView.OwnerOf(i);
                    if(owner==Faction.RedTeam && previous[i]!=Faction.RedTeam) flipped[i]=true;
                    previous[i]=owner;
                }
                if(ended<0 && _b.CaptureState==CaptureMatchState.Ended)
                { ended=f; elapsed=900f-_b.CaptureView.MatchRemainingSeconds; }
                if(_b.CaptureState==CaptureMatchState.Lobby) { lobby=f; break; }
            }
            int cliffs=0,valleys=0;
            foreach(int tile in new[]{2,3,5,6}) if(flipped[tile]) cliffs++;
            foreach(int tile in new[]{0,1,4}) if(flipped[tile]) valleys++;
            Debug.Log("[V14-C01] ended="+ended+" lobby="+lobby+" elapsed="+elapsed+" cliffFlips="+cliffs+" valleyFlips="+valleys+" result="+_b.MatchStatusLabel);
            Assert.Greater(ended,0); Assert.Greater(lobby,ended);
            Assert.AreEqual("LAST: RED WINS",_b.MatchStatusLabel);
            Assert.GreaterOrEqual(cliffs,2); Assert.GreaterOrEqual(valleys,1);
            Assert.That(elapsed,Is.InRange(336.8f,505.2f));
        }

        [UnityTest] public IEnumerator C02_All19AiSegments_EnterTheirCircleWithinLiteralWindows()
        {
            yield return Start();
            _move.WarpTo(new Vector3(-17,0,-17)); KeepHeroDown();
            int[] owners=new int[19];
            for(int tile=0;tile<19;tile++)
            {
                // Each segment starts at the preceding tower centre, never at an opportunistic arrival point.
                Vector3 start=tile==0?new Vector3(0,0,16.65625f):new Vector3(X[tile-1],Terrain.HeightAt(X[tile-1],Z[tile-1],0),Z[tile-1]);
                _redMove.WarpTo(start);
                for(int i=0;i<19;i++) owners[i]=i==tile?2:1;
                _b.SeedCaptureOwnershipForTest(owners);
                Vector3 before=_red.transform.position; int arrival=-1;
                for(int f=1;f<=Windows[tile];f++)
                {
                    yield return null; KeepHeroDown();
                    Vector3 now=_red.transform.position; CheckStep(before,now); before=now;
                    if(Distance(now,new Vector3(X[tile],0,Z[tile]))<=2.5f) { arrival=f; break; }
                }
                Debug.Log("[V14-C02] tile="+tile+" arrival="+arrival+" window="+Windows[tile]);
                Assert.Greater(arrival,0,"固定窗口超時 tile="+tile+" window="+Windows[tile]);
            }
        }

        [UnityTest] public IEnumerator C03_RealR5AndG0_WithPursuingAi_480FramesApprovedV0140JitProfile()
        {
            yield return Start(); _b.HoldOpponentForTest(true);
            // Warm-up: only an unrelated plain tower and 0:04..0:00 clock; no R5, G0, launch or pursuit.
            int[] owners={0,0,0,0,2,0,0,1,1,0,0,0,0,0,0,2,0,0,1};
            _b.SeedCaptureOwnershipForTest(owners); _b.SeedCaptureMatchElapsedForTest(896f);
            _move.WarpTo(new Vector3(-13.125f,0,-7.578125f));
            int warmFlips=_b.CaptureFlipCount, warmLaunches=_b.VentLogic.LaunchCount;
            int warm=0;
            for(;warm<600 && _b.CaptureState!=CaptureMatchState.Lobby;warm++) yield return null;
            Assert.AreEqual(CaptureMatchState.Lobby,_b.CaptureState,"暖機固定600幀");
            Assert.IsTrue(_b.CaptureView.EndedByTime);
            Assert.AreEqual(warmLaunches,_b.VentLogic.LaunchCount,"暖機不得發射");
            Assert.AreEqual(1,_b.CaptureFlipCount-warmFlips,"暖機只翻無關15號");
            for(int f=0;f<30;f++) yield return null;
            TapOpponent(); _b.HoldOpponentForTest(false);
            for(int f=0;f<60;f++) yield return null;

            // A transparent per-locomotion decorator counts real height reads without changing a result.
            // Other terrain consumers retain the original query. No test calls this decorator's HeightAt.
            ITerrainQuery originalTerrain=_move.TerrainQuery;
            var counted=new CanyonCountedTerrain(originalTerrain,_move);
            _move.SetTerrain(counted); counted.Reset();
            RuneWall[] walls=Object.FindObjectsOfType<RuneWall>(true);
            bool[] stamped=new bool[walls.Length];
            for(int i=0;i<walls.Length;i++) stamped[i]=walls[i].NavBlockerStamped;
            int previousVersion=_b.NavGrid.Version;
            int[] versions=new int[480],changes=new int[480];
            bool[] seeing=new bool[480],grounded=new bool[480],chasing=new bool[480];
            float[] heights=new float[480],expected=new float[480];
            long[] mainThread=new long[480];
            long[] updateAlloc=new long[480],lateAlloc=new long[480];
            int launchBefore=_b.VentLogic.LaunchCount;
            Vector3 redBefore=_red.transform.position; float redTravel=0f;
            GameObject rig=new GameObject("CanyonMatchProbeRig");
            rig.AddComponent<AllocationProbeBegin>(); rig.AddComponent<AllocationProbeEnd>();
            var driver=rig.AddComponent<CanyonMatchRouteDriver>();
            driver.Configure(_b,_hero,Camera.main);
            Assert.IsTrue(driver.AllocationRecorderValid,"driver配置診斷ProfilerRecorder必須有效");
            AllocationProbe.Reset();
            ProfilerRecorder recorder=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);
            Assert.IsTrue(recorder.Valid,"Main Thread recorder不可用時不能捏造效能");
            try
            {
                AllocationProbe.Measuring=true; driver.Running=true;
                for(int f=0;f<480;f++)
                {
                    yield return null;
                    versions[f]=_b.NavGrid.Version-previousVersion; previousVersion=_b.NavGrid.Version;
                    for(int i=0;i<walls.Length;i++)
                    {
                        bool now=walls[i].NavBlockerStamped;
                        if(now!=stamped[i]) changes[f]++;
                        stamped[i]=now;
                    }
                    seeing[f]=_red.CanSeeHero(); grounded[f]=!_move.IsVentFlying; chasing[f]=_red.IsChasingHero;
                    Vector3 p=_hero.transform.position;
                    heights[f]=p.y; expected[f]=Terrain.HeightAt(p.x,p.z,0);
                    redTravel+=Distance(redBefore,_red.transform.position); redBefore=_red.transform.position;
                    mainThread[f]=recorder.LastValue;
                    updateAlloc[f]=AllocationProbe.UpdateBytes; lateAlloc[f]=AllocationProbe.LateUpdateBytes;
                }
            }
            finally { AllocationProbe.Measuring=false; driver.Running=false; recorder.Dispose(); _move.SetTerrain(originalTerrain); Object.Destroy(rig); }
            driver.Report();
            for(int f=0;f<480;f++)
            {
                long updateDelta=updateAlloc[f]-(f==0?0L:updateAlloc[f-1]);
                long lateDelta=lateAlloc[f]-(f==0?0L:lateAlloc[f-1]);
                if(updateDelta!=0 || lateDelta!=0) Debug.Log("[V14-C03-alloc] frame="+(f+1)+" update="+updateDelta+" late="+lateDelta);
            }
            int visible=0,hidden=0,groundFrames=0,versionFrames=0,chaseFrames=0;
            for(int f=0;f<480;f++)
            {
                if(seeing[f]) visible++; else hidden++;
                if(chasing[f]) chaseFrames++;
                if(grounded[f]) { groundFrames++; Assert.AreEqual(expected[f],heights[f],1e-3f,"C03 grounded frame="+f); }
                if(versions[f]!=0) versionFrames++;
                Assert.AreEqual(changes[f]>0,versions[f]!=0,"Version只跟符印牆stamp出現/撤除改變 frame="+f);
            }
            Array.Sort(mainThread);
            Assert.Greater(mainThread[479],0L,"Main Thread必須有真樣本");
            double median=(mainThread[239]/2.0+mainThread[240]/2.0)/1e6, p95=mainThread[455]/1e6;
            string performance="frames=480 medianMs="+median.ToString("R",CultureInfo.InvariantCulture)+" p95Ms="+p95.ToString("R",CultureInfo.InvariantCulture);
            File.WriteAllText(Path.Combine(EvidenceDirectory,"v0140-C03-main-thread.txt"),performance+Environment.NewLine);
            Debug.Log("[V14-C03] "+performance+" heightUpdateFrames="+counted.GroundedHeightFrames+" grounded="+groundFrames+" visible="+visible+" hidden="+hidden+" launch="+(_b.VentLogic.LaunchCount-launchBefore)+" versionFrames="+versionFrames+" redTravel="+redTravel+" update="+AllocationProbe.UpdateBytes+" late="+AllocationProbe.LateUpdateBytes);
            Assert.IsTrue(driver.ValidProjections,"R5/G0點擊均距四邊16px");
            Assert.AreEqual(4,driver.CommandsSent,"真實中繼點→R5→谷底中繼點→G0四次點地");
            Assert.Greater(driver.RampFrames,0,"走過R5");
            Assert.GreaterOrEqual(counted.GroundedHeightFrames,400,"真locomotion逐幀高度更新");
            Assert.Greater(visible,0); Assert.Greater(hidden,0);
            Assert.Greater(redTravel,1f,"對手追逐/移動活性");
            Assert.Greater(chaseFrames,0,"量測窗口內對手真追英雄");
            Assert.AreEqual(1,_b.VentLogic.LaunchCount-launchBefore);
            Assert.IsFalse(_move.IsVentFlying,"窗口內落地");
            Assert.LessOrEqual(Distance(_hero.transform.position,new Vector3(4.59375f,1,2.0703125f)),0.05f);
            Assert.AreEqual(AbyssalVanguardPhase.Dormant,_b.VanguardLogic.Phase,"本局不進巨獸路線");
            Assert.AreEqual(480,AllocationProbe.Frames);
            // This is a version-specific byte-profile guard, not runtime proof of JIT origin.
            // Acceptance also requires the immutable raw/source checks specified in V0140 §14.
            // Original zero-byte failure and original test are preserved in vow-toolchain.
            Assert.AreEqual("2022.3.62f1",Application.unityVersion,"C03 exception is limited to the approved Unity version");
            for(int f=0;f<480;f++)
            {
                long updateDelta=updateAlloc[f]-(f==0?0L:updateAlloc[f-1]);
                long lateDelta=lateAlloc[f]-(f==0?0L:lateAlloc[f-1]);
                Assert.AreEqual(f==0 || f==209?48L:0L,updateDelta,"C03 approved Update profile frame="+(f+1));
                Assert.AreEqual(0L,lateDelta,"C03 LateUpdate must remain zero frame="+(f+1));
            }
            Assert.AreEqual(96L,AllocationProbe.UpdateBytes); Assert.AreEqual(0L,AllocationProbe.LateUpdateBytes);
            Debug.Log("[V14-C03-approved] byte profile guard passed; original zero-byte standard is not passed; JIT origin requires separate raw/source evidence.");
        }

        private void LobbyEntry(string label)
        {
            Assert.AreEqual(CaptureMatchState.Lobby,_b.CaptureState);
            Assert.LessOrEqual(Distance(_hero.transform.position,new Vector3(0,0,-16.65625f)),0.05f);
            Assert.LessOrEqual(Distance(_red.transform.position,new Vector3(4,0,-12.5f)),0.05f);
            Assert.AreEqual(0f,_hero.transform.position.y,1e-3f); Assert.AreEqual(0f,_red.transform.position.y,1e-3f);
            Vector3 p=_red.transform.position; float nearest=float.MaxValue;
            for(int i=0;i<44;i++)
            {
                Terrain.GetCliffSegment(i,out float x0,out float z0,out float x1,out float z1);
                Vector2 a=new Vector2(x0,z0),d=new Vector2(x1-x0,z1-z0),q=new Vector2(p.x,p.z);
                float t=Mathf.Clamp01(Vector2.Dot(q-a,d)/d.sqrMagnitude);
                nearest=Mathf.Min(nearest,(q-a-d*t).magnitude);
            }
            Assert.Greater(nearest,0.35f);
            Object.FindObjectOfType<FollowCameraRig>().SnapToTarget();
            Assert.AreEqual(844,Screen.width); Assert.AreEqual(390,Screen.height);
            Vector3 screen=Camera.main.WorldToScreenPoint(p+Vector3.up); AssertScreen(screen);
            Debug.Log("[V14-C04] "+label+" opponentPixel="+screen+" cliffDistance="+nearest);
        }
        private void ResetState()
        {
            for(int pad=0;pad<2;pad++)
            { Assert.AreEqual(0f,_b.VentLogic.Cooldown(pad)); Assert.AreEqual(0f,_b.VentLogic.Progress(pad)); }
            Assert.AreEqual(AbyssalVanguardPhase.Dormant,_b.VanguardLogic.Phase);
            Assert.AreEqual(AbyssalVanguardTarget.ObjectivePhase.Inactive,_b.VanguardTarget.Phase);
            Assert.AreEqual(0f,_b.VanguardLogic.BlueCoreProgress); Assert.AreEqual(0f,_b.VanguardLogic.RedCoreProgress);
            Assert.AreEqual(2,_b.VanguardLogic.BehemothOwner); Assert.AreEqual(0f,_b.VanguardLogic.BehemothRemaining);
            Assert.AreEqual(0,_b.NavGrid.NegativeStampCount);
            Vector3 p=_hero.transform.position; Assert.AreEqual(Terrain.HeightAt(p.x,p.z,0),p.y,1e-3f);
        }
        private IEnumerator EndToLobby()
        {
            _b.SeedCaptureScoresForTest(0,1000);
            for(int f=0;f<360 && _b.CaptureState!=CaptureMatchState.Lobby;f++) yield return null;
            Assert.AreEqual(CaptureMatchState.Lobby,_b.CaptureState,"原結算固定360幀窗口");
        }
        [UnityTest] public IEnumerator C04_BothRealLobbyEntriesAreClickable_AndSecondModeRestoresAllState()
        {
            _view=new CanyonTestGameView(844,390);
            yield return Load(); TapCapture(); LobbyEntry("Off→Lobby");
            int firstBlocked=_b.NavGrid.BlockedCount;
            TapOpponent(); ResetState(); _b.HoldOpponentForTest(true);
            _move.WarpTo(new Vector3(2.09375f,-1,2.0625f));
            for(int f=0;f<70;f++) yield return null;
            Assert.AreEqual(1,_b.VentLogic.LaunchCount); Assert.Greater(_b.VentLogic.Cooldown(0),0f);
            _b.SeedCaptureMatchElapsedForTest(600f); yield return null;
            Assert.AreEqual(AbyssalVanguardPhase.Vanguard,_b.VanguardLogic.Phase);
            _b.VanguardTarget.ReceiveDamage(1000f,DamageType.True,_hero.gameObject);
            _move.WarpTo(new Vector3(0,-1,0));
            for(int f=0;f<10;f++) yield return null;
            Assert.AreEqual(AbyssalVanguardPhase.Core,_b.VanguardLogic.Phase);
            Assert.Greater(_b.VanguardLogic.BlueCoreProgress,0f,"重置前真核心引導活性");
            yield return EndToLobby(); LobbyEntry("Ended→Lobby");
            TapOpponent(); ResetState();
            yield return EndToLobby();
            TapCapture(); Assert.AreEqual(CaptureMatchState.Off,_b.CaptureState);
            TapCapture(); LobbyEntry("second Off→Lobby");
            Assert.AreEqual(firstBlocked,_b.NavGrid.BlockedCount); Assert.AreEqual(0,_b.NavGrid.NegativeStampCount);
            TapOpponent(); ResetState();
        }
    }

    // Only installed on the hero's real locomotion; all answers are delegated unchanged.
    internal sealed class CanyonCountedTerrain : ITerrainQuery
    {
        private readonly ITerrainQuery _source;
        private readonly HeroLocomotion _mover;
        private int _lastFrame=-1;
        public int GroundedHeightFrames { get; private set; }
        public CanyonCountedTerrain(ITerrainQuery source,HeroLocomotion mover) { _source=source; _mover=mover; }
        public void Reset() { GroundedHeightFrames=0; _lastFrame=-1; }
        public float HeightAt(float x,float z,int layer)
        {
            if(!_mover.IsVentFlying && _lastFrame!=Time.frameCount) { GroundedHeightFrames++; _lastFrame=Time.frameCount; }
            return _source.HeightAt(x,z,layer);
        }
        public int LayerCountAt(float x,float z) => _source.LayerCountAt(x,z);
        public TerrainClass ClassAt(float x,float z,int layer) => _source.ClassAt(x,z,layer);
        public int ResolveLayer(float x,float z,float currentY) => _source.ResolveLayer(x,z,currentY);
        public bool IsSameFloor(float x0,float z0,int layer0,float x1,float z1,int layer1) => _source.IsSameFloor(x0,z0,layer0,x1,z1,layer1);
    }

    // Commands execute inside the measured Update span, not the test coroutine.
    public sealed class CanyonMatchRouteDriver : MonoBehaviour
    {
        private Phase1Bootstrap _b; private HeroController _hero; private Camera _camera;
        private PlayerInputService _input;
        private int _frame,_attempts,_moveEvents,_targetEvents,_uiEvents;
        private bool _aborted;
        private Vector3 _lastMove;
        private int _lastUi=-1;
        private readonly Vector3[] _destinations={new Vector3(0,-0.341796875f,-12),new Vector3(0,-1,-8.3671875f),new Vector3(0,-1,-2.5f),new Vector3(2.09375f,-1,2.0625f)};
        private readonly Vector3[] _positions=new Vector3[480];
        private readonly int[] _phases=new int[480];
        private readonly Vector3[] _tapPixels=new Vector3[4],_tapBefore=new Vector3[4],_tapAfter=new Vector3[4],_movePoints=new Vector3[4];
        private readonly int[] _tapFrames=new int[4],_routes=new int[4],_regions=new int[4],_moveCounts=new int[4],_targetCounts=new int[4],_uiCounts=new int[4],_consumedRegions=new int[4];
        private readonly bool[] _sent=new bool[4];
        private readonly long[] _updateBegin=new long[480],_updateEnd=new long[480],_inputBegin=new long[4],_inputEnd=new long[4];
        private ProfilerRecorder _allocation;
        public bool AllocationRecorderValid => _allocation.Valid;
        public bool Running;
        public bool ValidProjections { get; private set; }=true;
        public int CommandsSent { get; private set; }
        public int RampFrames { get; private set; }
        public void Configure(Phase1Bootstrap b,HeroController hero,Camera camera)
        {
            _b=b; _hero=hero; _camera=camera; _input=(PlayerInputService)b.InputService;
            _allocation=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame");
            _updateBegin[0]=_allocation.CurrentValue; // Warm only this diagnostic reader, never a gameplay path.
            _input.OnMoveDestinationSelected+=Moved; _input.OnCombatTargetSelected+=Selected; _input.OnUiRegionTapped+=UiTapped;
        }
        private void Moved(Vector3 point) { _moveEvents++; _lastMove=point; }
        private void Selected(ICombatTarget target) { _targetEvents++; }
        private void UiTapped(int region) { _uiEvents++; _lastUi=region; }
        private void OnDestroy()
        {
            if(_allocation.Valid) _allocation.Dispose();
            if(_input==null) return;
            _input.OnMoveDestinationSelected-=Moved; _input.OnCombatTargetSelected-=Selected; _input.OnUiRegionTapped-=UiTapped;
        }
        private void Update()
        {
            if(!Running) return;
            int sample=_frame;
            if(sample<480) _updateBegin[sample]=_allocation.CurrentValue;
            try
            {
                Vector3 p=_hero.transform.position;
                if(_frame<480) { _positions[_frame]=p; _phases[_frame]=CommandsSent; }
                _frame++;
                if(Mathf.Abs(p.x)<=1.75f && Mathf.Abs(p.z+11.3671875f)<=2f) RampFrames++;
                if(_aborted || CommandsSent>=4) return;
                if(CommandsSent==0) Send(_destinations[0]);
                else if(_frame>_tapFrames[CommandsSent-1] && _moveCounts[CommandsSent-1]==1 && _hero.HasArrivedAtDestination)
                    Send(_destinations[CommandsSent]);
            }
            finally { if(sample<480) _updateEnd[sample]=_allocation.CurrentValue; }
        }
        private void Send(Vector3 world)
        {
            Vector3 p=_camera.WorldToScreenPoint(world);
            bool valid=p.z>0 && p.x>=16 && p.x<=Screen.width-16 && p.y>=16 && p.y<=Screen.height-16;
            int slot=_attempts++;
            _tapFrames[slot]=_frame; _tapPixels[slot]=p; _tapBefore[slot]=_hero.transform.position;
            _routes[slot]=(int)_input.Routing.Route(p.x,p.y,Screen.width,Screen.height,_input.ActiveMode,out int region);
            _regions[slot]=region;
            ValidProjections &= valid;
            if(!valid) { _aborted=true; return; } // One failed projection is red; never retry or advance the phase.
            int moves=_moveEvents,targets=_targetEvents,ui=_uiEvents;
            _inputBegin[slot]=_allocation.CurrentValue;
            _b.WorldTapInput.SendScreenTap(p.x,p.y);
            _inputEnd[slot]=_allocation.CurrentValue;
            CommandsSent++; _sent[slot]=true;
            _tapAfter[slot]=_hero.transform.position; _moveCounts[slot]=_moveEvents-moves; _targetCounts[slot]=_targetEvents-targets;
            _movePoints[slot]=_lastMove; _uiCounts[slot]=_uiEvents-ui; _consumedRegions[slot]=_lastUi;
        }
        public void Report()
        {
            for(int i=0;i<_attempts;i++)
                Debug.Log("[V14-C03-tap] phase="+i+" frame="+_tapFrames[i]+" world="+_destinations[i]+" pixel="+_tapPixels[i]
                    +" before="+_tapBefore[i]+" after="+_tapAfter[i]+" sent="+_sent[i]+" route="+(TouchRoute)_routes[i]+" region="+_regions[i]
                    +" moves="+_moveCounts[i]+" movePoint="+_movePoints[i]+" targets="+_targetCounts[i]+" ui="+_uiCounts[i]+" consumedRegion="+_consumedRegions[i]
                    +" inputBytes="+(_inputEnd[i]-_inputBegin[i]));
            for(int f=0;f<Mathf.Min(_frame,480);f++)
            {
                if(f%60==0 || (f>0 && _phases[f]!=_phases[f-1]))
                    Debug.Log("[V14-C03-position] frame="+(f+1)+" phase="+_phases[f]+" hero="+_positions[f]);
                if(_updateEnd[f]!=_updateBegin[f])
                    Debug.Log("[V14-C03-driver-alloc] frame="+(f+1)+" driverBytes="+(_updateEnd[f]-_updateBegin[f]));
            }
        }
    }

    // Identical true FixedResolution API to B13; reflection is limited to Editor view configuration.
    internal sealed class CanyonTestGameView : IDisposable
    {
#if UNITY_EDITOR
        private readonly UnityEditor.EditorWindow _window;
        private readonly object _group;
        private readonly System.Reflection.MethodInfo _select;
        private readonly int _original,_temporary;
        public CanyonTestGameView(int width,int height)
        {
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;
            var assembly=typeof(UnityEditor.EditorWindow).Assembly;
            var view=assembly.GetType("UnityEditor.GameView",true);
            _window=UnityEditor.EditorWindow.GetWindow(view);
            _original=(int)view.GetProperty("selectedSizeIndex",flags).GetValue(_window);
            _select=view.GetMethod("SizeSelectionCallback",flags);
            var sizesType=assembly.GetType("UnityEditor.GameViewSizes",true);
            var sizes=sizesType.BaseType.GetProperty("instance",flags).GetValue(null);
            var groupType=view.GetProperty("currentSizeGroupType",flags).GetValue(null);
            _group=sizesType.GetMethod("GetGroup",flags).Invoke(sizes,new[]{groupType});
            var sizeType=assembly.GetType("UnityEditor.GameViewSize",true);
            var kindType=assembly.GetType("UnityEditor.GameViewSizeType",true);
            var size=Activator.CreateInstance(sizeType,new object[]{Enum.Parse(kindType,"FixedResolution"),width,height,"C04 temporary 844x390"});
            _temporary=(int)_group.GetType().GetMethod("GetTotalCount").Invoke(_group,null);
            _group.GetType().GetMethod("AddCustomSize").Invoke(_group,new[]{size});
            _select.Invoke(_window,new object[]{_temporary,null}); _window.Repaint();
        }
        public void Dispose()
        {
            try { _select.Invoke(_window,new object[]{_original,null}); }
            finally { _group.GetType().GetMethod("RemoveCustomSize").Invoke(_group,new object[]{_temporary}); }
        }
#else
        private readonly int _width=Screen.width,_height=Screen.height;
        public CanyonTestGameView(int width,int height) { Screen.SetResolution(width,height,false); }
        public void Dispose() { Screen.SetResolution(_width,_height,false); }
#endif
    }
}
