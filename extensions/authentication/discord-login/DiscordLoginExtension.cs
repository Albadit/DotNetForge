using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using DotNetForge.Sdk;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordLogin;

/// <summary>
/// Entry point of the Discord Login extension: an OAuth 2.0 scheme for Discord plus a "Sign in with Discord" button
/// (<c>AddLoginProvider</c>). The scheme signs into the host's temporary external cookie; the host then signs in the
/// CMS user with the same email - or, while Enable sign-ups is on, creates one. Only a <b>verified</b> Discord email
/// is passed on, so nobody can claim an address they don't own.
/// </summary>
public sealed class DiscordLoginExtension : IExtension
{
    public const string Scheme = "discord";
    public const string ClientIdVariable = "DISCORD_CLIENT_ID";
    public const string ClientSecretVariable = "DISCORD_CLIENT_SECRET";

    public void ConfigureServices(IServiceCollection services, ExtensionContext context)
    {
        // A remote scheme is consulted on every request, so it must only exist when it is fully configured. Throwing
        // here marks the extension "not running" on the Plugins screen with this message.
        var clientId = context.GetEnvironmentValue(ClientIdVariable);
        var clientSecret = context.GetEnvironmentValue(ClientSecretVariable);
        if (clientId is null || clientSecret is null)
        {
            throw new InvalidOperationException(
                $"Set {ClientIdVariable} and {ClientSecretVariable} in .env (from your Discord application), then restart.");
        }

        services.AddAuthentication().AddOAuth(Scheme, "Discord", options =>
        {
            options.SignInScheme = ExternalLogin.SignInScheme;
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.CallbackPath = "/auth/discord/callback";
            options.AuthorizationEndpoint = "https://discord.com/oauth2/authorize";
            options.TokenEndpoint = "https://discord.com/api/oauth2/token";
            options.UserInformationEndpoint = "https://discord.com/api/users/@me";
            foreach (var scope in context.GetStringList("scopes").DefaultIfEmpty("identify").Append("email").Distinct())
            {
                options.Scope.Add(scope);
            }

            // Lax works for the top-level redirect back from Discord, also on plain-HTTP development hosts.
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.Events.OnCreatingTicket = AddUserClaimsAsync;
        });

        services.AddLoginProvider(context, Scheme, "Discord");
    }

    /// <summary>Reads the Discord user and adds id, name and - only when Discord has verified it - email claims.</summary>
    private static async Task AddUserClaimsAsync(OAuthCreatingTicketContext ticket)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ticket.Options.UserInformationEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket.AccessToken);
        using var response = await ticket.Backchannel.SendAsync(request, ticket.HttpContext.RequestAborted);
        response.EnsureSuccessStatusCode();

        using var user = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ticket.HttpContext.RequestAborted));
        var root = user.RootElement;
        var identity = ticket.Identity!;
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, root.GetProperty("id").GetString()!));
        if (root.TryGetProperty("username", out var name) && name.GetString() is { } username)
        {
            identity.AddClaim(new Claim(ClaimTypes.Name, username));
        }

        if (root.TryGetProperty("verified", out var verified) && verified.ValueKind == JsonValueKind.True &&
            root.TryGetProperty("email", out var email) && email.GetString() is { Length: > 0 } address)
        {
            identity.AddClaim(new Claim(ClaimTypes.Email, address));
        }
    }
}
