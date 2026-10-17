using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Moser.RagAi.Web.Components;
using Npgsql;
using Pgvector.Npgsql;

namespace Moser.RagAi.Web;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
        });

        var connectionString = builder.Configuration.GetConnectionString("Desk");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set ConnectionStrings__Desk to the Postgres connection string.");

        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 12 * 1024 * 1024);
        builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 12 * 1024 * 1024);
        builder.Services.Configure<HubOptions>(options => options.MaximumReceiveMessageSize = 12 * 1024 * 1024);
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.Configure<DeskOptions>(builder.Configuration.GetSection("Desk"));
        builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));

        var modelDirectory = ResolveModelDirectory(builder);
        builder.Services.AddSingleton(new EmbeddingLocation(modelDirectory));

        await EnsureVectorExtensionAsync(connectionString);
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        builder.Services.AddSingleton(dataSourceBuilder.Build());
        builder.Services.AddSingleton<IDocumentIndex, PgDocumentIndex>();
        builder.Services.AddSingleton<IDocumentTextExtractor, FileTextExtractor>();
        builder.Services.AddSingleton<IEmbeddingGenerator>(services =>
        {
            var directory = services.GetRequiredService<EmbeddingLocation>().Path;
            return new MiniLmEmbedder(
                Path.Combine(directory, EmbeddingFiles.ModelFileName),
                Path.Combine(directory, EmbeddingFiles.VocabFileName));
        });
        builder.Services.AddSingleton(services =>
        {
            var options = services.GetRequiredService<IOptions<DeskOptions>>().Value;
            return new DeskLimits(
                options.MaxUploadBytes > 0 ? options.MaxUploadBytes : DeskLimits.DefaultMaxUploadBytes,
                options.MinimumSimilarity,
                options.TopK > 0 ? options.TopK : 4);
        });
        builder.Services.AddHttpClient("gemini", (services, client) =>
        {
            var endpoint = services.GetRequiredService<IOptions<GeminiOptions>>().Value.Endpoint.Trim();
            if (!endpoint.EndsWith('/'))
                endpoint += "/";
            client.BaseAddress = new Uri(endpoint);
            client.Timeout = TimeSpan.FromSeconds(90);
        });
        builder.Services.AddSingleton<IAnswerGenerator>(services =>
        {
            var http = services.GetRequiredService<IHttpClientFactory>().CreateClient("gemini");
            var model = services.GetRequiredService<IOptions<GeminiOptions>>().Value.Model;
            var key = services.GetRequiredService<IConfiguration>()["GEMINI_API_KEY"];
            return new GeminiAnswerGenerator(http, model, key, services.GetRequiredService<ILogger<GeminiAnswerGenerator>>());
        });
        builder.Services.AddSingleton<DeskService>();
        builder.Services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres")
            .AddCheck<EmbeddingFilesHealthCheck>("embeddings");

        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/sign-in";
                options.Cookie.Name = "desk.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
            });
        builder.Services.AddAuthorization();
        builder.Services.AddCascadingAuthenticationState();

        var app = builder.Build();
        var users = app.Services.GetRequiredService<IOptions<DeskOptions>>().Value.Users;
        if (!users.Exists(user => user.IsUsable))
            app.Logger.LogWarning("No desk users are configured. Set Desk__Users__0__UserName and Desk__Users__0__Password.");

        using (var download = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
        {
            download.DefaultRequestHeaders.UserAgent.ParseAdd("Moser.RagAi");
            await EmbeddingFiles.EnsureAsync(modelDirectory, download, CancellationToken.None);
        }

        _ = app.Services.GetRequiredService<IEmbeddingGenerator>();
        await InitializeDatabaseAsync(app.Services, app.Logger, CancellationToken.None);

        if (!app.Environment.IsDevelopment())
            app.UseExceptionHandler("/Error", createScopeForErrors: true);

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            ResponseWriter = WriteHealthAsync
        });
        app.MapPost("/account/sign-in", SignInAsync);
        app.MapPost("/account/sign-out", SignOutAsync);
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        app.Run();
    }

    private static string ResolveModelDirectory(WebApplicationBuilder builder)
    {
        var configured = builder.Configuration["Embeddings:Directory"];
        if (string.IsNullOrWhiteSpace(configured))
            return Path.Combine(builder.Environment.ContentRootPath, "models");
        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(builder.Environment.ContentRootPath, configured);
    }

    private static async Task EnsureVectorExtensionAsync(string connectionString)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector", connection);
                await command.ExecuteNonQueryAsync();
                return;
            }
            catch (Exception) when (attempt < 8)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    private static async Task InitializeDatabaseAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        var index = services.GetRequiredService<IDocumentIndex>();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await index.InitializeAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < 8)
            {
                logger.LogWarning(ex, "Database is not ready, attempt {Attempt}.", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    private static Task WriteHealthAsync(HttpContext context, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new { name = entry.Key, status = entry.Value.Status.ToString() })
        });
    }

    private static async Task<IResult> SignInAsync(HttpContext http, IAntiforgery antiforgery, IOptions<DeskOptions> options)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest();
        }

        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        var name = form["userName"].ToString().Trim();
        var password = form["password"].ToString();
        var returnUrl = ReturnPath.Normalize(form["returnUrl"].ToString());
        var user = options.Value.Users.FirstOrDefault(candidate =>
            candidate.IsUsable && string.Equals(candidate.UserName, name, StringComparison.OrdinalIgnoreCase));
        if (user is null || !PasswordCompare.Matches(user.Password, password))
        {
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SignIn").LogWarning("Sign-in failed.");
            return Results.Redirect($"/sign-in?error=1&ReturnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, user.UserName)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.Redirect(returnUrl);
    }

    private static async Task<IResult> SignOutAsync(HttpContext http, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest();
        }

        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/");
    }
}
