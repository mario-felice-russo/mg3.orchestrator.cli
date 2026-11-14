using Mg3.Orchestrator.Abstractions.Output;
using Mg3.Orchestrator.Output.Json;
using Mg3.Orchestrator.Output.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Mg3.Orchestrator.Output;

public static class OutputServiceCollectionExtensions
{
    public static IServiceCollection AddMg3Output(this IServiceCollection services)
    {
        services.AddSingleton<ITextRenderer, TableRenderer>();
        services.AddSingleton<ITextRenderer, ListRenderer>();
        services.AddSingleton<ITextRenderer, MarkdownRenderer>();
        services.AddSingleton<ITextRenderer, CsvRenderer>();
        services.AddSingleton<IOutputFormatter, TextOutputFormatter>();
        services.AddSingleton<IOutputFormatter, JsonOutputFormatter>();
        return services;
    }
}
