namespace Helper.Lib;

using System;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

public class HttpHelper
{
    public static async Task<string> InvokeRestCallAsync(HttpClient client, string baseAddress, string url, string token)
    {
        string creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{token}"));
        client.BaseAddress = new Uri($"{baseAddress}/");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);

        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        string content = await response.Content.ReadAsStringAsync();

        return response.IsSuccessStatusCode ? content : null;
    }
}