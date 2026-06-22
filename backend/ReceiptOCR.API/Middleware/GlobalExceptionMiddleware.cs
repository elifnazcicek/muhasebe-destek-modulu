using System.Net;
using System.Text.Json;

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

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // Hatayı Serilog üzerinden logla
            _logger.LogError(ex, "İşlenmeyen hata oluştu: {ErrorMessage}", ex.Message);

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
