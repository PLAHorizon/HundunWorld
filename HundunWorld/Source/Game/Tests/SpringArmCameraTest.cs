using FlaxEngine;
using System;

namespace HundunWorld.Game.Tests
{
    /// <summary>
    /// 弹簧臂相机测试脚本
    /// </summary>
    public class SpringArmCameraTest : Script
    {
        [Header("测试设置")]
        [Tooltip("弹簧臂相机组件")]
        public SpringArmCamera SpringArmCamera;

        [Tooltip("测试目标")]
        public Actor TestTarget;

        [Tooltip("测试移动速度")]
        public float MoveSpeed = 5.0f;

        [Tooltip("测试旋转速度")]
        public float RotateSpeed = 90.0f;

        [Header("测试控制")]
        [Tooltip("是否启用自动测试")]
        public bool EnableAutoTest = true;

        [Tooltip("测试类型")]
        public TestType CurrentTest = TestType.Stationary;

        [Tooltip("测试持续时间（秒）")]
        public float TestDuration = 10.0f;

        public enum TestType
        {
            Stationary,
            CircularMotion,
            StraightLine,
            ZigZag
        }

        private float _testTimer;
        private Vector3 _startPosition;
        private bool _isInitialized;

        public override void OnStart()
        {
            if (SpringArmCamera == null)
            {
                Debug.LogError("[SpringArmCameraTest] SpringArmCamera reference not set!");
                Enabled = false;
                return;
            }

            if (TestTarget == null)
            {
                Debug.LogError("[SpringArmCameraTest] TestTarget reference not set!");
                Enabled = false;
                return;
            }

            _testTimer = 0.0f;
            _startPosition = TestTarget.Position;
            _isInitialized = true;

            Debug.Log("[SpringArmCameraTest] Initialized successfully!");
        }

        public override void OnUpdate()
        {
            if (!_isInitialized || !EnableAutoTest)
                return;

            _testTimer += Time.DeltaTime;

            // 执行测试
            switch (CurrentTest)
            {
                case TestType.Stationary:
                    TestStationary();
                    break;
                case TestType.CircularMotion:
                    TestCircularMotion();
                    break;
                case TestType.StraightLine:
                    TestStraightLine();
                    break;
                case TestType.ZigZag:
                    TestZigZag();
                    break;
            }

            // 测试结束后重置
            if (_testTimer >= TestDuration)
            {
                ResetTest();
            }

            // 显示测试信息
            DisplayTestInfo();
        }

        private void TestStationary()
        {
            // 目标保持静止
            TestTarget.Position = _startPosition;
        }

        private void TestCircularMotion()
        {
            // 目标做圆周运动
            float angle = (_testTimer / TestDuration) * Mathf.TwoPi;
            float radius = 5.0f;
            TestTarget.Position = _startPosition + new Vector3(
                Mathf.Cos(angle) * radius,
                0,
                Mathf.Sin(angle) * radius
            );
        }

        private void TestStraightLine()
        {
            // 目标做直线运动
            float distance = (_testTimer / TestDuration) * 20.0f;
            TestTarget.Position = _startPosition + new Vector3(distance, 0, 0);
        }

        private void TestZigZag()
        {
            // 目标做 zig-zag 运动
            float time = _testTimer;
            float x = (time / TestDuration) * 20.0f;
            float z = Mathf.Sin(time * 2.0f) * 5.0f;
            TestTarget.Position = _startPosition + new Vector3(x, 0, z);
        }

        private void ResetTest()
        {
            _testTimer = 0.0f;
            TestTarget.Position = _startPosition;

            // 循环测试类型
            int testCount = Enum.GetValues(typeof(TestType)).Length;
            CurrentTest = (TestType)((int)(CurrentTest + 1) % testCount);

            Debug.Log($"[SpringArmCameraTest] Test completed. Switching to: {CurrentTest}");
        }

        private void DisplayTestInfo()
        {
            // 在屏幕上显示测试信息
            DebugDraw.DrawText($"SpringArm Camera Test\n" +
                $"Test Type: {CurrentTest}\n" +
                $"Time: {_testTimer:F2}s / {TestDuration:F2}s\n" +
                $"Target Position: {TestTarget.Position.ToString()}\n" +
                $"Camera Position: {SpringArmCamera.Actor.Position.ToString()}\n" +
                $"Arm Length: {SpringArmCamera.CurrentArmLength:F2}\n" +
                $"Pitch: {SpringArmCamera.CurrentPitch:F2}\n" +
                $"Yaw: {SpringArmCamera.CurrentYaw:F2}", new Float2(10, 10), Color.White, 16
               
            );
        }

        public override void OnDebugDraw()
        {
            if (!_isInitialized)
                return;

            // 绘制测试目标位置
            DebugDraw.DrawSphere(new BoundingSphere(TestTarget.Position, 0.2f), Color.Red);

            // 绘制弹簧臂
            if (SpringArmCamera != null && SpringArmCamera.Actor != null)
            {
                Vector3 focusPoint = TestTarget.Position + SpringArmCamera.FocusOffset;
                DebugDraw.DrawLine(focusPoint, SpringArmCamera.Actor.Position, Color.Green);
                DebugDraw.DrawSphere(new BoundingSphere(focusPoint, 0.1f), Color.Blue);
            }
        }
    }
}
