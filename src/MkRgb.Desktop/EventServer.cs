using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MkRgb.Core;

namespace MkRgb.Desktop;

public sealed class EventServer : IAsyncDisposable
{
    WebApplication? app;
    public async Task Start(Settings settings, Action<string, double> publish, Func<string, Task> activate)
    {
        await DisposeAsync();
        var b = WebApplication.CreateSlimBuilder();
        b.Logging.ClearProviders();
        b.WebHost.ConfigureKestrel(o => { o.ListenLocalhost(settings.ApiPort); o.Limits.MaxRequestBodySize = 65536; o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5); });
        app = b.Build();
        app.Use(async (context, next) => { try { await next(context); } catch (JsonException) { context.Response.StatusCode = 400; } catch (BadHttpRequestException) { context.Response.StatusCode = 400; } catch (InvalidOperationException) { context.Response.StatusCode = 400; } });
        bool Authorized(string? value) => value is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(settings.ApiToken));
        app.MapPost("/v1/events", async (HttpContext ctx) =>
        {
            if (!Authorized(ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.Ordinal)))
                return Results.Unauthorized();
            var data = await ctx.Request.ReadFromJsonAsync<IncomingEvent>();
            if (data is null || string.IsNullOrWhiteSpace(data.Name) || data.Name.Length > 80 || !double.IsFinite(data.Value))
                return Results.BadRequest();
            publish(data.Name, data.Value);
            return Results.Ok(new
            {
                version = 1
            });
        });
        app.MapPost("/v1/profiles/{id}", async (HttpContext ctx, string id) =>
        {
            if (!Authorized(ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.Ordinal)))
                return Results.Unauthorized();
            if (!settings.Profiles.Any(p => p.Id == id))
                return Results.NotFound();
            try
            {
                await activate(id);
            }
            catch (Exception) { return Results.Conflict(new { error = "Profile could not be activated. Check the hardware service and cooling mappings." }); }
            return Results.Ok();
        });
        app.MapPost("/v1/cs2", async (HttpContext ctx) =>
        {
            using var data = await JsonDocument.ParseAsync(ctx.Request.Body);
            var root = data.RootElement;
            if (!root.TryGetProperty("auth", out var auth) || !auth.TryGetProperty("token", out var secret) || !Authorized(secret.GetString()))
                return Results.Unauthorized();
            if (root.TryGetProperty("player", out var player))
            {
                if (player.TryGetProperty("state", out var state))
                    foreach (var name in new[] { "health", "armor", "flashed", "burning" })
                        if (state.TryGetProperty(name, out var val) && val.TryGetDouble(out var n))
                            publish(name, n);
                if (player.TryGetProperty("weapons", out var weapons))
                    foreach (var w in weapons.EnumerateObject())
                        if (w.Value.TryGetProperty("state", out var st) && st.GetString() == "active" && w.Value.TryGetProperty("ammo_clip", out var ammo))
                            publish("ammo", ammo.GetDouble());
            }
            if (root.TryGetProperty("round", out var round) && round.TryGetProperty("phase", out var phase))
                publish("round_live", phase.GetString() == "live" ? 100 : 0);
            return Results.Ok();
        });
        await app.StartAsync();
    }
    public static string Cs2Config(Settings s) => $$"""
"MK RGB"
{
  "uri" "http://127.0.0.1:{{s.ApiPort}}/v1/cs2"
  "timeout" "5.0"
  "buffer" "0.1"
  "throttle" "0.1"
  "heartbeat" "10.0"
  "auth" { "token" "{{s.ApiToken}}" }
  "data" { "provider" "1" "player_id" "1" "player_state" "1" "player_weapons" "1" "round" "1" }
}
""";
    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
            app = null;
        }
    }
    record IncomingEvent(string Name, double Value);
}
