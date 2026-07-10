using System.Net;
using System.Text;
using Silmoon.Extensions;
using Silmoon.Models;

namespace Silmoon.AI;

public class SseHttpClient : HttpClient
{
    public SseHttpClient(int? requestTimeoutMilliseconds = null) : base(new HttpClientHandler { UseProxy = false, Proxy = null }) => ApplyRequestTimeout(requestTimeoutMilliseconds);
    public SseHttpClient(bool disableProxy, int? requestTimeoutMilliseconds = null) : base(new HttpClientHandler { UseProxy = !disableProxy, Proxy = null }) => ApplyRequestTimeout(requestTimeoutMilliseconds);
    public SseHttpClient(HttpClientHandler httpClientHandler, int? requestTimeoutMilliseconds = null) : base(httpClientHandler) => ApplyRequestTimeout(requestTimeoutMilliseconds);

    void ApplyRequestTimeout(int? requestTimeoutMilliseconds)
    {
        if (requestTimeoutMilliseconds.HasValue)
            Timeout = requestTimeoutMilliseconds < 0 ? System.Threading.Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(requestTimeoutMilliseconds.Value);
    }

    public async Task<StateSet<bool, string>> PostJsonStringAsync(string url, string json)
    {
        try
        {
            using var httpRequest = CreateJsonPostRequest(url, json);
            using var response = await SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
            var content = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK) return true.ToStateSet<string>(content);
            return false.ToStateSet<string>(null, $"HTTP error: {response.StatusCode}, Content: {content}");
        }
        catch (Exception ex)
        {
            return false.ToStateSet<string>(null, $"Exception during HTTP request: {ex.Message}");
        }
    }
    public async Task<StateSet<bool, string[]>> PostServerSentEventsAsync(string url, string json, Func<StateSet<bool, string>, Task> callback, int maxRetryCount = 0, bool notifyDone = false)
    {
        int retryCount = 0;
        retry:
        try
        {
            using var httpRequest = CreateJsonPostRequest(url, json);
            using var response = await SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                var message = $"Http status code: {response.StatusCode}, Content: {errorContent}";
                await callback(false.ToStateSet<string>(null, message));
                return false.ToStateSet<string[]>(null, message);
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            List<string> events = [];
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                var data = line[5..].Trim();
                if (data.Length == 0) continue;
                if (data == "[DONE]")
                {
                    if (notifyDone) await callback(true.ToStateSet<string>(null));
                    break;
                }

                events.Add(data);
                await callback(true.ToStateSet<string>(data));
            }

            return true.ToStateSet<string[]>([.. events]);
        }
        catch (Exception ex)
        {
            if (retryCount < maxRetryCount)
            {
                retryCount++;
                goto retry;
            }

            await callback(false.ToStateSet<string>(null, ex.Message));
            return false.ToStateSet<string[]>(null, ex.Message);
        }
    }

    static HttpRequestMessage CreateJsonPostRequest(string url, string json)
    {
        return new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}


