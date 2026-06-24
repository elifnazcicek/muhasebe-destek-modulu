using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using ReceiptOCR.API.Middleware;
using ReceiptOCR.API.Services;
using Serilog;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.EntityFrameworkCore;

// =============================================================================
// Serilog Yapılandırması (Bootstrap Logger)
// =============================================================================
// Uygulama başlamadan önce loglama altyapısını ayarla.
// Bu sayede uygulama başlatma sırasındaki hatalar da loglanır.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/app-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("ReceiptOCR API başlatılıyor...");

    var builder = WebApplication.CreateBuilder(args);

    // Serilog'u host seviyesinde entegre et (ILogger otomatik olarak Serilog'a yönlendirilir)
    builder.Host.UseSerilog();

    // =============================================================================
    // Servis Kayıtları (Dependency Injection)
    // =============================================================================

    // Entity Framework Core (SQL Server)
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    builder.Services.AddDbContext<ReceiptOCR.API.Data.ReceiptDbContext>(options =>
        options.UseSqlServer(connectionString));

    // Görüntü ön işleme servisi (Bizim sorumluluk alanımız)
    builder.Services.AddSingleton<ImagePreprocessingService>();
    
    // Gemini API servisi
    builder.Services.AddHttpClient<GeminiService>();

    // Excel kuyruk servisi ve arka plan işçi servisi
    builder.Services.AddSingleton<IExcelQueueService, ExcelQueueService>();
    builder.Services.AddHostedService<ExcelBackgroundWorker>();

    // CORS: Angular dev server (http://localhost:4200) erişimi için
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins("http://localhost:4200")
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
    });

    // Controller desteği
    builder.Services.AddControllers();

    // =========================================================================
    // JWT Kimlik Doğrulama (Authentication) Yapılandırması
    // =========================================================================
    var jwtKey = builder.Configuration["Jwt:Key"] ?? "super_secret_key_for_receipt_ocr_app_1234567890123456";
    var key = Encoding.ASCII.GetBytes(jwtKey);

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false; // Geliştirme ortamı için false
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ReceiptOCR",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ReceiptOCR",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

    builder.Services.AddAuthorization();

    // =========================================================================
    // Rate Limiting Yapılandırması
    // =========================================================================
    // Sabit pencere politikası: Her IP için dakikada maksimum 30 istek
    builder.Services.AddRateLimiter(options =>
    {
        options.AddFixedWindowLimiter("fixed", limiterOptions =>
        {
            limiterOptions.PermitLimit = 30;
            limiterOptions.Window = TimeSpan.FromSeconds(60);
            limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            limiterOptions.QueueLimit = 0;
        });

        // Rate limit aşıldığında döndürülecek yanıt (HTTP 429)
        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.ContentType = "application/json";

            var response = new
            {
                success = false,
                message = "Çok fazla istek gönderildi. Lütfen bir süre bekleyip tekrar deneyin."
            };

            await context.HttpContext.Response.WriteAsJsonAsync(response, cancellationToken);
        };

        // Global olarak "fixed" politikasını uygula
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            // IP adresine göre rate limiting uygula
            var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(remoteIp, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromSeconds(60),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
        });
    });

    var app = builder.Build();

    // =============================================================================
    // Veritabanı Otomatik Oluşturma ve Seed (Her bilgisayarda sorunsuz çalışması için)
    // =============================================================================
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ReceiptOCR.API.Data.ReceiptDbContext>();
        try
        {
            // Veritabanı yoksa otomatik oluşturur (Tablolar dahil)
            db.Database.EnsureCreated();
            
            // Eğer hiç kullanıcı yoksa varsayılan stajyer ve admin hesaplarını oluştur
            if (!db.Users.Any())
            {
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hashPassword = (string pass) => 
                {
                    var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(pass));
                    var strBuilder = new System.Text.StringBuilder();
                    foreach (var b in bytes) strBuilder.Append(b.ToString("x2"));
                    return strBuilder.ToString();
                };

                db.Users.AddRange(
                    new ReceiptOCR.API.Models.User { Username = "stajyer", PasswordHash = hashPassword("123456"), FullName = "Stajyer Kullanıcı", Role = "User", IsActive = true, CreatedDate = DateTime.Now },
                    new ReceiptOCR.API.Models.User { Username = "admin", PasswordHash = hashPassword("admin123"), FullName = "Sistem Yöneticisi", Role = "Admin", IsActive = true, CreatedDate = DateTime.Now }
                );
                db.SaveChanges();
                Log.Information("Veritabanı oluşturuldu ve varsayılan kullanıcılar eklendi.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Veritabanı oluşturulurken hata meydana geldi. SQL Server bağlantınızı kontrol edin.");
        }
    }

    // =============================================================================
    // Middleware Pipeline
    // =============================================================================

    // 1. Global hata yakalama middleware'i (en üstte olmalı)
    app.UseMiddleware<GlobalExceptionMiddleware>();

    // 2. Rate limiting middleware'i
    app.UseRateLimiter();

    // 3. Serilog HTTP istek loglama
    app.UseSerilogRequestLogging();

    app.UseCors();
    
    // 4. JWT Yetkilendirme Middleware'leri
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    // Sağlık kontrolü endpoint'i
    app.MapGet("/", () => new
    {
        status = "ok",
        message = "Fiş/Fatura OCR Sistemi - .NET 9.0 API çalışıyor.",
        version = "0.1.0"
    });

    app.Run();
}
catch (Exception ex)
{
    // Uygulama başlatma hatalarını logla
    Log.Fatal(ex, "Uygulama başlatılırken kritik hata oluştu!");
}
finally
{
    // Uygulama kapanırken tüm logların yazılmasını garanti et
    Log.CloseAndFlush();
}
