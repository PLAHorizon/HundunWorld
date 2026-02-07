using System;
using FlaxEngine;
using FlaxEngine.GUI;
using System.Threading.Tasks;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// ConfirmDialog测试脚本
    /// 用于验证按钮显示和粒子效果修复
    /// </summary>
    public class ConfirmDialogTest : Script
    {
        private ConfirmDialog _testDialog;
        
        public override void OnStart()
        {
            FlaxEngine.Debug.Log("ConfirmDialog测试开始...");
            
            // 延迟创建测试对话框，确保UI系统完全初始化
            Task.Delay(1000).ContinueWith(_ => TestDialogDisplay());
        }
        
        private void TestDialogDisplay()
        {
            try
            {
                FlaxEngine.Debug.Log("=== 开始测试ConfirmDialog ===");
                
                // 创建测试对话框
                _testDialog = new ConfirmDialog();
                
                // 添加事件处理
                _testDialog.Confirmed += OnTestConfirmed;
                _testDialog.Cancelled += OnTestCancelled;
                
                // 显示简单对话框
                _testDialog.ShowSimple("测试对话框", "这是一个测试对话框，用于验证按钮显示和粒子效果。");
                
                FlaxEngine.Debug.Log("测试对话框创建完成");
                
                // 检查组件状态
                Task.Delay(500).ContinueWith(_ => CheckDialogStatus());
                
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"测试对话框创建失败: {ex.Message}");
            }
        }
        
        private void CheckDialogStatus()
        {
            if (_testDialog == null)
            {
                FlaxEngine.Debug.LogError("测试对话框为null");
                return;
            }
            
            FlaxEngine.Debug.Log("=== 对话框状态检查 ===");
            FlaxEngine.Debug.Log($"对话框实例: {(_testDialog != null ? "存在" : "不存在")}");
            
            // 这里可以添加更多的状态检查逻辑
            // 注意：由于按钮是私有字段，我们无法直接访问进行检查
            // 但是可以通过日志输出来验证修复是否生效
            
            FlaxEngine.Debug.Log("状态检查完成，请查看控制台日志输出");
        }
        
        private void OnTestConfirmed()
        {
            FlaxEngine.Debug.Log("✅ 确认按钮点击测试成功！");
            FlaxEngine.Debug.Log("✨ 按钮点击功能已修复！");
            _testDialog = null;
        }
        
        private void OnTestCancelled()
        {
            FlaxEngine.Debug.Log("❌ 取消按钮点击测试成功！");
            FlaxEngine.Debug.Log("✨ 按钮点击功能已修复！");
            _testDialog = null;
        }
        
        public override void OnUpdate()
        {
            // 按ESC键创建新的测试对话框
            if (Input.GetKeyDown(KeyboardKeys.Escape) && _testDialog == null)
            {
                FlaxEngine.Debug.Log("按下ESC键，重新创建测试对话框");
                TestDialogDisplay();
            }
            
            // 按F1键测试高级对话框
            if (Input.GetKeyDown(KeyboardKeys.F1) && _testDialog == null)
            {
                FlaxEngine.Debug.Log("按下F1键，测试高级对话框功能");
                TestAdvancedDialog();
            }
        }
        
        private void TestAdvancedDialog()
        {
            try
            {
                _testDialog = new ConfirmDialog();
                _testDialog.Confirmed += OnTestConfirmed;
                _testDialog.Cancelled += OnTestCancelled;
                
                // 测试高级功能
                _testDialog.ShowAdvanced(
                    "高级测试对话框",
                    "这是一个高级功能测试，包含粒子效果。",
                    default(Sprite), // 无图标
                    null, // 无粒子效果Actor
                    null, // 无条目列表
                    true, // 显示按钮
                    null
                );
                
                FlaxEngine.Debug.Log("高级测试对话框创建完成");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"高级测试对话框创建失败: {ex.Message}");
            }
        }
    }
}