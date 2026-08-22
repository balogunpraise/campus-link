using System.Security.Claims;
using System.Text;
using CampusLink.Core.Application.Interfaces;
using CampusLink.Core.Application.Interfaces.Repositories;
using CampusLink.Core.Domain.Entities;
using CampusLink.Infrastructure.Data;
using CampusLink.Infrastructure.Data.Repositories;
using CampusLink.Infrastructure.Data.Seed;
using CampusLink.Infrastructure.Identity;
using CampusLink.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace CampusLink.Infrastructure.DependencyInjection;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddPersistence(configuration, environment);
        services.AddIdentityConfiguration();
        services.AddJwtAuthentication(configuration);
        services.AddRepositories();
        services.AddApplicationServices();

        return services;
    }

    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException(
                                   "Connection string 'DefaultConnection' is not configured. " +
                                   "Set it in appsettings.json or via the " +
                                   "ConnectionStrings__DefaultConnection environment variable.");

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);

                // Transient network blips shouldn't fail a student's request.
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            });

            if (environment.IsDevelopment())
            {
                options.EnableDetailedErrors();
                // Parameter values in logs are fine locally, never in production.
                options.EnableSensitiveDataLogging();
            }
        });

        return services;
    }

    // Named to avoid colliding with Identity's own AddIdentityCore<TUser>().
    public static IServiceCollection AddIdentityConfiguration(this IServiceCollection services)
    {
        services.AddIdentity<AppUser, AppRole>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;

                // Institution verification keys off the email domain, so an
                // address can only belong to one account.
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false;

                // Slows down password guessing against a known username.
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }

    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(JwtSettings.SectionName);
        services.Configure<JwtSettings>(section);

        var settings = section.Get<JwtSettings>() ?? new JwtSettings();

        if (string.IsNullOrWhiteSpace(settings.Key))
            throw new InvalidOperationException(
                "Jwt:Key is not configured. Set it via user-secrets or the Jwt__Key environment variable.");

        // 256-bit minimum for HMAC-SHA256; a shorter key would throw deep inside
        // the token handler at first login instead of here at startup.
        if (Encoding.UTF8.GetByteCount(settings.Key) < 32)
            throw new InvalidOperationException("Jwt:Key must be at least 32 bytes (256 bits) long.");

        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),

                    // Default is 5 minutes, which would keep expired tokens
                    // working well past their stated lifetime.
                    ClockSkew = TimeSpan.FromSeconds(settings.ClockSkewSeconds),

                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        // Lets the client tell "token expired, go refresh" apart
                        // from "token is bad, sign in again".
                        if (context.Exception is SecurityTokenExpiredException)
                            context.Response.Headers["Token-Expired"] = "true";

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IInstitutionService, InstitutionService>();
        services.AddScoped<InstitutionSeeder>();

        return services;
    }

    // Applies pending migrations and loads the world institutions dataset if the
    // table is empty. Call from Program.cs at startup.
    public static async Task<IHost> InitializeDatabaseAsync(this IHost host, bool seed)
    {
        using var scope = host.Services.CreateScope();
        var provider = scope.ServiceProvider;

        await provider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        if (seed)
            await provider.GetRequiredService<InstitutionSeeder>().SeedAsync();

        return host;
    }

    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        // Scoped: repositories wrap the request-scoped DbContext, and the
        // manager hands out all of them over that same context.
        services.AddScoped<IRepositoryManager, RepositoryManager>();

        services.AddScoped<IInstitutionRepository, InstitutionRepository>();
        services.AddScoped<IInstitutionEmailDomainRepository, InstitutionEmailDomainRepository>();
        services.AddScoped<IInstitutionPartnershipRepository, InstitutionPartnershipRepository>();
        services.AddScoped<ISubjectRepository, SubjectRepository>();
        services.AddScoped<ICourseRepository, CourseRepository>();
        services.AddScoped<IResourceRepository, ResourceRepository>();
        services.AddScoped<IResourceShareRequestRepository, ResourceShareRequestRepository>();
        services.AddScoped<IStudyBuddyProfileRepository, StudyBuddyProfileRepository>();
        services.AddScoped<IStudyBuddyMatchRepository, StudyBuddyMatchRepository>();
        services.AddScoped<IStudyGroupRepository, StudyGroupRepository>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        return services;
    }
}
