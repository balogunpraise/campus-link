using CampusLink.Api;
using CampusLink.Infrastructure.DependencyInjection;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Postgres, Identity, JWT authentication and the repository layer.
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

builder.Services.AddCampusLinkRateLimiting();
builder.Services.AddControllers();
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

if (app.Environment.IsDevelopment())
{
    // Serve the SwaggerGen document (this is the one carrying the Bearer
    // security scheme) and point the UI at it. /openapi/v1.json belongs to the
    // built-in AddOpenApi() generator, which isn't registered here.
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "CampusLink API v1");
    });
}

app.UseHttpsRedirection();

app.UseRateLimiter();

// Order matters: authentication populates the user, authorization checks it.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Migrate, and load the ~10k world institutions the first time the table is
// empty, so email-domain sign-up works without any manual data entry.
if (app.Configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
    await app.InitializeDatabaseAsync(app.Configuration.GetValue("Database:SeedInstitutions", true));

app.Run();
