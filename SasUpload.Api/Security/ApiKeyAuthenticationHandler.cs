using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SasUpload.Api.Configuration;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

namespace SasUpload.Api.Security;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<ApiSecurityOptions> securityOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var suppliedKey = values.Count == 1 ? values[0] : null;
        if (string.IsNullOrEmpty(suppliedKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing API key."));
        }

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(securityOptions.Value.ApiKey));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey));
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "poc-console-client")],
            SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
