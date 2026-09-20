using Microsoft.AspNetCore.Http.Features;
using Microsoft.OpenApi;
using MaskFlow.Api.Application;
using MaskFlow.Api.Infrastructure;
using MaskFlow.Api.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 512L * 1024L * 1024L;
});

var corsOrigins = (Environment.GetEnvironmentVariable("MASKFLOW_CORS_ORIGINS")
    ?? "http://localhost:3010,http://127.0.0.1:3010,http://localhost:3000,http://127.0.0.1:3000")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddTransient<SamInternalKeyHandler>();
builder.Services.AddHttpClient("sam", client =>
{
    client.Timeout = TimeSpan.FromMinutes(30);
    client.BaseAddress = new Uri(Environment.GetEnvironmentVariable("SAM_SERVICE_URL")
        ?? builder.Configuration["SamService:Url"]
        ?? "http://localhost:8001");
}).AddHttpMessageHandler<SamInternalKeyHandler>();

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 512L * 1024L * 1024L;
    options.ValueCountLimit = 4096;
    options.MultipartHeadersCountLimit = 64;
});

builder.Services.AddMaskFlowApplication();
builder.Services.AddMaskFlowInfrastructure();
builder.Services.AddControllers(options =>
{
    options.Filters.Add<MaskFlowAuthorizeFilter>();
    options.Filters.Add<AdminApiKeyFilter>();
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MaskFlow API",
        Version = "v1",
        Description = "MaskFlow business API and SAM inference proxy endpoints."
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Enter the JWT/session token as: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

var app = builder.Build();

ProductionSecretsValidator.EnsureConfigured(app.Environment);

app.UseCors();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "MaskFlow API v1");
        options.DocumentTitle = "MaskFlow API Debug";
    });
}

var store = app.Services.GetRequiredService<MaskFlowStore>();
store.Initialize();

app.MapControllers();

app.Run();
