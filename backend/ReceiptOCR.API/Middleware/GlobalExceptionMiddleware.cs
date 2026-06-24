using System.Net;
using System.Text.Json;
using ReceiptOCR.API.Data;
using ReceiptOCR.API.Models;

namespace ReceiptOCR.API.Middleware;

/// <summary>
/// Tüm yakalanmamış hataları yakalayan ve istemciye temiz bir JSON yanıt döndüren global hata yönetimi middleware'i.
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ReceiptDbContext dbContext)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // Hatayı Serilog üzerinden logla
            _logger.LogError(ex, "İşlenmeyen hata oluştu: {ErrorMessage}", ex.Message);

            // Veritabanına hata logunu kaydet
            try
            {
                dbContext.ChangeTracker.Clear();
                var username = context.User?.Identity?.Name;
                var actionType = $"{context.Request.Method} {context.Request.Path}";
                var errorLog = new ErrorLog
                {
                    Timestamp = DateTime.Now,
                    Username = username,
                    ActionType = actionType,
                    ErrorMessage = ex.Message,
                    StackTrace = ex.StackTrace
                };
                dbContext.ErrorLogs.Add(errorLog);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Hata veritabanına kaydedilirken hata oluştu");
            }

            // İstemciye temiz JSON yanıt döndür
            await HandleExceptionAsync(context, ex);
        }
    }

    /// <summary>
    /// Hata durumunda istemciye standart JSON hata yanıtı oluşturur.
    /// </summary>
    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var response = new
        {
            success = false,
            error = exception.Message,
            message = "Sunucu hatası."
        };

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(response, jsonOptions);
        await context.Response.WriteAsync(json);
    }
}
