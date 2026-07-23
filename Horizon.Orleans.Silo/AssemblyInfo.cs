// 注册 Grain 实现程序集，确保 Orleans 运行时能发现所有 grain 实现。
// 缺失此配置会导致客户端 GetGrain<T> 调用时抛出
// "Could not find an implementation for interface" 异常。
// 本文件与 Gateway 的 Program.cs 中 [assembly: Orleans.ApplicationPart(...)] 属性等效。
[assembly: Orleans.ApplicationPart("Horizon.Orleans.Grains")]
[assembly: Orleans.ApplicationPart("Horizon.Orleans.Interface")]