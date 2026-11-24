using System.Text.Json.Serialization;
using CampusLink.Api;
using CampusLink.Api.Hubs;
using CampusLink.Core.Application.Interfaces;
using CampusLink.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Postgres, Identity, JWT authentication and the repository layer.
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

builder.Services.AddCampusLinkRateLimiting();

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        // SignalR has its own serializer — AddJsonOptions above only covers MVC.
        // Without this the hub would demand enum ordinals while REST accepts
        // names, so the same "type": "Video" would work on one door and fail
        // silently on the other.
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Lets the REST chat endpoints push to connected sockets, so both doors feed
// the same real-time stream.
builder.Services.AddScoped<IChatNotifier, SignalRChatNotifier>();
builder.Services.AddScoped<ICallNotifier, SignalRCallNotifier>();

// Pins what Clients.User(...) resolves to, so call signaling can't be misrouted
// by a future change to the claim set.
builder.Services.AddSingleton<IUserIdProvider, UserIdProvider>();

// Sweeps calls nobody answered — a ring timeout can't be the caller's job.
builder.Services.AddHostedService<RingTimeoutService>();

// The chat hub is the one thing the frontend's browser calls directly instead
// of through its own same-origin proxy (a WebSocket can't ride an httpOnly
// cookie through a Next.js Route Handler), so it's the one endpoint that needs
// an actual CORS policy. Everything else the frontend calls server-to-server,
// where CORS never applies.
const string FrontendCorsPolicy = "Frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000"];

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

// Browsers can't set an Authorization header on a WebSocket handshake, so
// SignalR clients pass the token as ?access_token=. Accept it for hub paths
// only — never for the regular API surface, where it would end up in logs.
builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    var existing = options.Events?.OnMessageReceived;

    options.Events ??= new JwtBearerEvents();
    options.Events.OnMessageReceived = async context =>
    {
        if (existing is not null)
            await existing(context);

        var token = context.Request.Query["access_token"];

        if (!string.IsNullOrEmpty(token) &&
            context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
            context.Token = token;
    };
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Accept and emit enum names ("Global") rather than raw ordinals.
        // Responses already render enums as strings, so without this a client
        // couldn't round-trip a value it just received — and an API where
        // matchScope is 2 is a worse API anyway. Numbers are still accepted on
        // input, so existing callers don't break.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // "Authorize" button in Swagger UI, so protected endpoints are testable.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token only — Swagger adds the \"Bearer \" prefix."
    });

    // Swashbuckle 10 hands the document to the factory so the reference can be
    // resolved against it.
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() }
    });
});

var app = builder.Build();

// if (app.Environment.IsDevelopment())
// {
    // Serve the SwaggerGen document (this is the one carrying the Bearer
    // security scheme) and point the UI at it. /openapi/v1.json belongs to the
    // built-in AddOpenApi() generator, which isn't registered here.
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "CampusLink API v1");
    });
//}

app.UseHttpsRedirection();

// Must run before authentication/authorization: a preflight OPTIONS request
// carries no auth, so if UseCors ran after UseAuthorization the preflight
// itself would be rejected.
app.UseCors(FrontendCorsPolicy);

app.UseRateLimiter();

// Order matters: authentication populates the user, authorization checks it.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat").RequireCors(FrontendCorsPolicy);
app.MapHub<CallHub>("/hubs/calls").RequireCors(FrontendCorsPolicy);

// Migrate on startup in every environment, production included, so a deploy
// carries its own schema change rather than needing a separate manual step.
// Set Database__MigrateOnStartup=false to take over with `dotnet ef database
// update` in a release step instead.
//
// Seeding loads the ~10k world institutions the first time the table is empty,
// so email-domain sign-up works without manual data entry. It's a one-off:
// subsequent boots find rows and skip.
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
    await app.InitializeDatabaseAsync(app.Configuration.GetValue("Database:SeedInstitutions", true));

app.Run();
