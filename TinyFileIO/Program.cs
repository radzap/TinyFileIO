using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using TinyFileIO.Components;
using TinyFileIO.Data;
using TinyFileIO.Endpoints;
using TinyFileIO.Middleware;
using TinyFileIO.Services;
using TinyFileIO.Services.BackgroundJobs;

namespace TinyFileIO
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // Cookie authentication
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(o =>
                {
                    o.LoginPath    = "/_tfio/login";
                    o.LogoutPath   = "/_tfio/auth/logout";
                    o.AccessDeniedPath = "/_tfio/login";
                    o.Cookie.HttpOnly  = true;
                    o.Cookie.SameSite  = SameSiteMode.Strict;
                    o.SlidingExpiration = true;
                    o.ExpireTimeSpan    = TimeSpan.FromHours(8);
                });
            builder.Services.AddAuthorization();
            builder.Services.AddCascadingAuthenticationState();

            // S3-compatible API
            builder.Services.AddControllers();
            builder.Services.AddSingleton<IS3XmlSerializer, S3XmlSerializer>();

            // File system storage providers
            builder.Services.AddSingleton<IS3BucketService, FileSystemBucketService>();
            builder.Services.AddSingleton<IS3ObjectService, FileSystemObjectService>();
            builder.Services.AddSingleton<IS3MultipartService, FileSystemMultipartService>();
            builder.Services.AddHttpClient();

            // Background jobs
            builder.Services.AddScoped<IBackgroundJobQueue, BackgroundJobQueue>();
            builder.Services.AddScoped<IBackgroundJobHistoryService, BackgroundJobHistoryService>();
            builder.Services.AddScoped<IBackgroundJob, DownloadJob>();
            builder.Services.AddHostedService<BackgroundJobWorker>();

            // S3 credential store — users ARE the credentials (username=AccessKeyId, password=S3Secret)
            // No separate credential service needed.

            // Pre-signed URL store (in-memory)
            builder.Services.AddSingleton<IPresignedUrlService, PresignedUrlService>();

            // Authorization provider
            builder.Services.AddScoped<IAuthorizationProvider, AuthorizationProvider>();
            builder.Services.AddScoped<IUserManagementService, UserManagementService>();

            // Database
            var location = builder.Configuration["StoreLocation"] ?? string.Empty;
            var connectionString = (builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty).Replace("{StoreLocation}", location);
            builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
            builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString), ServiceLifetime.Scoped);

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/_tfio/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            //app.UseHttpsRedirection();

            // Virtual-hosted style: {bucket}.{host} → /{bucket}/key path rewrite
            app.UseMiddleware<VirtualHostedMiddleware>();

            // Attach x-amz-request-id / x-amz-id-2 to every response
            app.UseMiddleware<S3RequestIdMiddleware>();

            // S3 API authentication (SigV4, SigV2, pre-signed token)
            app.UseMiddleware<S3AuthMiddleware>();

            app.UseAuthentication();
            app.UseAuthorization();
            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            // Auth endpoints (plain form POST — no Blazor involved)
            app.MapTfioAuthEndpoints();

            // S3 credential + pre-signed URL management (UI API)
            app.MapS3ManagementEndpoints();

            // UI-only ZIP download endpoint
            app.MapDownloadZipEndpoint();

            // S3 API controllers — must come after Blazor mapping so Blazor routes win
            app.MapControllers();

            if (args.Contains("--migrate"))
            {
                MigrateToLatest(app);
                Console.WriteLine("Migration completed successfully");
            }
            else
            {
                try
                {
                    MigrateToLatest(app);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Migration failed: {ex.Message}");
                    Console.WriteLine("Use --migrate to run the migration manually.");
                }

                app.Run();
            }
        }

        private static void MigrateToLatest(WebApplication app)
        {
            using (var scope = app.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                dbContext.Database.Migrate();
            }
        }
    }
}
