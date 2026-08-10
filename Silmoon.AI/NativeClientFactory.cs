using Silmoon.AI.Anthropic;
using Silmoon.AI.Models;
using Silmoon.AI.Interfaces;
using Silmoon.AI.OpenAI;

namespace Silmoon.AI;

public static class NativeClientFactory
{
    public static INativeClient Create(ModelProvider provider, string modelName, string systemPrompt = null, bool enableThinking = false, bool disableProxy = false, int? httpRequestTimeoutMilliseconds = null, double? temperature = null, double? topP = null) =>
        provider.ApiKind switch
        {
            NativeApiKind.Chat => new ChatClient(provider, modelName, systemPrompt, enableThinking, disableProxy, httpRequestTimeoutMilliseconds, temperature, topP),
            NativeApiKind.Responses => new ResponsesClient(provider, modelName, systemPrompt, enableThinking, disableProxy, httpRequestTimeoutMilliseconds, temperature, topP),
            NativeApiKind.Anthropic => new AnthropicClient(provider, modelName, systemPrompt, disableProxy, httpRequestTimeoutMilliseconds, temperature, topP),
            _ => throw new NotSupportedException($"Unsupported provider: {provider.ApiKind}")
        };
}
