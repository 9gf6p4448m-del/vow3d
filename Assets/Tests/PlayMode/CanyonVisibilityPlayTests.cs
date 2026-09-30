using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Core;

namespace Vow.Tests.PlayMode
{
    // 凍結258點逐點literal，來源docs/v0140-b13-positions.csv；禁止依被測高度生成點集。
    public sealed class CanyonVisibilityPlayTests
    {
        private int _width,_height;
        private float _dt;
        private readonly RaycastHit[] _hits=new RaycastHit[64];
        [SetUp] public void SetUp(){_width=Screen.width;_height=Screen.height;_dt=Time.captureDeltaTime;Time.captureDeltaTime=1f/60f;ResizeTestView(844,390);}
        [TearDown] public void TearDown()
        {
            Time.captureDeltaTime=_dt;
#if UNITY_EDITOR
            if(_testSizeIndex<0)return;
            try{_selectSize.Invoke(_gameView,new object[]{_originalSizeIndex,null});}
            finally
            {
                _sizeGroup.GetType().GetMethod("RemoveCustomSize").Invoke(_sizeGroup,new object[]{_testSizeIndex});
                _testSizeIndex=-1;
            }
#else
            ResizeTestView(_width,_height);
#endif
        }
#if UNITY_EDITOR
        private UnityEditor.EditorWindow _gameView;
        private object _sizeGroup;
        private System.Reflection.MethodInfo _selectSize;
        private int _originalSizeIndex,_testSizeIndex=-1;
#endif
        private void ResizeTestView(int width,int height)
        {
#if UNITY_EDITOR
            // FixedResolution 決定 render size；Editor 視窗外框尺寸不能保證 Screen 尺寸。
            // 只反射 Editor 環境，原選取與臨時尺寸在 TearDown 還原。
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;
            var assembly=typeof(UnityEditor.EditorWindow).Assembly;
            var view=assembly.GetType("UnityEditor.GameView",true);
            _gameView=UnityEditor.EditorWindow.GetWindow(view);
            _originalSizeIndex=(int)view.GetProperty("selectedSizeIndex",flags).GetValue(_gameView);
            _selectSize=view.GetMethod("SizeSelectionCallback",flags);
            var sizesType=assembly.GetType("UnityEditor.GameViewSizes",true);
            var sizes=sizesType.BaseType.GetProperty("instance",flags).GetValue(null);
            var groupType=view.GetProperty("currentSizeGroupType",flags).GetValue(null);
            _sizeGroup=sizesType.GetMethod("GetGroup",flags).Invoke(sizes,new[]{groupType});
            var sizeType=assembly.GetType("UnityEditor.GameViewSize",true);
            var kindType=assembly.GetType("UnityEditor.GameViewSizeType",true);
            var fixedResolution=System.Enum.Parse(kindType,"FixedResolution");
            var size=System.Activator.CreateInstance(sizeType,new object[]{fixedResolution,width,height,"B13 temporary 844x390"});
            int index=(int)_sizeGroup.GetType().GetMethod("GetTotalCount").Invoke(_sizeGroup,null);
            _sizeGroup.GetType().GetMethod("AddCustomSize").Invoke(_sizeGroup,new[]{size});
            _testSizeIndex=index;
            // callback 同步 targetRenderSize 至 PlayModeView.targetSize；單設索引不會。
            _selectSize.Invoke(_gameView,new object[]{index,null});
            _gameView.Repaint();
#else
            Screen.SetResolution(width,height,false);
#endif
        }
#if UNITY_EDITOR
        private void CaptureGameViewRaw(string fileName)
        {
            // 僅保存 Editor 已渲染的畫面；不替代 Screen 或遮擋判準，也不另外重建相機。
            var field=_gameView.GetType().GetField("m_RenderTexture",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var rendered=(RenderTexture)field.GetValue(_gameView);
            Assert.IsNotNull(rendered,"實際 GameView render texture 必須存在才能驗畫面");
            RenderTexture previous=RenderTexture.active;Texture2D pixels=null;
            try
            {
                RenderTexture.active=rendered;
                pixels=new Texture2D(rendered.width,rendered.height,TextureFormat.RGBA32,false);
                pixels.ReadPixels(new Rect(0,0,rendered.width,rendered.height),0,0,false);
                Color32[] colors=pixels.GetPixels32();
                string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../vow-toolchain/"+fileName));
                using(var writer=new System.IO.BinaryWriter(System.IO.File.Create(path)))
                {
                    writer.Write(rendered.width);writer.Write(rendered.height);
                    foreach(Color32 color in colors){writer.Write(color.r);writer.Write(color.g);writer.Write(color.b);writer.Write(color.a);}
                }
            }
            finally{RenderTexture.active=previous;if(pixels!=null)Object.DestroyImmediate(pixels);}
        }
#endif
        private int VisibleSamples(Transform terrain,Vector3 foot,Vector3 cameraPosition,Vector3 right,out bool head)
        {
            int visible=0;head=false;
            float[] heights={0.1f,0.5f,0.9f,1.3f,1.7f};
            float[] offsets={-0.3f,0f,0.3f};
            for(int h=0;h<5;h++)for(int side=0;side<3;side++)
            {
                Vector3 sample=foot+Vector3.up*heights[h]+right*offsets[side];
                Vector3 segment=sample-cameraPosition;
                int count=Physics.RaycastNonAlloc(cameraPosition,segment.normalized,_hits,segment.magnitude,Physics.AllLayers,QueryTriggerInteraction.Ignore);
                Assert.Less(count,_hits.Length,"命中緩衝滿槽，不能靜默截斷");
                bool blocked=false;
                for(int i=0;i<count;i++)if(_hits[i].collider.transform.IsChildOf(terrain))blocked=true;
                if(!blocked){visible++;if(h==4&&side==1)head=true;}
            }
            return visible;
        }
        [UnityTest] public IEnumerator B13_All258ValleyPoints_AndBuriedFalseGreenControl()
        {
            Screen.SetResolution(844,390,false);
            SceneManager.LoadScene("VOW_Phase1_Greybox",LoadSceneMode.Single);
            yield return null;yield return null;
            Assert.AreEqual(844,Screen.width);Assert.AreEqual(390,Screen.height);
            var b=Object.FindObjectOfType<Phase1Bootstrap>();var hero=Object.FindObjectOfType<HeroController>();
            Assert.IsNotNull(b);Assert.IsNotNull(hero);
            Assert.IsTrue(b.TryGetCaptureButtonScreenPoint(out float x,out float y));
            Assert.That(x,Is.InRange(16f,828f));Assert.That(y,Is.InRange(16f,374f));
            b.WorldTapInput.SendScreenTap(x,y);
            Transform terrain=null;
            foreach(GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())if(root.name=="CanyonTerrain")terrain=root.transform;
            Assert.IsNotNull(terrain);Assert.IsTrue(terrain.gameObject.activeInHierarchy);
            var mover=hero.GetComponent<HeroLocomotion>();var rig=Object.FindObjectOfType<FollowCameraRig>();
            Assert.IsNotNull(rig);Camera camera=Camera.main;Assert.AreEqual(40f,camera.fieldOfView,1e-4f);
            Assert.AreEqual(258,Positions.Length);
            int minimumVisible=15,minimumIndex=-1;
            for(int i=0;i<258;i++)
            {
                Vector2 p=Positions[i];mover.WarpTo(new Vector3(p.x,-1f,p.y));yield return null;rig.SnapToTarget();Physics.SyncTransforms();
                Vector3 foot=hero.transform.position;
                Assert.AreEqual(-1f,foot.y,1e-3f);
                Assert.AreEqual(52f,camera.transform.eulerAngles.x,1e-4f);
                Assert.AreEqual(17f,Vector3.Distance(camera.transform.position,foot),1e-3f);
                int visible=VisibleSamples(terrain,foot,camera.transform.position,camera.transform.right,out bool head);
                if(visible<minimumVisible){minimumVisible=visible;minimumIndex=i;}
                Assert.GreaterOrEqual(visible,2,"B13紅即停；點"+i+"可見"+visible+"/15");
                Assert.IsTrue(head,"B13紅即停；點"+i+"頭頂中央必須可見");
            }
            // 不Warp：保留合成腳底y=-1，避免production高度校正拉回+1形成假綠。
            Vector3 buried=new Vector3(6.5625f,-1f,3.7890625f);
            Vector3 syntheticCamera=buried-camera.transform.forward*17f;
            int buriedVisible=VisibleSamples(terrain,buried,syntheticCamera,camera.transform.right,out bool buriedHead);
            Assert.AreEqual(0,buriedVisible,"假綠防護：埋入2號盒的合成英雄必須0/15");
            Assert.IsFalse(buriedHead);
            Debug.Log("B13 screen="+Screen.width+"x"+Screen.height+" positions=258 minimum="+minimumVisible+"/15 index="+minimumIndex+" buried="+buriedVisible+"/15");
#if UNITY_EDITOR
            // 凍結斷言全部結束後擷取，這些幀不參與前述量測窗口。
            mover.WarpTo(new Vector3(Positions[0].x,-1f,Positions[0].y));rig.SnapToTarget();yield return null;
            CaptureGameViewRaw("v0140-B-codex-valley.rgba");
            mover.WarpTo(new Vector3(4.59375f,1f,2.0703125f));rig.SnapToTarget();yield return null;
            CaptureGameViewRaw("v0140-B-codex-cliff.rgba");
#endif
        }
        private static readonly Vector2[] Positions=
        {
            new Vector2(4.054566343561484f, -0.184992626397494f),
            new Vector2(4.054566343561484f, 7.393132373602506f),
            new Vector2(-3.9919816467759413f, -0.07658699089396612f),
            new Vector2(-3.9919816467759413f, 7.501538009106034f),
            new Vector2(-4.054566343561484f, 0.184992626397494f),
            new Vector2(-4.054566343561484f, 7.763117626397494f),
            new Vector2(-3.9295713257253393f, -0.03151660092582789f),
            new Vector2(-3.9295713257253393f, 7.546608399074172f),
            new Vector2(3.9295713257253393f, 0.03151660092582789f),
            new Vector2(3.804576307889194f, 0.2480258282491498f),
            new Vector2(3.67958129005305f, 0.46453505557247166f),
            new Vector2(3.554586272216905f, 0.6810442828957936f),
            new Vector2(3.4295912543807607f, 0.8975535102191153f),
            new Vector2(3.304596236544616f, 1.1140627375424375f),
            new Vector2(3.1796012187084717f, 1.3305719648657595f),
            new Vector2(3.054606200872327f, 1.5470811921890812f),
            new Vector2(2.929611183036182f, 1.763590419512403f),
            new Vector2(2.804616165200038f, 1.9800996468357248f),
            new Vector2(2.679621147363893f, 2.196608874159047f),
            new Vector2(2.5546261295277484f, 2.4131181014823686f),
            new Vector2(2.429631111691604f, 2.6296273288056904f),
            new Vector2(2.3046360938554593f, 2.8461365561290126f),
            new Vector2(2.1796410760193146f, 3.0626457834523344f),
            new Vector2(2.0546460581831703f, 3.279155010775656f),
            new Vector2(1.9296510403470255f, 3.495664238098978f),
            new Vector2(4.054566343561484f, 0.184992626397494f),
            new Vector2(3.9295713257253393f, -0.03151660092582789f),
            new Vector2(3.804576307889194f, -0.2480258282491498f),
            new Vector2(3.67958129005305f, -0.46453505557247166f),
            new Vector2(3.554586272216905f, -0.6810442828957936f),
            new Vector2(3.4295912543807607f, -0.8975535102191153f),
            new Vector2(3.304596236544616f, -1.1140627375424375f),
            new Vector2(3.1796012187084717f, -1.3305719648657595f),
            new Vector2(3.054606200872327f, -1.5470811921890812f),
            new Vector2(2.929611183036182f, -1.763590419512403f),
            new Vector2(2.804616165200038f, -1.9800996468357248f),
            new Vector2(2.679621147363893f, -2.196608874159047f),
            new Vector2(2.5546261295277484f, -2.4131181014823686f),
            new Vector2(2.429631111691604f, -2.6296273288056904f),
            new Vector2(2.3046360938554593f, -2.8461365561290126f),
            new Vector2(2.1796410760193146f, -3.0626457834523344f),
            new Vector2(2.0546460581831703f, -3.279155010775656f),
            new Vector2(1.9296510403470255f, -3.495664238098978f),
            new Vector2(-3.804576307889194f, -0.2480258282491498f),
            new Vector2(-3.67958129005305f, -0.46453505557247166f),
            new Vector2(-3.554586272216905f, -0.6810442828957936f),
            new Vector2(-3.4295912543807607f, -0.8975535102191153f),
            new Vector2(-3.304596236544616f, -1.1140627375424375f),
            new Vector2(-3.1796012187084717f, -1.3305719648657595f),
            new Vector2(-3.054606200872327f, -1.5470811921890812f),
            new Vector2(-2.929611183036182f, -1.763590419512403f),
            new Vector2(-2.804616165200038f, -1.9800996468357248f),
            new Vector2(-2.679621147363893f, -2.196608874159047f),
            new Vector2(-2.5546261295277484f, -2.4131181014823686f),
            new Vector2(-2.429631111691604f, -2.6296273288056904f),
            new Vector2(-2.3046360938554593f, -2.8461365561290126f),
            new Vector2(-2.1796410760193146f, -3.0626457834523344f),
            new Vector2(-2.0546460581831703f, -3.279155010775656f),
            new Vector2(-1.9296510403470255f, -3.495664238098978f),
            new Vector2(-1.8670663435614836f, 3.604069873602506f),
            new Vector2(-1.9920613613976283f, 3.387560646279184f),
            new Vector2(-2.1170563792337727f, 3.1710514189558623f),
            new Vector2(-2.2420513970699174f, 2.95454219163254f),
            new Vector2(-2.367046414906062f, 2.7380329643092183f),
            new Vector2(-2.4920414327422065f, 2.5215237369858965f),
            new Vector2(-2.617036450578351f, 2.3050145096625743f),
            new Vector2(-2.7420314684144955f, 2.0885052823392525f),
            new Vector2(-2.8670264862506403f, 1.871996055015931f),
            new Vector2(-2.992021504086785f, 1.6554868276926091f),
            new Vector2(-3.1170165219229293f, 1.4389776003692873f),
            new Vector2(-3.242011539759074f, 1.2224683730459651f),
            new Vector2(-3.367006557595219f, 1.0059591457226433f),
            new Vector2(-3.492001575431363f, 0.7894499183993214f),
            new Vector2(-3.616996593267508f, 0.5729406910759992f),
            new Vector2(-3.7419916111036526f, 0.3564314637526774f),
            new Vector2(-3.866986628939797f, 0.13992223642935567f),
            new Vector2(4.054566343561484f, 7.763117626397494f),
            new Vector2(3.9295713257253393f, 7.546608399074172f),
            new Vector2(3.804576307889194f, 7.330099171750851f),
            new Vector2(3.67958129005305f, 7.113589944427528f),
            new Vector2(3.554586272216905f, 6.897080717104207f),
            new Vector2(3.4295912543807607f, 6.680571489780885f),
            new Vector2(3.304596236544616f, 6.4640622624575625f),
            new Vector2(3.1796012187084717f, 6.247553035134241f),
            new Vector2(3.054606200872327f, 6.031043807810919f),
            new Vector2(2.929611183036182f, 5.814534580487597f),
            new Vector2(2.804616165200038f, 5.598025353164275f),
            new Vector2(2.679621147363893f, 5.381516125840953f),
            new Vector2(2.5546261295277484f, 5.165006898517631f),
            new Vector2(2.429631111691604f, 4.94849767119431f),
            new Vector2(2.3046360938554593f, 4.731988443870987f),
            new Vector2(2.1796410760193146f, 4.515479216547665f),
            new Vector2(2.0546460581831703f, 4.298969989224344f),
            new Vector2(1.9296510403470255f, 4.082460761901022f),
            new Vector2(-3.804576307889194f, 7.330099171750851f),
            new Vector2(-3.67958129005305f, 7.113589944427528f),
            new Vector2(-3.554586272216905f, 6.897080717104207f),
            new Vector2(-3.4295912543807607f, 6.680571489780885f),
            new Vector2(-3.304596236544616f, 6.4640622624575625f),
            new Vector2(-3.1796012187084717f, 6.247553035134241f),
            new Vector2(-3.054606200872327f, 6.031043807810919f),
            new Vector2(-2.929611183036182f, 5.814534580487597f),
            new Vector2(-2.804616165200038f, 5.598025353164275f),
            new Vector2(-2.679621147363893f, 5.381516125840953f),
            new Vector2(-2.5546261295277484f, 5.165006898517631f),
            new Vector2(-2.429631111691604f, 4.94849767119431f),
            new Vector2(-2.3046360938554593f, 4.731988443870987f),
            new Vector2(-2.1796410760193146f, 4.515479216547665f),
            new Vector2(-2.0546460581831703f, 4.298969989224344f),
            new Vector2(-1.9296510403470255f, 4.082460761901022f),
            new Vector2(3.9295713257253393f, 7.609641600925828f),
            new Vector2(3.804576307889194f, 7.82615082824915f),
            new Vector2(3.67958129005305f, 8.042660055572473f),
            new Vector2(3.554586272216905f, 8.259169282895794f),
            new Vector2(3.4295912543807607f, 8.475678510219117f),
            new Vector2(3.304596236544616f, 8.692187737542438f),
            new Vector2(3.1796012187084717f, 8.90869696486576f),
            new Vector2(3.054606200872327f, 9.125206192189081f),
            new Vector2(2.929611183036182f, 9.341715419512404f),
            new Vector2(2.804616165200038f, 9.558224646835725f),
            new Vector2(2.679621147363893f, 9.774733874159049f),
            new Vector2(2.5546261295277484f, 9.99124310148237f),
            new Vector2(2.429631111691604f, 10.207752328805691f),
            new Vector2(2.3046360938554593f, 10.424261556129013f),
            new Vector2(2.1796410760193146f, 10.640770783452336f),
            new Vector2(2.0546460581831703f, 10.857280010775657f),
            new Vector2(1.9296510403470255f, 11.073789238098978f),
            new Vector2(-1.8670663435614836f, 11.182194873602507f),
            new Vector2(-1.9920613613976283f, 10.965685646279185f),
            new Vector2(-2.1170563792337727f, 10.749176418955862f),
            new Vector2(-2.2420513970699174f, 10.532667191632541f),
            new Vector2(-2.367046414906062f, 10.31615796430922f),
            new Vector2(-2.4920414327422065f, 10.099648736985896f),
            new Vector2(-2.617036450578351f, 9.883139509662575f),
            new Vector2(-2.7420314684144955f, 9.666630282339254f),
            new Vector2(-2.8670264862506403f, 9.450121055015932f),
            new Vector2(-2.992021504086785f, 9.23361182769261f),
            new Vector2(-3.1170165219229293f, 9.017102600369288f),
            new Vector2(-3.242011539759074f, 8.800593373045965f),
            new Vector2(-3.367006557595219f, 8.584084145722644f),
            new Vector2(-3.492001575431363f, 8.367574918399322f),
            new Vector2(-3.616996593267508f, 8.151065691076f),
            new Vector2(-3.7419916111036526f, 7.934556463752677f),
            new Vector2(-3.866986628939797f, 7.7180472364293555f),
            new Vector2(1.8670663435614836f, -3.974055126397494f),
            new Vector2(1.9920613613976283f, -4.190564353720816f),
            new Vector2(2.1170563792337727f, -4.407073581044138f),
            new Vector2(2.2420513970699174f, -4.62358280836746f),
            new Vector2(2.367046414906062f, -4.840092035690781f),
            new Vector2(2.4920414327422065f, -5.0566012630141035f),
            new Vector2(2.617036450578351f, -5.273110490337426f),
            new Vector2(2.7420314684144955f, -5.489619717660747f),
            new Vector2(2.8670264862506403f, -5.706128944984069f),
            new Vector2(2.992021504086785f, -5.9226381723073915f),
            new Vector2(3.1170165219229293f, -6.139147399630713f),
            new Vector2(3.242011539759074f, -6.355656626954035f),
            new Vector2(3.367006557595219f, -6.572165854277357f),
            new Vector2(3.492001575431363f, -6.788675081600679f),
            new Vector2(3.616996593267508f, -7.005184308924001f),
            new Vector2(3.7419916111036526f, -7.221693536247323f),
            new Vector2(3.866986628939797f, -7.4382027635706445f),
            new Vector2(3.9919816467759413f, -7.654711990893966f),
            new Vector2(-1.8670663435614836f, -3.974055126397494f),
            new Vector2(-1.9920613613976283f, -4.190564353720816f),
            new Vector2(-2.1170563792337727f, -4.407073581044138f),
            new Vector2(-2.2420513970699174f, -4.62358280836746f),
            new Vector2(-2.367046414906062f, -4.840092035690781f),
            new Vector2(-2.4920414327422065f, -5.0566012630141035f),
            new Vector2(-2.617036450578351f, -5.273110490337426f),
            new Vector2(-2.7420314684144955f, -5.489619717660747f),
            new Vector2(-2.8670264862506403f, -5.706128944984069f),
            new Vector2(-2.992021504086785f, -5.9226381723073915f),
            new Vector2(-3.1170165219229293f, -6.139147399630713f),
            new Vector2(-3.242011539759074f, -6.355656626954035f),
            new Vector2(-3.367006557595219f, -6.572165854277357f),
            new Vector2(-3.492001575431363f, -6.788675081600679f),
            new Vector2(-3.616996593267508f, -7.005184308924001f),
            new Vector2(-3.7419916111036526f, -7.221693536247323f),
            new Vector2(-3.866986628939797f, -7.4382027635706445f),
            new Vector2(-3.9919816467759413f, -7.654711990893966f),
            new Vector2(4.054566343561484f, -7.393132373602506f),
            new Vector2(3.9295713257253393f, -7.609641600925828f),
            new Vector2(3.804576307889194f, -7.82615082824915f),
            new Vector2(3.67958129005305f, -8.042660055572473f),
            new Vector2(3.554586272216905f, -8.259169282895794f),
            new Vector2(3.4295912543807607f, -8.475678510219117f),
            new Vector2(3.304596236544616f, -8.692187737542438f),
            new Vector2(3.1796012187084717f, -8.90869696486576f),
            new Vector2(3.054606200872327f, -9.125206192189081f),
            new Vector2(2.929611183036182f, -9.341715419512404f),
            new Vector2(2.804616165200038f, -9.558224646835725f),
            new Vector2(2.679621147363893f, -9.774733874159049f),
            new Vector2(2.5546261295277484f, -9.99124310148237f),
            new Vector2(2.429631111691604f, -10.207752328805691f),
            new Vector2(2.3046360938554593f, -10.424261556129013f),
            new Vector2(2.1796410760193146f, -10.640770783452336f),
            new Vector2(2.0546460581831703f, -10.857280010775657f),
            new Vector2(1.9296510403470255f, -11.073789238098978f),
            new Vector2(-4.054566343561484f, -7.393132373602506f),
            new Vector2(-3.9295713257253393f, -7.609641600925828f),
            new Vector2(-3.804576307889194f, -7.82615082824915f),
            new Vector2(-3.67958129005305f, -8.042660055572473f),
            new Vector2(-3.554586272216905f, -8.259169282895794f),
            new Vector2(-3.4295912543807607f, -8.475678510219117f),
            new Vector2(-3.304596236544616f, -8.692187737542438f),
            new Vector2(-3.1796012187084717f, -8.90869696486576f),
            new Vector2(-3.054606200872327f, -9.125206192189081f),
            new Vector2(-2.929611183036182f, -9.341715419512404f),
            new Vector2(-2.804616165200038f, -9.558224646835725f),
            new Vector2(-2.679621147363893f, -9.774733874159049f),
            new Vector2(-2.5546261295277484f, -9.99124310148237f),
            new Vector2(-2.429631111691604f, -10.207752328805691f),
            new Vector2(-2.3046360938554593f, -10.424261556129013f),
            new Vector2(-2.1796410760193146f, -10.640770783452336f),
            new Vector2(-2.0546460581831703f, -10.857280010775657f),
            new Vector2(-1.9296510403470255f, -11.073789238098978f),
            new Vector2(2.1875f, 10.9971875f),
            new Vector2(1.9375f, 10.9971875f),
            new Vector2(-2f, 10.9971875f),
            new Vector2(-2.1875f, -10.9971875f),
            new Vector2(-1.9375f, -10.9971875f),
            new Vector2(2f, -10.9971875f),
            new Vector2(2.12f, 9.3671875f),
            new Vector2(2.12f, 9.6171875f),
            new Vector2(2.12f, 9.8671875f),
            new Vector2(2.12f, 10.1171875f),
            new Vector2(2.12f, 10.3671875f),
            new Vector2(2.12f, 10.6171875f),
            new Vector2(2.12f, 10.8671875f),
            new Vector2(2.12f, 11.1171875f),
            new Vector2(2.12f, 11.3671875f),
            new Vector2(-2.12f, 9.3671875f),
            new Vector2(-2.12f, 9.6171875f),
            new Vector2(-2.12f, 9.8671875f),
            new Vector2(-2.12f, 10.1171875f),
            new Vector2(-2.12f, 10.3671875f),
            new Vector2(-2.12f, 10.6171875f),
            new Vector2(-2.12f, 10.8671875f),
            new Vector2(-2.12f, 11.1171875f),
            new Vector2(-2.12f, 11.3671875f),
            new Vector2(-2.12f, -9.3671875f),
            new Vector2(-2.12f, -9.6171875f),
            new Vector2(-2.12f, -9.8671875f),
            new Vector2(-2.12f, -10.1171875f),
            new Vector2(-2.12f, -10.3671875f),
            new Vector2(-2.12f, -10.6171875f),
            new Vector2(-2.12f, -10.8671875f),
            new Vector2(-2.12f, -11.1171875f),
            new Vector2(-2.12f, -11.3671875f),
            new Vector2(2.12f, -9.3671875f),
            new Vector2(2.12f, -9.6171875f),
            new Vector2(2.12f, -9.8671875f),
            new Vector2(2.12f, -10.1171875f),
            new Vector2(2.12f, -10.3671875f),
            new Vector2(2.12f, -10.6171875f),
            new Vector2(2.12f, -10.8671875f),
            new Vector2(2.12f, -11.1171875f),
            new Vector2(2.12f, -11.3671875f),
        };
    }
}
