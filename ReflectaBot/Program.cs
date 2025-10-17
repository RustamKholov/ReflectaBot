using DotNetEnv;
using Microsoft.OpenApi.Models;
using ReflectaBot.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Serilog;
using ReflectaBot.Services.Embedding;
using ReflectaBot.Services.Intent;
using ReflectaBot.Models.Configuration;
using ReflectaBot.Interfaces.Intent;
using ReflectaBot.Interfaces;


var envPath = Path.Combine(Directory.GetCurrentDirectory(), "..", ".env");
if (File.Exists(envPath))
{
    Env.Load(envPath);
}
else
{
    Env.Load();
}
var elasticUri = "http://localhost:9200";
var elasticPassword = Environment.GetEnvironmentVariable("ELASTIC_PASSWORD");
Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs/reflectabot-.txt",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7)
                .WriteTo.Elasticsearch(new Serilog.Sinks.Elasticsearch.ElasticsearchSinkOptions(new Uri(elasticUri))
                {
                    AutoRegisterTemplate = true,
                    IndexFormat = "reflectabot-logs-{0:yyyy.MM.dd}",
                    ModifyConnectionSettings = x => x.BasicAuthentication("elastic", elasticPassword)
                }).CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog();
try
{
    Log.Information("Starting up ReflectaBot.....");
    // Add services to the container.
    // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi

    builder.Services.Configure<TelegramBotConfiguration>(options =>
    {
        options.BotToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ??
                            builder.Configuration["Telegram:BotToken"] ??
                            string.Empty;

        options.WebhookUrl = new Uri(
                                Environment.GetEnvironmentVariable("WEBHOOK_URL") ??
                                builder.Configuration["Telegram:WebhookUrl"] ??
                                "https://kapasitet.ignorelist.com/api/update");

        options.SecretToken = Environment.GetEnvironmentVariable("TELEGRAM_SECRET_TOKEN") ??
                                builder.Configuration["Telegram:SecretToken"] ??
                                string.Empty;
    });

    builder.Services.Configure<IntentConfiguration>(options =>
    {
        options.ExamplesJsonPath = Path.Combine(AppContext.BaseDirectory, "Data", "intent_examples.json");
    });

    builder.Services.Configure<EmbeddingConfiguration>(options =>
    {
        options.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        options.ModelName = Environment.GetEnvironmentVariable("EMBEDDING_MODEL") ?? "text-embedding-3-small";
        options.BaseUrl = Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_BASE_URL") ?? "https://api.openai.com/v1/embeddings";
    });

    builder.Services.Configure<LlmConfiguration>(options =>
    {
        options.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        options.ModelName = Environment.GetEnvironmentVariable("LLM_MODEL") ?? "gpt-3.5-turbo";
        options.BaseUrl = Environment.GetEnvironmentVariable("OPENAI_COMPLETION_BASE_URL") ?? "https://api.openai.com/v1/chat/completions";
    });

    builder.Services.AddHttpClient("tgwebhook")
        .RemoveAllLoggers()
        .AddTypedClient<ITelegramBotClient>(
        httpClient => new TelegramBotClient(
            Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ??
            builder.Configuration["Telegram:BotToken"] ??
            string.Empty, httpClient));

    builder.Services.AddHttpClient<EmbeddingHelper>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(1);
        });

    builder.Services.AddHttpClient<ILlmClassifier, LlmClassifier>(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(2);
    });

    builder.Services.AddHttpClient<IUrlService, UrlService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
    });

    builder.Services.AddHttpClient<IContentScrapingService, ContentScrapingService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });

    builder.Services.AddHttpClient<IAIContentService, AIContentService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });

    builder.Services.AddScoped<IntentEmbeddingService>();
    builder.Services.AddScoped<IUpdateHandler, UpdateHandler>();
    builder.Services.AddScoped<IIntentRouter, IntentRouter>();
    builder.Services.AddScoped<IContentProcessor, UniversalContentProcessor>();
    builder.Services.AddScoped<ITextAnalysisService, TextAnalysisService>();
    builder.Services.AddScoped<IDocumentProcessor, DocumentProcessor>();
    builder.Services.AddSingleton<IContentCacheService, ContentCacheService>();

    builder.Services.AddSingleton<IUrlCacheService, UrlCacheService>();



    if (builder.Environment.IsDevelopment())
    {
        Log.Information("Development mode: Using polling");
        builder.Services.AddHostedService<TelegramPollingService>();
    }
    else
    {
        Log.Information("Production mode: Using webhooks");
        builder.Services.AddHostedService<TelegramWebhookService>();
        builder.Services.AddControllers();
    }

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "ReflectaBot", Version = "v1" });
    });



    var app = builder.Build();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    else
    {
        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseAuthorization();
        app.MapControllers();
    }

    app.Run();

}
catch (Exception ex)
{
    Log.Fatal(ex, "ReflectaBot terminated unexpectedly!");
}
finally
{
    Log.CloseAndFlush();
}
