using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Veyra.Access.Api;
using Veyra.Access.Keys;
using Veyra.Access.Sessions;
using Veyra.Access.Setup;
using Veyra.Access.Users;
using Veyra.Core;
using Veyra.Core.Schema;
using Veyra.Core.Security;

namespace Veyra.Access;

/// <summary>
/// Registers the Access module with the dependency injection container. <c>Veyra.Api</c> calls it at startup.
/// </summary>
public static class AccessServiceCollectionExtensions
{
    /// <summary>The header that carries the antiforgery token on requests that change state.</summary>
    public const string AntiforgeryHeaderName = "X-Veyra-Antiforgery";

    /// <summary>The rate limiting policy for sign-in and first-run setup.</summary>
    public const string SignInRateLimitPolicy = "veyra.sign-in";

    private const string _sessionCookieName = "veyra.session";

    private const string _antiforgeryCookieName = "veyra.antiforgery";

    /// <summary>
    /// Registers the Access module: users, roles, sessions, ingestion keys, and first-run setup (ADR 0008).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything is denied by default: the fallback authorization policy requires a signed-in user, so an endpoint
    /// is only public when it says <c>[AllowAnonymous]</c>.
    /// </para>
    /// <para>
    /// The host still has to add the middleware: authentication, authorization, and rate limiting.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to add the module to.</param>
    /// <param name="configuration">
    /// The application configuration. The module reads its <c>Veyra:Access</c> section.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddVeyraAccess(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AccessOptions>()
            .Bind(configuration.GetSection(VeyraOptionNames.Access))
            .Validate(o => o.LockoutThreshold > 0, "Veyra:Access:LockoutThreshold must be greater than zero.")
            .Validate(o => o.MinimumPasswordLength >= 12, "Veyra:Access:MinimumPasswordLength must be at least 12.")
            .Validate(o => o.SessionIdleTimeout > TimeSpan.Zero, "Veyra:Access:SessionIdleTimeout must be positive.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISchemaMigrationSource, AccessMigrations>();

        services.AddSingleton<IPasswordHasher<AccessUser>, PasswordHasher<AccessUser>>();
        services.AddSingleton<PasswordPolicy>();
        services.AddSingleton<UserStore>();
        services.AddSingleton<UserService>();
        services.AddSingleton<SignInService>();
        services.AddSingleton<SetupService>();
        services.AddSingleton<IngestionKeyStore>();
        services.AddSingleton<IngestionKeyService>();
        services.AddSingleton<IngestionKeyValidator>();
        services.AddSingleton<IIngestionKeyValidator>(provider => provider.GetRequiredService<IngestionKeyValidator>());

        // A fixed application name keeps cookies readable when the image or its path changes between versions.
        services.AddDataProtection().SetApplicationName("Veyra");

        AddControllers(services);
        AddSessions(services, configuration);
        AddPolicies(services);
        AddAntiforgery(services);
        AddSignInRateLimit(services);

        return services;
    }

    // The controllers are public, as MVC requires, but the services they use stay internal to this module. So the
    // module builds its controllers itself. The host must call AddControllersAsServices() for MVC to use these.
    private static void AddControllers(IServiceCollection services)
    {
        services.AddTransient(provider => new SetupController(provider.GetRequiredService<SetupService>()));
        services.AddTransient(provider => new SessionController(
            provider.GetRequiredService<SignInService>(),
            provider.GetRequiredService<UserService>(),
            provider.GetRequiredService<UserStore>(),
            provider.GetRequiredService<IAntiforgery>()));
        services.AddTransient(provider => new UsersController(provider.GetRequiredService<UserService>()));
        services.AddTransient(provider =>
            new IngestionKeysController(provider.GetRequiredService<IngestionKeyService>()));
    }

    private static void AddSessions(IServiceCollection services, IConfiguration configuration)
    {
        var idleTimeout = configuration.GetSection(VeyraOptionNames.Access).GetValue<TimeSpan?>(
            nameof(AccessOptions.SessionIdleTimeout)) ?? new AccessOptions().SessionIdleTimeout;

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = _sessionCookieName;
                options.Cookie.HttpOnly = true;

                // Lax, not Strict: a Strict cookie is not sent on the redirect back from single sign-on (ADR 0008).
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = idleTimeout;
                options.SlidingExpiration = true;

                // An API answers with status codes; redirects to a login page would confuse every client.
                options.Events.OnRedirectToLogin = context =>
                    WriteStatusAsync(context, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = context =>
                    WriteStatusAsync(context, StatusCodes.Status403Forbidden);
                options.Events.OnValidatePrincipal = SessionPrincipal.ValidateAsync;
            });

        // Secure cookies everywhere except local development over plain HTTP.
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IHostEnvironment>((options, environment) =>
                options.Cookie.SecurePolicy = SecurePolicy(environment));
    }

    private static void AddPolicies(IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(VeyraPolicies.Viewer, policy => RequireRole(policy, VeyraRole.Viewer))
            .AddPolicy(VeyraPolicies.Editor, policy => RequireRole(policy, VeyraRole.Editor))
            .AddPolicy(VeyraPolicies.Admin, policy => RequireRole(policy, VeyraRole.Admin));
    }

    private static void AddAntiforgery(IServiceCollection services)
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeaderName;
            options.Cookie.Name = _antiforgeryCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        services.AddOptions<AntiforgeryOptions>()
            .Configure<IHostEnvironment>((options, environment) =>
                options.Cookie.SecurePolicy = SecurePolicy(environment));
    }

    private static void AddSignInRateLimit(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Ten attempts per minute per client address. Lockout protects each account; this protects Veyra from
            // one address trying many accounts.
            options.AddPolicy(SignInRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
        });
    }

    private static void RequireRole(AuthorizationPolicyBuilder policy, VeyraRole minimum) =>
        policy.RequireAuthenticatedUser().RequireAssertion(context => context.User.Role() >= minimum);

    private static CookieSecurePolicy SecurePolicy(IHostEnvironment environment) =>
        environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

    private static Task WriteStatusAsync(
        Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> context,
        int status)
    {
        context.Response.StatusCode = status;

        return Task.CompletedTask;
    }
}
