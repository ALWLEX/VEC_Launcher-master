using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using VECLauncher.Models;

namespace VECLauncher.Services;

public sealed class VecAuthService
{
    public const string DefaultVecServerUrl = "http://95.59.233.227:8080";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static async Task<string> GetActiveServerUrlAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var resp = await http.GetAsync("http://localhost:8080/api/info");
            if (resp.IsSuccessStatusCode) return "http://localhost:8080";
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        return DefaultVecServerUrl;
    }

    public async Task<MinecraftAccount> LoginAsync(string login, string password, string? serverUrl = null)
    {
        var url = string.IsNullOrWhiteSpace(serverUrl) ? DefaultVecServerUrl : serverUrl.TrimEnd('/');

        try
        {
            var payload = new
            {
                username = login,
                password = password,
                clientToken = Guid.NewGuid().ToString("N"),
                requestUser = true
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{url}/api/auth/login", content);

            if (!resp.IsSuccessStatusCode)
            {
                resp = await _http.PostAsync($"{url}/authserver/authenticate", content);
            }

            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                var json = JsonNode.Parse(body);

                var token = json?["accessToken"]?.ToString() ?? Guid.NewGuid().ToString("N");
                var userNode = json?["selectedProfile"] ?? json?["user"];
                var username = userNode?["name"]?.ToString() ?? login;
                var uuid = userNode?["id"]?.ToString() ?? Guid.NewGuid().ToString("N");
                var skinUrl = json?["skinUrl"]?.ToString() ?? $"{url}/api/skin/{username}.png";

                return new MinecraftAccount
                {
                    Username = username,
                    Uuid = uuid,
                    AccessToken = token,
                    Type = AccountType.Vec,
                    ServerUrl = url,
                    SkinUrl = skinUrl,
                    CapeUrl = $"{url}/api/cape/{username}.png",
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
                };
            }
            else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized || resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new Exception("Invalid login or password on VEC server.");
            }
        }
        catch (Exception ex) when (!ex.Message.Contains("Invalid login"))
        {
            Log.Warn($"VecAuthService: VEC remote server offline ({url}): {ex.Message}. Using secure local database.");
        }

        var (success, message, account) = VecAccountDatabase.Login(login, password);
        if (!success || account == null)
        {
            throw new Exception(message);
        }

        return account;
    }

    public async Task<MinecraftAccount> RegisterAsync(string username, string password, string? email = null, string? serverUrl = null)
    {
        var url = string.IsNullOrWhiteSpace(serverUrl) ? DefaultVecServerUrl : serverUrl.TrimEnd('/');

        try
        {
            var payload = new
            {
                username = username,
                password = password,
                email = email ?? $"{username.ToLowerInvariant()}@vec.kpvk.edu.kz"
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{url}/api/auth/register", content);

            if (resp.IsSuccessStatusCode)
            {
                return await LoginAsync(username, password, url);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"VecAuthService: VEC server registration offline ({url}): {ex.Message}");
        }

        var (success, message, account) = VecAccountDatabase.Register(username, password, email);
        if (!success || account == null)
        {
            throw new Exception(message);
        }

        return account;
    }
}