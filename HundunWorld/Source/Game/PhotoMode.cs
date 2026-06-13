using FlaxEngine;

namespace HundunWorld.Game
{
    public class PhotoMode : Script
    {
        [Header("拍照设置")]
        [Tooltip("拍摄2寸头像的快捷键")]
        public KeyboardKeys TakeHeadshotKey = KeyboardKeys.F9;

        [Tooltip("拍摄全身照的快捷键")]
        public KeyboardKeys TakeFullBodyKey = KeyboardKeys.F10;

        [Tooltip("游戏截图的快捷键")]
        public KeyboardKeys TakeScreenshotKey = KeyboardKeys.F11;

        [Tooltip("截图保存路径（相对于项目根目录）")]
        public string ScreenshotPath = "Screenshots";

        [Tooltip("头像照片距离（米）")]
        public float HeadshotDistance = 2.5f;

        [Tooltip("头像照片俯仰角")]
        public float HeadshotPitch = 0f;

        [Tooltip("全身照距离（米）")]
        public float FullBodyDistance = 5f;

        [Tooltip("全身照俯仰角")]
        public float FullBodyPitch = 10f;

        [Tooltip("拍照后恢复原视角的速度")]
        public float PhotoReturnSpeed = 8f;

        private ThirdPersonCamera _camera;
        private bool _isPhotoMode;
        private float _prePhotoDistance;
        private float _prePhotoPitch;
        private float _prePhotoYaw;
        private float _targetPhotoDistance;
        private float _targetPhotoPitch;
        private bool _photoTransitioning;

        public override void OnStart()
        {
            _camera = Actor.GetScript<ThirdPersonCamera>();
            if (_camera == null)
            {
                _camera = Actor.Parent?.GetScript<ThirdPersonCamera>();
            }
            _isPhotoMode = false;
            _photoTransitioning = false;
        }

        public override void OnUpdate()
        {
            if (_camera == null) return;

            if (Input.GetKeyDown(TakeHeadshotKey))
            {
                EnterPhotoMode(HeadshotDistance, HeadshotPitch);
            }
            else if (Input.GetKeyDown(TakeFullBodyKey))
            {
                EnterPhotoMode(FullBodyDistance, FullBodyPitch);
            }
            else if (Input.GetKeyDown(TakeScreenshotKey))
            {
                Debug.Log("Screenshot taken");
            }

            if (_photoTransitioning)
            {
                UpdatePhotoTransition();
            }
        }

        private void EnterPhotoMode(float distance, float pitch)
        {
            _isPhotoMode = true;
            _prePhotoDistance = _camera.Distance;
            _prePhotoPitch = _camera.Pitch;
            _prePhotoYaw = _camera.Yaw;
            _targetPhotoDistance = distance;
            _targetPhotoPitch = pitch;
            _photoTransitioning = true;
        }

        private void UpdatePhotoTransition()
        {
            float speed = Time.DeltaTime * PhotoReturnSpeed;
            _camera.Distance = Mathf.Lerp(_camera.Distance, _targetPhotoDistance, speed);
            _camera.Pitch = Mathf.Lerp(_camera.Pitch, _targetPhotoPitch, speed);

            float distDiff = Mathf.Abs(_camera.Distance - _targetPhotoDistance);
            float pitchDiff = Mathf.Abs(_camera.Pitch - _targetPhotoPitch);

            if (distDiff < 0.1f && pitchDiff < 0.5f)
            {
                if (_isPhotoMode)
                {
                    _isPhotoMode = false;
                    _targetPhotoDistance = _prePhotoDistance;
                    _targetPhotoPitch = _prePhotoPitch;
                }
                else
                {
                    _photoTransitioning = false;
                }
            }
        }

        public bool IsPhotoModeActive()
        {
            return _isPhotoMode || _photoTransitioning;
        }
    }
}
