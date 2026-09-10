using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Volo.Abp.AspNetCore.Mvc.UI.RazorPages;
using Volo.Abp.Localization;
using Volo.Abp.OpenIddict.Applications;

namespace Meta.Dow.Pages;

public class IndexModel(
    IOpenIddictApplicationRepository openIdApplicationRepository,
    ILanguageProvider languageProvider,
    IConfiguration configuration
) : AbpPageModel
{
    public List<ApplicationLink> Applications { get; protected set; } = [];

    public IReadOnlyList<LanguageInfo> Languages { get; protected set; }

    public string CurrentLanguage { get; protected set; }

    protected IOpenIddictApplicationRepository OpenIdApplicationRepository { get; } = openIdApplicationRepository;

    protected ILanguageProvider LanguageProvider { get; } = languageProvider;

    public async Task OnGetAsync()
    {
        var clients = await OpenIdApplicationRepository.GetListAsync().ConfigureAwait(false);
        Applications = clients.Select(ToApplicationLink).Where(x => !string.IsNullOrWhiteSpace(x.VisitUrl)).ToList();

        var gatewayUrl = configuration["App:GatewayUrl"];
        if (!string.IsNullOrWhiteSpace(gatewayUrl) && Applications.All(x => x.VisitUrl != gatewayUrl))
        {
            Applications.Insert(0, new ApplicationLink { DisplayName = "Gateway", VisitUrl = gatewayUrl });
        }

        Languages = await LanguageProvider.GetLanguagesAsync().ConfigureAwait(false);
        CurrentLanguage = CultureInfo.CurrentCulture.DisplayName;
    }

    private static ApplicationLink ToApplicationLink(OpenIddictApplication application)
    {
        return new ApplicationLink
        {
            DisplayName = application.DisplayName ?? application.ClientId ?? string.Empty,
            LogoUri = application.LogoUri,
            VisitUrl = ResolveVisitUrl(application),
        };
    }

    private static string? ResolveVisitUrl(OpenIddictApplication application)
    {
        if (!string.IsNullOrWhiteSpace(application.ClientUri))
        {
            return NormalizeVisitUrl(application.ClientUri);
        }

        foreach (var value in ParseUris(application.RedirectUris).Concat(ParseUris(application.PostLogoutRedirectUris)))
        {
            var normalized = NormalizeVisitUrl(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private static string? NormalizeVisitUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var origin = uri.GetLeftPart(UriPartial.Authority);
        if (uri.AbsolutePath.Contains("swagger", StringComparison.OrdinalIgnoreCase))
        {
            return origin + "/swagger";
        }

        if (
            uri.AbsolutePath.Contains("signin-oidc", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.Contains("signout", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.Contains("authentication/", StringComparison.OrdinalIgnoreCase)
        )
        {
            return origin;
        }

        return string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/" ? origin : value.TrimEnd('/');
    }

    private static IEnumerable<string> ParseUris(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            yield break;
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('['))
        {
            string[]? items = null;
            try
            {
                items = JsonSerializer.Deserialize<string[]>(trimmed);
            }
            catch (JsonException)
            {
                // OpenIddict may persist space-separated URIs instead of JSON.
            }

            if (items != null)
            {
                foreach (var item in items)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        yield return item;
                    }
                }

                yield break;
            }
        }

        foreach (var part in trimmed.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            yield return part;
        }
    }
}

public class ApplicationLink
{
    public string DisplayName { get; set; } = string.Empty;

    public string? LogoUri { get; set; }

    public string? VisitUrl { get; set; }
}
