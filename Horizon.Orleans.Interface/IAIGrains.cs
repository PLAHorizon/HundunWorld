using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// AI分析师Grain接口 - 负责智能对话分析
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IAIAnalystGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 智能对话
        /// </summary>
        Task<string> ChatAsync(string question, string conversationId);

        /// <summary>
        /// 生成每日报告
        /// </summary>
        Task<string> GenerateDailyReportAsync(DateTime date);
    }

    /// <summary>
    /// RAG检索Grain接口 - 负责向量搜索
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IRAGRetrieverGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 搜索知识库
        /// </summary>
        Task<List<string>> SearchAsync(string query, int topK);
    }

    /// <summary>
    /// 嵌入Grain接口 - 负责批量文档嵌入
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IEmbeddingGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 批量嵌入文档
        /// </summary>
        Task<int> EmbedDocumentsAsync(List<long> documentIds);
    }

    /// <summary>
    /// 知识库Grain接口 - 负责文档管理
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IKnowledgeBaseGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 上传文档
        /// </summary>
        Task<long> UploadDocumentAsync(string title, string content, string source);

        /// <summary>
        /// 获取未索引文档列表
        /// </summary>
        Task<List<long>> GetUnindexedDocumentsAsync();
    }

    /// <summary>
    /// 报告生成Grain接口 - 负责每日报告生成
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IReportGeneratorGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 生成每日报告
        /// </summary>
        Task<string> GenerateDailyReportAsync(DateTime date);
    }
}
