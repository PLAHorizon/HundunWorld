using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace HundunWorld.Game.Modules
{
    /// <summary>
    /// 模块管理器，负责游戏模块的加载、卸载和生命周期管理
    /// </summary>
    public class ModuleManager
    {
        private Dictionary<string, IGameModule> _loadedModules;
        private Dictionary<string, Assembly> _loadedAssemblies;

        public ModuleManager()
        {
            _loadedModules = new Dictionary<string, IGameModule>();
            _loadedAssemblies = new Dictionary<string, Assembly>();
        }

        /// <summary>
        /// 加载模块
        /// </summary>
        /// <param name="modulePath">模块文件路径</param>
        /// <returns>是否加载成功</returns>
        public bool LoadModule(string modulePath)
        {
            try
            {
                if (!File.Exists(modulePath))
                {
                    throw new FileNotFoundException($"模块文件不存在: {modulePath}");
                }

                // 获取模块名称
                string moduleName = Path.GetFileNameWithoutExtension(modulePath);

                // 检查是否已加载
                if (_loadedModules.ContainsKey(moduleName))
                {
                    throw new InvalidOperationException($"模块已加载: {moduleName}");
                }

                // 加载程序集
                Assembly assembly = Assembly.LoadFrom(modulePath);
                _loadedAssemblies[moduleName] = assembly;

                // 查找实现IGameModule接口的类型
                Type moduleType = assembly.GetTypes()
                    .FirstOrDefault(t => typeof(IGameModule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                if (moduleType == null)
                {
                    throw new InvalidOperationException($"在程序集中未找到实现IGameModule接口的类型: {modulePath}");
                }

                // 创建模块实例
                IGameModule module = (IGameModule)Activator.CreateInstance(moduleType);

                // 初始化模块
                module.Initialize();

                // 添加到已加载模块字典
                _loadedModules[moduleName] = module;

                return true;
            }
            catch (Exception ex)
            {
                // 记录错误日志
                Console.WriteLine($"加载模块失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 卸载模块
        /// </summary>
        /// <param name="moduleName">模块名称</param>
        /// <returns>是否卸载成功</returns>
        public bool UnloadModule(string moduleName)
        {
            try
            {
                if (!_loadedModules.ContainsKey(moduleName))
                {
                    throw new InvalidOperationException($"模块未加载: {moduleName}");
                }

                // 获取模块实例
                IGameModule module = _loadedModules[moduleName];

                // 清理模块资源
                module.Dispose();

                // 从字典中移除
                _loadedModules.Remove(moduleName);

                // 移除程序集引用
                if (_loadedAssemblies.ContainsKey(moduleName))
                {
                    _loadedAssemblies.Remove(moduleName);
                }

                return true;
            }
            catch (Exception ex)
            {
                // 记录错误日志
                Console.WriteLine($"卸载模块失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取已加载的模块
        /// </summary>
        /// <param name="moduleName">模块名称</param>
        /// <returns>模块实例</returns>
        public IGameModule GetModule(string moduleName)
        {
            if (_loadedModules.TryGetValue(moduleName, out IGameModule module))
            {
                return module;
            }

            return null;
        }

        /// <summary>
        /// 获取所有已加载的模块名称
        /// </summary>
        /// <returns>模块名称列表</returns>
        public IEnumerable<string> GetLoadedModuleNames()
        {
            return _loadedModules.Keys;
        }

        /// <summary>
        /// 检查模块是否已加载
        /// </summary>
        /// <param name="moduleName">模块名称</param>
        /// <returns>是否已加载</returns>
        public bool IsModuleLoaded(string moduleName)
        {
            return _loadedModules.ContainsKey(moduleName);
        }

        /// <summary>
        /// 释放所有模块资源
        /// </summary>
        public void DisposeAllModules()
        {
            // 卸载所有模块
            foreach (string moduleName in _loadedModules.Keys.ToList())
            {
                UnloadModule(moduleName);
            }

            _loadedModules.Clear();
            _loadedAssemblies.Clear();
        }
    }
}