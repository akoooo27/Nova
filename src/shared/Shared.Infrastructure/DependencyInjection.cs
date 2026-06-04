using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using Shared.Application.Authentication;
using Shared.Infrastructure.Authentication;
using Shared.Infrastructure.Clock;
using Shared.Infrastructure.DomainEvents;
using Shared.Infrastructure.Options;

using SharedKernel;

namespace Shared.Infrastructure;

public static class DependencyInjection
{

    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        services.AddScoped<IUserContext, UserContext>();

        return services;
    }

    public static IServiceCollection AddAuth0JwtAuthentication(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<Auth0JwtOptions>()
            .BindConfiguration(Auth0JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<Auth0JwtOptions>>((jwt, wrapper) =>
            {
                Auth0JwtOptions auth0 = wrapper.Value;

                jwt.Authority = $"https://{auth0.Domain}/";
                jwt.Audience = auth0.Audience;
                jwt.MapInboundClaims = false;

                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "sub",
                    RoleClaimType = "roles",
                };
            });

        services.AddAuthorization();

        return services;
    }

    public static IServiceCollection AddDomainEventsDispatcher(this IServiceCollection services)
    {
        services.AddScoped<IDomainEventsDispatcher, DomainEventsDispatcher>();

        return services;
    }
}