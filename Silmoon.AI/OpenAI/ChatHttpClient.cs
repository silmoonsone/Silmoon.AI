using System.Net;
using Newtonsoft.Json;
using Silmoon.AI.OpenAI.Models;
using Silmoon.Extensions;
using Silmoon.Models;

namespace Silmoon.AI.OpenAI;

public class ChatHttpClient : SseHttpClient
{
    public static JsonSerializerSettings SerializerSettings => NativeApiJson.SerializerSettings;
    public static JsonSerializerSettings RequestSerializerSettings => NativeApiJson.RequestSerializerSettings;

    public ChatHttpClient(int? requestTimeoutMilliseconds = null) : base(requestTimeoutMilliseconds) { }
    public ChatHttpClient(bool disableProxy, int? requestTimeoutMilliseconds = null) : base(disableProxy, requestTimeoutMilliseconds) { }
    public ChatHttpClient(HttpClientHandler httpClientHandler, int? requestTimeoutMilliseconds = null) : base(httpClientHandler, requestTimeoutMilliseconds) { }

    public async Task<StateSet<bool, ChatCompletionsResponse>> CompletionsAsync(string url, ChatCompletionsRequest request)
    {
        request.Stream = false;
        var response = await PostJsonStringAsync(url, request.ToJsonRequestString(RequestSerializerSettings));
        if (!response.State) return false.ToStateSet<ChatCompletionsResponse>(null, response.Message);

        var responseData = JsonConvert.DeserializeObject<ChatCompletionsResponse>(response.Data, SerializerSettings);
        return true.ToStateSet(responseData);
    }
    public async Task<StateSet<bool, ChatCompletionsChunk[]>> CompletionsStreamAsync(string url, ChatCompletionsRequest request, Func<StateSet<bool, ChatCompletionsChunk>, Task> callback)
    {
        request.Stream = true;
        List<ChatCompletionsChunk> chunks = [];

        var response = await PostServerSentEventsAsync(url, request.ToJsonRequestString(RequestSerializerSettings), async state =>
        {
            if (!state.State)
            {
                await callback(false.ToStateSet<ChatCompletionsChunk>(null, state.Message));
                return;
            }

            if (state.Data is null)
            {
                await callback(true.ToStateSet<ChatCompletionsChunk>(null));
                return;
            }

            try
            {
                var chunkData = JsonConvert.DeserializeObject<ChatCompletionsChunk>(state.Data, SerializerSettings);
                if (chunkData != null && chunkData.Choices is not null)
                {
                    chunks.Add(chunkData);
                    await callback(true.ToStateSet(chunkData));
                }
                else await callback(false.ToStateSet<ChatCompletionsChunk>(null, state.Data));
            }
            catch (Exception ex)
            {
                await callback(false.ToStateSet<ChatCompletionsChunk>(null, $"Exception during JSON deserialization: {ex.Message}, Line: {state.Data}"));
            }
        }, maxRetryCount: 5, notifyDone: true);

        if (!response.State) return false.ToStateSet<ChatCompletionsChunk[]>(null, response.Message);
        return true.ToStateSet<ChatCompletionsChunk[]>([.. chunks]);
    }
}

