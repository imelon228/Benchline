using System.Globalization;
using System.IO.Compression;
using System.Threading.RateLimiting;
using Benchline.Api;
using Benchline.Application;
using Benchline.Contracts;
using Benchline.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
var demo = builder.Environment.IsEnvironment("Demo");
builder.WebHost.UseStaticWebAssets();
builder.Services.AddSerilog((_, logger) => logger.WriteTo.Console());
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
TimeProvider clock = TimeProvider.System;
if (demo && builder.Configuration["Workshop:DemoClockUtc"] is { } demoTime)
{
    if (!DateTimeOffset.TryParse(demoTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
        throw new InvalidOperationException("Set Workshop:DemoClockUtc to a UTC date and time.");
    clock = new DemoClock(time.ToUniversalTime());
}
builder.Services.AddSingleton(clock);
builder.Services.AddScoped<Workshop>();
builder.Services.AddScoped<Mutation>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IScheduleRepository, EfScheduleRepository>();
builder.Services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();
builder.Services.AddDbContext<WorkshopDb>(options =>
{
    var connection = builder.Configuration.GetConnectionString("Workshop") ?? "Data Source=workshop.db";
    if (builder.Configuration["Workshop:Provider"] == "PostgreSQL")
        options.UseNpgsql(connection);
    else
        options.UseSqlite(connection);
});
builder.Services.AddHealthChecks().AddCheck<WorkshopHealth>("database");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "benchline.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = demo ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
if (builder.Configuration["Workshop:KeyPath"] is { Length: > 0 } path)
    builder.Services.AddDataProtection().SetApplicationName("Benchline").PersistKeysToFileSystem(new DirectoryInfo(path));
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-Workshop-Token";
    options.Cookie.Name = "benchline.request";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = demo ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("sign-in", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = new[] { "text/html", "text/css", "text/javascript", "application/javascript", "application/wasm", "application/octet-stream" };
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

var app = builder.Build();
await DemoData.Initialize(app.Services, demo, app.Configuration);
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();
if (!demo)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    if (demo && HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/api/session")
    {
        context.Response.Headers[DemoSignIn.Header] = app.Configuration["Workshop:DemoPassword"] == DemoSignIn.DefaultPassword
            ? "default"
            : "configured";
    }
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers.CacheControl = "no-store";
    if (context.Request.Path.Value is "/index.html" or "/service-worker.js" or "/service-worker-assets.js")
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            return Task.CompletedTask;
        });
    await next(context);
});
app.UseResponseCompression();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapOpenApi();
app.MapWorkshop();
app.MapHub<WorkshopHub>("/live", options => options.CloseOnAuthenticationExpiration = true).RequireAuthorization();
app.MapFallbackToFile("{*path:nonfile}", "index.html", new StaticFileOptions
{
    OnPrepareResponse = file => file.Context.Response.Headers.CacheControl = "no-cache"
});

await app.RunAsync();

public partial class Program;
