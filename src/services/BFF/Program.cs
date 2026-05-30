using BFF.Database;
using BFF.FrontendProxy;

using Duende.Bff;
using Duende.Bff.Builder;
using Duende.Bff.DynamicFrontends;
using Duende.Bff.EntityFramework;
using Duende.Bff.Yarp;

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

using Shared.Infrastructure.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<BffSessionDbContext>("bff-db");

builder.Services.AddOptions<Auth0Options>()
    .BindConfiguration(Auth0Options.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddBff(options =>
    {
        options.SessionCleanupInterval = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkServerSideSessionsServices<BffSessionDbContext, IBffServicesBuilder>()
    .AddSessionCleanupBackgroundProcess()
    .AddRemoteApis()
    .ConfigureOpenIdConnect(_ => { })
    .ConfigureCookies(options =>
    {
        options.Cookie.Name = "__Nova-bff";
        options.Cookie.SameSite = SameSiteMode.Strict;
    });

builder.Services
    .AddReverseProxy()
    .LoadFromMemory
    (
        FrontendProxyConfiguration.CreateRoutes(),
        FrontendProxyConfiguration.CreateClusters(
            FrontendProxyConfiguration.GetFrontendAddress(builder.Configuration))
    )
    .AddBffExtensions();

builder.Services
    .AddOptions<OpenIdConnectOptions>(BffAuthenticationSchemes.BffOpenIdConnect)
    .Configure<IOptions<Auth0Options>>((oidc, wrapper) =>
    {
        Auth0Options auth0 = wrapper.Value;

        oidc.Authority = $"https://{auth0.Domain}/";
        oidc.ClientId = auth0.ClientId;
        oidc.ClientSecret = auth0.ClientSecret;
        oidc.ResponseType = "code";
        oidc.ResponseMode = "query";

        oidc.GetClaimsFromUserInfoEndpoint = true;
        oidc.MapInboundClaims = false;
        oidc.SaveTokens = true;
        oidc.DisableTelemetry = true;

        oidc.Scope.Clear();
        oidc.Scope.Add("openid");
        oidc.Scope.Add("profile");
        oidc.Scope.Add("offline_access");

        oidc.TokenValidationParameters = new()
        {
            NameClaimType = "name",
            RoleClaimType = "role",
        };

        oidc.Events.OnRedirectToIdentityProvider = ctx =>
        {
            ctx.ProtocolMessage.SetParameter("audience", auth0.Audience);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddDataProtection()
    .SetApplicationName("BFF");

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

app.UseHttpsRedirection();

app.UseWebSockets();
app.UseAuthentication();
app.UseBff();
app.UseAuthorization();

app.MapReverseProxy();

await app.RunAsync();