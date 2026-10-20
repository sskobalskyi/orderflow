using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

var auth = builder.Configuration.GetSection("Auth");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // Browsers see Keycloak on localhost, containers reach it by service name, so the token
        // issuer and the metadata address differ.
        o.MetadataAddress = auth["MetadataAddress"]!;
        o.RequireHttpsMetadata = false;
        o.MapInboundClaims = false; // keep the raw "sub" claim name
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = auth["Issuer"],
            ValidateAudience = false,
            NameClaimType = "preferred_username",
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("per-user", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirstValue("sub") ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(t => t.AddRequestTransform(ctx =>
    {
        // Services trust the gateway: never forward a client-supplied identity header.
        ctx.ProxyRequest.Headers.Remove("X-Customer-Id");
        if (ctx.HttpContext.User.FindFirstValue("sub") is { } sub)
            ctx.ProxyRequest.Headers.Add("X-Customer-Id", sub);
        return ValueTask.CompletedTask;
    }));

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapReverseProxy();
app.Run();
