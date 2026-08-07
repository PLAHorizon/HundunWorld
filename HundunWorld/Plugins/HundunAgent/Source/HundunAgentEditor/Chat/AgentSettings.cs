using System;
using System.IO;
using System.Text.Json;

namespace HundunAgent.Chat
{
    /// <summary>
    /// HundunAgent 聊天设置（持久化到 Cache/HundunAgent/settings.json）。
    /// 支持任意 OpenAI 兼容接口（BaseUrl + ApiKey + Model）。
    /// </summary>
    public sealed class AgentSettings
    {
        private static AgentSettings _instance;
        private static readonly object _lock = new object();

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true
        };

        /// <summary>OpenAI 兼容服务根地址，如 https://api.openai.com/v1 或本地 http://localhost:8000/v1。</summary>
        public string BaseUrl = "";

        /// <summary>API Key。</summary>
        public string ApiKey = "";

        /// <summary>模型名，如 gpt-4o、deepseek-chat、qwen-max。</summary>
        public string Model = "gpt-4o";

        /// <summary>单个任务允许的最大工具调用轮数。</summary>
        public int MaxToolSteps = 30;

        /// <summary>请求超时（秒）。</summary>
        public int RequestTimeoutSeconds = 180;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Model);

        public static AgentSettings Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = Load();
                    }
                    return _instance;
                }
            }
        }

        public static string SettingsPath =>
            Path.Combine(FlaxEngine.Globals.ProjectFolder, "Cache", "HundunAgent", "settings.json");

        private static AgentSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var loaded = JsonSerializer.Deserialize<AgentSettings>(json, SerializerOptions);
                    if (loaded != null)
                        return loaded;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning("[HundunAgent] 设置读取失败: " + ex.Message);
            }

            return new AgentSettings();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(this, SerializerOptions);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning("[HundunAgent] 设置保存失败: " + ex.Message);
            }
        }
    }
}
