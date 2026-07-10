using System.Net;
using Newtonsoft.Json;
using Silmoon.AI.Anthropic.Models;
using Silmoon.Extensions;
using Silmoon.Models;

namespace Silmoon.AI.Anthropic;

public class AnthropicHttpClient : SseHttpClient
{
    public static JsonSerializerSettings SerializerSettings => NativeApiJson.SerializerSettings;

    public AnthropicHttpClient(int? requestTimeoutMilliseconds = null) : base(requestTimeoutMilliseconds) { }
    public AnthropicHttpClient(bool disableProxy, int? requestTimeoutMilliseconds = null) : base(disableProxy, requestTimeoutMilliseconds) { }
    public AnthropicHttpClient(HttpClientHandler httpClientHandler, int? requestTimeoutMilliseconds = null) : base(httpClientHandler, requestTimeoutMilliseconds) { }

    public async Task<StateSet<bool, AnthropicResponse>> MessagesAsync(string url, AnthropicRequest request)
    {
        request.Stream = false;
        var response = await PostJsonStringAsync(url, JsonConvert.SerializeObject(request, SerializerSettings));
        if (!response.State) return false.ToStateSet<AnthropicResponse>(null, response.Message);

        var responseData = JsonConvert.DeserializeObject<AnthropicResponse>(response.Data, SerializerSettings);
        return true.ToStateSet(responseData);
    }
    public async Task<StateSet<bool, AnthropicStreamEvent[]>> MessagesStreamAsync(string url, AnthropicRequest request, Func<StateSet<bool, AnthropicStreamEvent>, Task> callback)
    {
        request.Stream = true;
        List<AnthropicStreamEvent> events = [];

        var response = await PostServerSentEventsAsync(url, JsonConvert.SerializeObject(request, SerializerSettings), async state =>
        {
            if (!state.State)
            {
                await callback(false.ToStateSet<AnthropicStreamEvent>(null, state.Message));
                return;
            }

            if (state.Data is null) return;

            try
            {
                var item = JsonConvert.DeserializeObject<AnthropicStreamEvent>(state.Data, SerializerSettings);
                if (item is null) return;
                events.Add(item);
                await callback(true.ToStateSet(item));
            }
            catch (Exception ex)
            {
                await callback(false.ToStateSet<AnthropicStreamEvent>(null, $"Exception during JSON deserialization: {ex.Message}, Line: {state.Data}"));
            }
        });

        if (!response.State) return false.ToStateSet<AnthropicStreamEvent[]>(null, response.Message);
        return true.ToStateSet<AnthropicStreamEvent[]>([.. events]);
    }
}

