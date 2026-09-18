using UnityEngine;

namespace Vow.Bootstrap
{
    // 固定 52° 俯視跟隨鏡頭（GDD §伍-2）。階層：Rig（本元件，負責跟隨）→ ShakePivot（震屏專用）→ Camera。
    // 跟隨只動 Rig、震屏只動 ShakePivot，兩者不會互相覆寫。
    public sealed class FollowCameraRig : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private float _pitchDegrees = 52f;
        [SerializeField] private float _yawDegrees;
        [SerializeField] private float _distance = 17f;
        [SerializeField] private float _followSharpness = 14f;

        public void SetTarget(Transform target)
        {
            _target = target;
            SnapToTarget();
        }

        public void SnapToTarget()
        {
            if (_target == null) return;
            transform.rotation = Quaternion.Euler(_pitchDegrees, _yawDegrees, 0f);
            transform.position = DesiredPosition();
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            // 指數平滑：與幀率無關，120fps 與 60fps 的跟隨手感一致
            float blend = 1f - Mathf.Exp(-_followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, DesiredPosition(), blend);
        }

        private Vector3 DesiredPosition()
        {
            Quaternion rotation = Quaternion.Euler(_pitchDegrees, _yawDegrees, 0f);
            return _target.position - rotation * Vector3.forward * _distance;
        }
    }
}
