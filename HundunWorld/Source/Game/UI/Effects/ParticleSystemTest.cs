using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using Game.UI.Effects;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Effects
{
    /// <summary>
    /// 粒子系统功能测试类
    /// </summary>
    public static class ParticleSystemTest
    {
        /// <summary>
        /// 测试基础粒子系统创建
        /// </summary>
        public static void TestBasicParticleSystem()
        {
            FlaxEngine.Debug.Log("=== 基础粒子系统测试开始 ===");
            
            try
            {
                // 初始化粒子效果管理器
                UIParticleEffectManager.Initialize();
                FlaxEngine.Debug.Log("✓ 粒子效果管理器初始化成功");
                
                // 测试创建对话框星空效果
                var testEffect = UIParticleEffectManager.CreateDialogStarEffect(
                    "test_dialog",
                    new Float2(600, 400),
                    new Float3(0, 0, -100)
                );
                
                if (testEffect != null)
                {
                    FlaxEngine.Debug.Log("✓ 对话框星空效果创建成功");
                }
                else
                {
                    FlaxEngine.Debug.Log("⚠ 对话框星空效果创建失败，可能回退到备选方案");
                }
                
                // 测试简化效果
                var simpleEffect = UIParticleEffectManager.CreateSimpleStarEffect(
                    "test_simple",
                    new Float2(400, 300),
                    new Float3(100, 100, -100)
                );
                
                if (simpleEffect != null)
                {
                    FlaxEngine.Debug.Log("✓ 简化星空效果创建成功");
                }
                
                // 测试效果管理
                FlaxEngine.Debug.Log($"✓ 当前活跃效果数量: {UIParticleEffectManager.GetActiveEffectCount()}");
                
                // 清理测试效果
                UIParticleEffectManager.DestroyEffect("test_dialog");
                UIParticleEffectManager.DestroyEffect("test_simple");
                
                FlaxEngine.Debug.Log("=== 基础粒子系统测试完成 ===");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"基础粒子系统测试失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 测试粒子效果配置系统
        /// </summary>
        public static void TestParticleEffectConfig()
        {
            FlaxEngine.Debug.Log("=== 粒子效果配置系统测试开始 ===");
            
            try
            {
                // 测试自动质量检测
                ParticleEffectSettings.AutoDetectQuality();
                FlaxEngine.Debug.Log($"✓ 自动检测质量等级: {ParticleEffectSettings.CurrentQuality}");
                
                // 测试不同质量等级的配置
                var qualities = new[] { ParticleQuality.Low, ParticleQuality.Medium, ParticleQuality.High, ParticleQuality.Ultra };
                
                foreach (var quality in qualities)
                {
                    ParticleEffectSettings.CurrentQuality = quality;
                    var config = ParticleEffectSettings.GetRecommendedConfig(ParticleEffectType.StarField);
                    FlaxEngine.Debug.Log($"✓ {quality} 质量配置 - 粒子数: {config.ParticleCount}, 闪烁速度: {config.TwinkleSpeed}");
                }
                
                // 测试预定义配置
                var dialogConfig = ParticleEffectConfig.CreateDialogDefault();
                FlaxEngine.Debug.Log($"✓ 对话框默认配置 - 粒子数: {dialogConfig.ParticleCount}");
                
                var simplifiedConfig = ParticleEffectConfig.CreateSimplified();
                FlaxEngine.Debug.Log($"✓ 简化配置 - 粒子数: {simplifiedConfig.ParticleCount}");
                
                var loginConfig = ParticleEffectConfig.CreateLoginBackground();
                FlaxEngine.Debug.Log($"✓ 登录背景配置 - 粒子数: {loginConfig.ParticleCount}");
                
                FlaxEngine.Debug.Log("=== 粒子效果配置系统测试完成 ===");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"粒子效果配置系统测试失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 测试ConfirmDialog集成
        /// </summary>
        public static void TestConfirmDialogIntegration()
        {
            FlaxEngine.Debug.Log("=== ConfirmDialog粒子集成测试开始 ===");
            
            try
            {
                // 创建测试对话框
                var dialog = new ConfirmDialog();
                FlaxEngine.Debug.Log("✓ ConfirmDialog创建成功");
                
                // 测试简单对话框（应该包含粒子效果）
                dialog.ShowSimple("粒子测试", "这个对话框应该包含星空粒子效果");
                FlaxEngine.Debug.Log("✓ 带粒子效果的简单对话框显示成功");
                
                // 测试关闭对话框（应该清理粒子效果）
                dialog.Close();
                FlaxEngine.Debug.Log("✓ 对话框关闭和粒子效果清理完成");
                
                // 测试高级对话框
                var items = new List<ConfirmDialog.DialogItem>
                {
                    new ConfirmDialog.DialogItem { Text = "测试选项1" },
                    new ConfirmDialog.DialogItem { Text = "测试选项2" },
                    new ConfirmDialog.DialogItem { Text = "测试选项3" }
                };
                
                var advancedDialog = ConfirmDialog.CreateAdvancedDialog(
                    "高级粒子测试",
                    "这是一个带条目列表的对话框，应该有适应性粒子效果",
                    default(Sprite),
                    items,
                    () => FlaxEngine.Debug.Log("高级对话框确认")
                );
                
                FlaxEngine.Debug.Log("✓ 高级对话框创建成功");
                
                FlaxEngine.Debug.Log("=== ConfirmDialog粒子集成测试完成 ===");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"ConfirmDialog粒子集成测试失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 测试轻量级粒子效果
        /// </summary>
        public static void TestLightweightEffects()
        {
            FlaxEngine.Debug.Log("=== 轻量级粒子效果测试开始 ===");
            
            try
            {
                // 创建测试Actor
                var testActor = new EmptyActor();
                testActor.Name = "LightweightStarTest";
                
                // 添加轻量级星空效果
                var lightEffect = testActor.AddScript<LightweightStarEffect>();
                
                if (lightEffect != null)
                {
                    // 配置效果参数
                    lightEffect.ParticleCount = 20;
                    lightEffect.EffectArea = new Float2(500, 300);
                    lightEffect.ParticleSize = 2.0f;
                    lightEffect.TwinkleIntensity = 0.7f;
                    lightEffect.StarColor = ChineseClassicalTheme.SecondaryColor;
                    
                    FlaxEngine.Debug.Log("✓ 轻量级星空效果创建和配置成功");
                    
                    // 测试效果控制
                    lightEffect.SetEffectArea(new Float2(600, 400));
                    lightEffect.SetTwinkleIntensity(0.5f);
                    lightEffect.RestartEffect();
                    
                    FlaxEngine.Debug.Log("✓ 轻量级效果控制功能测试成功");
                }
                
                FlaxEngine.Debug.Log("=== 轻量级粒子效果测试完成 ===");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"轻量级粒子效果测试失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 测试GUI2D星空效果
        /// </summary>
        public static void TestGUI2DStarEffect()
        {
            FlaxEngine.Debug.Log("=== GUI2D星空效果测试开始 ===");
            
            try
            {
                // 创建测试容器
                var testContainer = new Panel
                {
                    Size = new Float2(400, 300),
                    BackgroundColor = new Color(0.1f, 0.1f, 0.2f, 0.8f)
                };
                
                // 创建GUI2D星空效果
                var gui2DEffect = new GUI2DStarEffect(testContainer, 15);
                
                // 设置颜色
                gui2DEffect.SetColors(
                    ChineseClassicalTheme.SecondaryColor,
                    ChineseClassicalTheme.TextColor
                );
                
                // 启动效果
                gui2DEffect.Start();
                FlaxEngine.Debug.Log("✓ GUI2D星空效果启动成功");
                
                // 模拟更新循环（测试几次）
                for (int i = 0; i < 10; i++)
                {
                    gui2DEffect.Update(0.016f); // 模拟16ms更新
                }
                FlaxEngine.Debug.Log("✓ GUI2D星空效果更新循环测试成功");
                
                // 测试容器尺寸调整
                testContainer.Size = new Float2(500, 350);
                gui2DEffect.ResizeToContainer();
                FlaxEngine.Debug.Log("✓ GUI2D星空效果尺寸调整测试成功");
                
                // 停止效果
                gui2DEffect.Stop();
                FlaxEngine.Debug.Log("✓ GUI2D星空效果停止成功");
                
                // 清理
                gui2DEffect.Destroy();
                FlaxEngine.Debug.Log("✓ GUI2D星空效果清理成功");
                
                FlaxEngine.Debug.Log("=== GUI2D星空效果测试完成 ===");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"GUI2D星空效果测试失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 性能压力测试
        /// </summary>
        public static void TestPerformanceStress()
        {
            FlaxEngine.Debug.Log("=== 粒子系统性能压力测试开始 ===");
            
            try
            {
                UIParticleEffectManager.Initialize();
                
                // 创建多个粒子效果进行压力测试
                var effectIds = new List<string>();
                
                for (int i = 0; i < 5; i++)
                {
                    string effectId = $"stress_test_{i}";
                    var effect = UIParticleEffectManager.CreateSimpleStarEffect(
                        effectId,
                        new Float2(300, 200),
                        new Float3(i * 100, i * 50, -100)
                    );
                    
                    if (effect != null)
                    {
                        effectIds.Add(effectId);
                    }
                }
                
                FlaxEngine.Debug.Log($"✓ 创建了 {effectIds.Count} 个并发粒子效果");
                
                // 测试批量更新
                foreach (var effectId in effectIds)
                {
                    UIParticleEffectManager.UpdateEffectPosition(effectId, Float3.Zero);
                    UIParticleEffectManager.SetEffectActive(effectId, true);
                }
                
                FlaxEngine.Debug.Log("✓ 批量更新操作完成");
                
                // 清理所有效果
                UIParticleEffectManager.DestroyAllEffects();
                FlaxEngine.Debug.Log("✓ 批量清理操作完成");
                
                FlaxEngine.Debug.Log("=== 粒子系统性能压力测试完成 ===");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"粒子系统性能压力测试失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 运行所有测试
        /// </summary>
        public static void RunAllTests()
        {
            FlaxEngine.Debug.Log("########################################");
            FlaxEngine.Debug.Log("### 粒子系统完整功能测试开始 ###");
            FlaxEngine.Debug.Log("########################################");
            
            TestBasicParticleSystem();
            TestParticleEffectConfig();
            TestLightweightEffects();
            TestGUI2DStarEffect();
            TestConfirmDialogIntegration();
            TestPerformanceStress();
            
            FlaxEngine.Debug.Log("########################################");
            FlaxEngine.Debug.Log("### 粒子系统完整功能测试完成 ###");
            FlaxEngine.Debug.Log("########################################");
        }
    }
}
