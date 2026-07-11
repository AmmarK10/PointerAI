using System.Net.Http.Json;
using System.Text.Json;

namespace PointerAI.Services;

public sealed class GeminiScreenAssistant(HttpClient client)
{
    public async Task<ScreenAssistantResult> AskAsync(string q, byte[] img, CancellationToken ct = default)
    {
        var key = Get("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("GEMINI_API_KEY is not set.");

        var model = Get("GEMINI_MODEL") ?? "gemini-3.1-flash-lite";

        using var r = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent");
        r.Headers.Add("x-goog-api-key", key);

        r.Content = JsonContent.Create(new
        {
            system_instruction = new
            {
                parts = new[] { new { text = "You are Pointer AI. Explain concisely where the requested UI target is in the screenshot. Return a normalized box. If absent, set targetFound false and box values to 0." } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = q },
                        new { inline_data = new { mime_type = "image/png", data = Convert.ToBase64String(img) } }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        answer = new { type = "STRING" },
                        targetFound = new { type = "BOOLEAN" },
                        x = new { type = "NUMBER" },
                        y = new { type = "NUMBER" },
                        width = new { type = "NUMBER" },
                        height = new { type = "NUMBER" },
                        confidence = new { type = "NUMBER" }
                    },
                    required = new[] { "answer", "targetFound", "x", "y", "width", "height", "confidence" }
                }
            }
        });

        using var res = await client.SendAsync(r, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException(Error(body, (int)res.StatusCode));

        using var d = JsonDocument.Parse(body);
        var json = d.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();

        return JsonSerializer.Deserialize<ScreenAssistantResult>(json!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Gemini returned an unreadable answer.");
    }

    static string? Get(string n) =>
        Environment.GetEnvironmentVariable(n) is { Length: > 0 } v
            ? v
            : Environment.GetEnvironmentVariable(n, EnvironmentVariableTarget.User);

    static string Error(string b, int s)
    {
        try
        {
            using var d = JsonDocument.Parse(b);
            return d.RootElement.GetProperty("error").GetProperty("message").GetString() ?? $"Gemini request failed ({s}).";
        }
        catch
        {
            return $"Gemini request failed ({s}).";
        }
    }
}