using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using ReflectaBot.Models;
using ReflectaBot.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Serilog;
using ReflectaBot.Services.Embedding;
using ReflectaBot.Services.Intent;
using ReflectaBot.Models.Configuration;
using ReflectaBot.Models.Intent;

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
        options.BaseUrl = Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_BASE_URL") ?? "https://api.openai.com/";
    });

    builder.Services.Configure<LlmConfiguration>(options =>
    {
        options.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        options.ModelName = Environment.GetEnvironmentVariable("LLM_MODEL") ?? "gpt-3.5-turbo";
        options.BaseUrl = Environment.GetEnvironmentVariable("OPENAI_COMPLETION_BASE_URL") ?? "https://api.openai.com/";
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

    builder.Services.AddScoped<IntentEmbeddingService>();
    builder.Services.AddScoped<IUpdateHandler, UpdateHandler>();
    builder.Services.AddScoped<IIntentRouter, IntentRouter>();



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

    if (args.Contains("--setup-intents"))
    {
        Log.Information("Setting up intent embeddings...");

        using var scope = app.Services.CreateScope();
        var embeddingService = scope.ServiceProvider.GetRequiredService<IntentEmbeddingService>();

        // Define your bot's intents
        var intentDefinitions = new List<IntentDefinition>
    {
        new()
        {
            Intent = "joke",
            Description = "User wants to hear a funny joke or humorous content",
            Examples = new() { "tell me a joke", "make me laugh", "something funny" }
        },
        new()
        {
            Intent = "dice",
            Description = "User wants to roll dice or get a random number",
            Examples = new() { "roll dice", "random number", "roll a d6" }
        },
        new()
        {
            Intent = "time",
            Description = "User wants to know the current time",
            Examples = new() { "what time is it", "current time", "show me the clock" }
        },
        new()
        {
            Intent = "fact",
            Description = "User wants to hear an interesting fact or trivia",
            Examples = new() { "tell me a fact", "something interesting", "did you know" }
        },
        new()
        {
            Intent = "coin",
            Description = "User wants to flip a coin for heads or tails",
            Examples = new() { "flip a coin", "heads or tails", "coin flip" }
        },
        new()
        {
            Intent = "greeting",
            Description = "User is saying hello or greeting the bot",
            Examples = new() { "hello", "hi", "good morning", "hey there" }
        },
        new()
        {
            Intent = "weather",
            Description = "User is asking about weather conditions",
            Examples = new() { "how's the weather", "is it raining", "weather forecast" }
        }
    };

        var success = await embeddingService.GenerateIntentEmbeddingAsync(intentDefinitions);

        if (success)
        {
            Log.Information("Intent embeddings generated successfully!");
            Log.Information("Estimated cost: $1-3 (one-time setup)");
            Log.Information("Your bot is now ready for fast, accurate intent recognition!");
        }
        else
        {
            Log.Error("Failed to generate intent embeddings");
        }

        return;
    }


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
