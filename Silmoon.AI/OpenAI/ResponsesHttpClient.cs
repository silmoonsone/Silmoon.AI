using Newtonsoft.Json;
using Silmoon.AI.OpenAI.Models;
using Silmoon.Extensions;
using Silmoon.Models;

namespace Silmoon.AI.OpenAI;

public class ResponsesHttpClient : SseHttpClient
{
    public static JsonSerializerSettings SerializerSettings => NativeApiJson.SerializerSettings;
    public static JsonSerializerSettings RequestSerializerSettings => NativeApiJson.RequestSerializerSettings;

    public ResponsesHttpClient(int? requestTimeoutMilliseconds = null) : base(requestTimeoutMilliseconds) { }
    public ResponsesHttpClient(bool disableProxy, int? requestTimeoutMilliseconds = null) : base(disableProxy, requestTimeoutMilliseconds) { }
    public ResponsesHttpClient(HttpClientHandler httpClientHandler, int? requestTimeoutMilliseconds = null) : base(httpClientHandler, requestTimeoutMilliseconds) { }

    public async Task<StateSet<bool, ResponsesResponse>> ResponsesAsync(string url, ResponsesRequest request)
    {
        request.Stream = false;
        var response = await PostJsonStringAsync(url, request.ToJsonRequestString(RequestSerializerSettings));
        if (!response.State) return false.ToStateSet<ResponsesResponse>(null, response.Message);

        var responseData = JsonConvert.DeserializeObject<ResponsesResponse>(response.Data, SerializerSettings);
        return true.ToStateSet(responseData);
    }

    public async Task<StateSet<bool, ResponsesStreamEvent[]>> ResponsesStreamAsync(string url, ResponsesRequest request, Func<StateSet<bool, ResponsesStreamEvent>, Task> callback)
    {
        request.Stream = true;
        List<ResponsesStreamEvent> events = [];

        var response = await PostServerSentEventsAsync(url, request.ToJsonRequestString(RequestSerializerSettings), async state =>
        {
            if (!state.State)
            {
                await callback(false.ToStateSet<ResponsesStreamEvent>(null, state.Message));
                return;
            }

            if (state.Data is null)
            {
                await callback(true.ToStateSet<ResponsesStreamEvent>(null));
                return;
            }

            try
            {
                var eventData = JsonConvert.DeserializeObject<ResponsesStreamEvent>(state.Data, SerializerSettings);
                if (eventData is not null)
                {
                    events.Add(eventData);
                    await callback(true.ToStateSet(eventData));
                }
                else await callback(false.ToStateSet<ResponsesStreamEvent>(null, state.Data));
            }
            catch (Exception ex)
            {
                await callback(false.ToStateSet<ResponsesStreamEvent>(null, $"Exception during JSON deserialization: {ex.Message}, Line: {state.Data}"));
            }
        }, maxRetryCount: 5, notifyDone: true);

        if (!response.State) return false.ToStateSet<ResponsesStreamEvent[]>(null, response.Message);
        return true.ToStateSet<ResponsesStreamEvent[]>([.. events]);
    }
}
