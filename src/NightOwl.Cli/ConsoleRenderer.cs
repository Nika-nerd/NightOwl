using NightOwl.Core;
using Spectre.Console;

namespace NightOwl.Cli;

public static class ConsoleRenderer
{
    public static void Render(NightOwlAnswer a)
    {  AnsiConsole.Write(new Panel(new Text(a.Summary))
            .Header($"NightOwl · уверенность {(int)Math.Round(a.OverallConfidence * 100)}%{(a.FromCache ? " · из кэша" : "")}")
            .Expand());
        AnsiConsole.MarkupLine($"[bold]Вопрос:[/] {Markup.Escape(a.Question)}");
        AnsiConsole.MarkupLine($"[bold]Язык:[/] {a.Plan.QuestionLanguage}  [bold]Нужна свежесть:[/] {a.Plan.NeedsFreshness}");
        foreach (var q in a.Plan.Queries)
            AnsiConsole.MarkupLine($"  • {Markup.Escape(q)}");

        foreach (var w in a.Warnings)
            AnsiConsole.MarkupLine($"[yellow]⚠ {Markup.Escape(w)}[/]");

        if (a.Claims.Count == 0)
        {
            AnsiConsole.MarkupLine($"\n[red]{Markup.Escape(a.Summary)}[/]");
            return;
        }

        // Нумерация источников = порядок в a.Sources
        var numberByUrl = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < a.Sources.Count; i++)
            numberByUrl[a.Sources[i].Origin.Url] = i + 1;

        string Refs(IEnumerable<Claim> list)
            => string.Join(", ", list
                .Select(c => numberByUrl.TryGetValue(c.SourceUrl, out var n) ? $"[{n}]" : "")
                .Where(s => s.Length > 0)
                .Distinct());

        var table = new Table().AddColumns("%", "Факт", "За", "Против");
        foreach (var v in a.Claims.Take(8))
        {
            table.AddRow(
                PercentMarkup(v.Confidence),
                Markup.Escape(v.Text),
                Markup.Escape(Refs(v.Supporting)),
                Markup.Escape(Refs(v.Contradicting)));
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(new Text(a.Summary))   // Text не разбирает [1][2] как разметку
            .Header($"NightOwl · уверенность {(int)Math.Round(a.OverallConfidence * 100)}%")
            .Expand());

        AnsiConsole.MarkupLine("\n[bold]Источники:[/]");
        for (var i = 0; i < a.Sources.Count; i++)
        {
            var s = a.Sources[i];
            AnsiConsole.MarkupLine(
                $"[[{i + 1}]] {s.Tier} · {Markup.Escape(s.Domain)} · {s.PublishedAt?.ToString("yyyy-MM-dd") ?? "н/д"} · {Markup.Escape(s.Origin.Url)}");
        }

        AnsiConsole.MarkupLine("\n[grey]Процент — эвристическая оценка по уровню источников, согласованности, свежести и разнообразию. Это не гарантия истинности.[/]");
    }

    private static string PercentMarkup(double confidence)
    {
        var pct = (int)Math.Round(confidence * 100);
        var color = pct >= 75 ? "green" : pct >= 50 ? "yellow" : "red";
        return $"[{color}]{pct}%[/]";
    }
}