using System;
using System.Linq;
using System.Reflection;

namespace Horizon.Core
{
    /// <summary>
    /// 反射工具类，用于安全地处理类型加载和反射操作
    /// </summary>
    public static class ReflectionHelper
    {
        /// <summary>
        /// 安全地获取程序集中的类型，处理ReflectionTypeLoadException异常
        /// </summary>
        /// <param name="assembly">要获取类型的程序集</param>
        /// <returns>成功加载的类型数组</returns>
        public static Type[] GetTypesSafely(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // 记录无法加载的类型信息
                var failedTypes = ex.Types.Where(t => t == null).Count();
                Console.WriteLine($"[警告] 无法加载 {failedTypes} 个类型");
                
                // 记录具体的加载异常
                if (ex.LoaderExceptions != null)
                {
                    foreach (var loaderException in ex.LoaderExceptions)
                    {
                        Console.WriteLine($"[错误] 类型加载失败: {loaderException?.Message}");
                    }
                }
                
                // 返回成功加载的类型
                return ex.Types.Where(t => t != null).ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 获取类型时发生未知异常: {ex.Message}");
                return new Type[0];
            }
        }
        
        /// <summary>
        /// 安全地从指定路径加载程序集并获取类型
        /// </summary>
        /// <param name="assemblyPath">程序集路径</param>
        /// <returns>成功加载的类型数组</returns>
        public static Type[] LoadTypesSafely(string assemblyPath)
        {
            try
            {
                var assembly = Assembly.LoadFrom(assemblyPath);
                return GetTypesSafely(assembly);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 加载程序集失败 '{assemblyPath}': {ex.Message}");
                return new Type[0];
            }
        }
        
        /// <summary>
        /// 安全地从程序集名称加载程序集并获取类型
        /// </summary>
        /// <param name="assemblyName">程序集名称</param>
        /// <returns>成功加载的类型数组</returns>
        public static Type[] LoadTypesSafely(AssemblyName assemblyName)
        {
            try
            {
                var assembly = Assembly.Load(assemblyName);
                return GetTypesSafely(assembly);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 加载程序集失败 '{assemblyName.FullName}': {ex.Message}");
                return new Type[0];
            }
        }
        
        /// <summary>
        /// 检查类型是否实现了指定的接口
        /// </summary>
        /// <param name="type">要检查的类型</param>
        /// <param name="interfaceType">接口类型</param>
        /// <returns>是否实现了指定接口</returns>
        public static bool ImplementsInterface(Type type, Type interfaceType)
        {
            if (type == null || interfaceType == null)
                return false;
                
            return interfaceType.IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract;
        }
        
        /// <summary>
        /// 安全地创建类型的实例
        /// </summary>
        /// <param name="type">要创建实例的类型</param>
        /// <param name="args">构造函数参数</param>
        /// <returns>创建的实例，如果失败则返回null</returns>
        public static object CreateInstanceSafely(Type type, params object[] args)
        {
            try
            {
                return Activator.CreateInstance(type, args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 创建类型实例失败 '{type.FullName}': {ex.Message}");
                return null;
            }
        }
    }
}