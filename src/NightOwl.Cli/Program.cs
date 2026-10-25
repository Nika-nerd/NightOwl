using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NightOwl.Cli;
using NightOwl.Core;
using NightOwl.Pipeline;
using Spectre.Console;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets<Program>();
builder.Logging.SetMinimumLevel(LogLevel.Critical); // не засоряем вывод; позже подключим Serilog в файл

builder.Services.AddNightOwl(builder.Configuration);

using var host = builder.Build();
var pipeline = host.Services.GetRequiredService<INightOwlPipeline>();

var question = args.Length > 0 ? string.Join(' ', args) : "latest WHO statement on measles";

try
{
    var answer = await AnsiConsole.Status().StartAsync("Запускаю...", async ctx =>
    {
        var progress = new Progress<PipelineProgress>(p => ctx.Status(Markup.Escape(p.Message)));
        return await pipeline.AskAsync(question, progress);
    });

    ConsoleRenderer.Render(answer);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Ошибка: {Markup.Escape(ex.Message)}[/]");
}