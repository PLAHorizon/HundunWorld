namespace Horizon.AI.Kernel
{
    public class SemanticKernelConfig
    {
        public string Endpoint { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ChatModelId { get; set; } = "gpt-4o-mini";
        public string EmbeddingModelId { get; set; } = "text-embedding-ada-002";
    }
}
