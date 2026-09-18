using UnityEngine;
using Vow.Core;
using Vow.Core.Logic;
#if (UNITY_WEBGL || UNITY_IOS) && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Vow.Combat.Feedback
{
    // IHapticService 實作：疲勞策略（HapticFatiguePolicy，純邏輯、有測試）＋各平台的震動後端。
    //
    //   WebGL   ：navigator.vibrate（Android 手機瀏覽器有效；iOS Safari 不支援震動 API，會靜默略過）
    //   Android ：系統 Vibrator，經 AndroidJNI 以預先配置好的參數陣列呼叫，命中路徑上不產生 GC
    //   iOS     ：UIImpactFeedbackGenerator（Assets/Plugins/iOS/VowHaptics.mm）
    //   其他    ：不震
    // Android 與 iOS 兩個原生後端在撰寫當下沒有對應的建置環境與實機，尚未驗證。
    public sealed class HapticFeedbackService : MonoBehaviour, IHapticService
    {
        private const int LightMilliseconds = 10;
        private const int HeavyMilliseconds = 35;

        [SerializeField] private BasicAttackHaptics _basicAttackMode = BasicAttackHaptics.Light;

        private HapticFatiguePolicy _policy;

        public HapticStrength LastStrength { get; private set; }
        public int PulseCount { get; private set; }

        private void Awake()
        {
            _policy = HapticFatiguePolicy.CreateDefault();
            _policy.BasicAttackMode = _basicAttackMode;
            InitializeBackend();
        }

        public void Notify(HapticCue cue)
        {
            HapticStrength strength = _policy.Decide(cue, Time.unscaledTimeAsDouble);
            LastStrength = strength;
            if (strength == HapticStrength.None) return;

            PulseCount++;
            Pulse(strength == HapticStrength.Heavy);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void VowVibrate(int milliseconds);

        private void InitializeBackend() { }

        private void Pulse(bool heavy)
        {
            VowVibrate(heavy ? HeavyMilliseconds : LightMilliseconds);
        }

#elif UNITY_ANDROID && !UNITY_EDITOR
        private System.IntPtr _vibrator;
        private System.IntPtr _vibrateMethod;
        private readonly jvalue[] _vibrateArgs = new jvalue[1];

        private void InitializeBackend()
        {
            using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
            {
                if (vibrator == null) return;
                _vibrator = AndroidJNI.NewGlobalRef(vibrator.GetRawObject());
                _vibrateMethod = AndroidJNIHelper.GetMethodID(vibrator.GetRawClass(), "vibrate", "(J)V");
            }
        }

        private void Pulse(bool heavy)
        {
            if (_vibrator == System.IntPtr.Zero) return;
            _vibrateArgs[0].j = heavy ? HeavyMilliseconds : LightMilliseconds;
            AndroidJNI.CallVoidMethod(_vibrator, _vibrateMethod, _vibrateArgs);
        }

        private void OnDestroy()
        {
            if (_vibrator != System.IntPtr.Zero) AndroidJNI.DeleteGlobalRef(_vibrator);
        }

#elif UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void VowHapticPrepare();
        [DllImport("__Internal")] private static extern void VowHapticImpact(int heavy);

        private void InitializeBackend()
        {
            VowHapticPrepare();
        }

        private void Pulse(bool heavy)
        {
            VowHapticImpact(heavy ? 1 : 0);
        }

#else
        private void InitializeBackend() { }

        private void Pulse(bool heavy) { }
#endif
    }
}
