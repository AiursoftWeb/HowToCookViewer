using System.Text.RegularExpressions;

using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.HowToCookViewer.Configuration;

[ExcludeFromCodeCoverage]
public class OidcSettings
{
    /// <summary>
    /// Your OIDC provider's discovery endpoint address. This is the base URL for the OIDC server.
    /// Example: "https://auth.aiursoft.com" or "https://accounts.google.com"
    /// </summary>
    public required string Authority { get; init; } = "https://your-oidc-provider.com";

    /// <summary>
    /// Extracts the scheme and host from the Authority URL.
    /// For example, if Authority is "https://auth.aiursoft.com/something", this returns "https://auth.aiursoft.com".
    /// If the Authority format is invalid, it returns the original full string.
    /// </summary>
    /// <returns>The base URL of the authority.</returns>
    public string GetAuthProviderHomePage()
    {
        // This regex pattern matches "http://" or "https://" at the start of the string,
        // followed by any characters that are not a forward slash.
        const string pattern = @"^https?:\/\/[^/]+";
        var match = Regex.Match(Authority, pattern);

        // If a match is found, return the matched part (e.g., "https://auth.aiursoft.com").
        // Otherwise, return the original Authority string as a fallback.
        return match.Success ? match.Value : Authority;
    }

    /// <summary>
    /// The Client ID of your application, obtained after registering your application with the OIDC provider.
    /// This is a public identifier for your app.
    /// </summary>
    public required string ClientId { get; init; } = "your-client-id";

    /// <summary>
    /// The Client Secret of your application, also obtained from the OIDC provider.
    /// This is a confidential credential and must be kept secret. It's used to authenticate your application to the provider.
    /// </summary>
    public required string ClientSecret { get; init; } = "your-client-secret";

    /// <summary>
    /// The name of the claim in the OIDC token that contains the user's roles or groups.
    /// The application will use this to synchronize the user's roles. Common values are "groups" or "role".
    /// </summary>
    public required string RolePropertyName { get; init; } = "groups";

    /// <summary>
    /// The name of the claim in the OIDC token that should be mapped to the local application's username.
    /// Common values include "preferred_username", "name", or "unique_name".
    /// </summary>
    public required string UsernamePropertyName { get; init; } = "preferred_username";

    /// <summary>
    /// The name of the claim in the OIDC token that contains the user's display name.'
    /// Common values include "name", "given_name", or "preferred_username".
    /// </summary>
    public required string UserDisplayNamePropertyName { get; init; } = "name";

    /// <summary>
    /// The name of the claim in the OIDC token that contains the user's email address.
    /// Typically, this is simply "email".
    /// </summary>
    public required string EmailPropertyName { get; init; } = "email";

    /// <summary>
    /// The name of the claim in the OIDC token that represents the user's unique and immutable identifier from the provider.
    /// This is used as the `ProviderKey` to link the external account to a local user. The standard OIDC claim for this is "sub".
    /// </summary>
    public required string UserIdentityPropertyName { get; init; } = "sub";
}
