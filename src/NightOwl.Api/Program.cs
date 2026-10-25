using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using NightOwl.Api;
using NightOwl.Core;
using NightOwl.Pipeline;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNightOwl(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter())); // Tier/Stage уходят строками

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"];
builder.Services.AddCors(o => o.AddPolicy("frontend", p => p
    .WithOrigins(origins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

// Защита квот и бюджета: не больше 5 вопросов в минуту с одного IP
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("ask", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();

app.UseCors("frontend");
app.UseRateLimiter();

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() }
};

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// ---------- Обычный запрос: один JSON ----------
app.MapPost("/api/ask", async (AskRequest request, INightOwlPipeline pipeline, CancellationToken ct) =>
{
    if (!TryValidate(request.Question, out var error))
        return Results.BadRequest(new { error });

    var answer = await pipeline.AskAsync(request.Question!.Trim(), null, ct);
    return Results.Ok(answer.ToDto());
}).RequireRateLimiting("ask");

// ---------- Потоковый запрос: SSE (progress... → result) ----------
app.MapGet("/api/ask/stream", async (
    string? q, INightOwlPipeline pipeline, ILogger<Program> logger, HttpContext http, CancellationToken ct) =>
{
    if (!TryValidate(q, out var error))
    {
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        await http.Response.WriteAsJsonAsync(new { error }, ct);
        return;
    }

    http.Response.Headers.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";
    http.Response.Headers["X-Accel-Buffering"] = "no"; // не буферизовать за nginx

    var channel = Channel.CreateUnbounded<(string Event, object Data)>();

    var worker = Task.Run(async () =>
    {
        try
        {
            var progress = new SyncProgress<PipelineProgress>(p => channel.Writer.TryWrite(("progress", p)));
            var answer = await pipeline.AskAsync(q!.Trim(), progress, ct);
            channel.Writer.TryWrite(("result", answer.ToDto()));
        }
        catch (OperationCanceledException)
        {
            // клиент закрыл соединение
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pipeline failed");
            channel.Writer.TryWrite(("error", new { message = "Внутренняя ошибка сервера" }));
        }
        finally
        {
            channel.Writer.TryComplete();
        }
    }, ct);

    await foreach (var (name, data) in channel.Reader.ReadAllAsync(ct))
    {
        var json = JsonSerializer.Serialize(data, jsonOptions);
        await http.Response.WriteAsync($"event: {name}\ndata: {json}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    await worker;
}).RequireRateLimiting("ask");

app.Run();

static bool TryValidate(string? question, out string error)
{
    var length = question?.Trim().Length ?? 0;
    error = length switch
    {
        < 3 => "Вопрос слишком короткий",
        > 500 => "Вопрос слишком длинный (максимум 500 символов)",
        _ => ""
    };
    return error.Length == 0;
}
