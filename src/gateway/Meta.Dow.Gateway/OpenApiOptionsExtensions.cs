using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Meta.Dow.Gateway;

public static class OpenApiOptionsExtensions
{
    public static OpenApiOptions UseJwtBearerAuthentication(this OpenApiOptions options)
    {
        var securityScheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Name = JwtBearerDefaults.AuthenticationScheme,
            Scheme = JwtBearerDefaults.AuthenticationScheme,
        };

        var schemeReference = new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme);

        options.AddDocumentTransformer(
            (document, context, cancellationToken) =>
            {
                document.Components ??= new();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[JwtBearerDefaults.AuthenticationScheme] = securityScheme;

                return Task.CompletedTask;
            }
        );

        options.AddOperationTransformer(
            (operation, context, cancellationToken) =>
            {
                if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any())
                {
                    operation.Security = [new() { [schemeReference] = [] }];
                }

                return Task.CompletedTask;
            }
        );

        return options;
    }
}
