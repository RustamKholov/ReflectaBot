using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using ReflectaBot.Models;
using ReflectaBot.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Serilog;

var envPath = Path.Combine(Directory.GetCurrentDirectory(), "..", ".env");
if (File.Exists(envPath))
{
    Env.Load(envPath);
}
else
{
    Env.Load();
}
var elasticUri = "http://elasticsearch:9200";
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
await TestElasticsearchConnection();
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

    builder.Services.AddHttpClient("tgwebhook").RemoveAllLoggers().AddTypedClient<ITelegramBotClient>(
        httpClient => new TelegramBotClient((Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")) ?? builder.Configuration["Telegram:BotToken"] ?? string.Empty, httpClient));

    builder.Services.AddSingleton<IUpdateHandler, UpdateHandler>();


    builder.Services.AddControllers();
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
    app.UseHttpsRedirection();
    app.UseRouting();
    app.UseAuthorization();
    app.MapControllers();

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

static async Task TestElasticsearchConnection()
{
    try
    {
        var elasticPassword = Environment.GetEnvironmentVariable("ELASTIC_PASSWORD");
        var elasticUri = "http://localhost:9200";

        Console.WriteLine($"🔍 Testing connection with:");
        Console.WriteLine($"   URI: {elasticUri}");
        Console.WriteLine($"   Password: '{elasticPassword}' (length: {elasticPassword?.Length ?? 0})");

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10);

        if (!string.IsNullOrEmpty(elasticPassword))
        {
            var credentials = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"elastic:{elasticPassword}"));
            Console.WriteLine($"🔍 Base64 credentials: {credentials}");
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        }
        else
        {
            Console.WriteLine("❌ No password provided - testing without authentication");
        }

        var response = await httpClient.GetAsync(elasticUri);

        Console.WriteLine($"🔍 Response status: {response.StatusCode}");
        Console.WriteLine($"🔍 Response reason: {response.ReasonPhrase}");

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"✅ Elasticsearch connection test successful");
            Console.WriteLine($"📄 Response: {content.Substring(0, Math.Min(200, content.Length))}...");

            // Test index search
            var indexResponse = await httpClient.GetAsync($"{elasticUri}/reflectabot-logs-*/_search?size=0");
            Console.WriteLine($"🔍 Index search test: {indexResponse.StatusCode}");
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"❌ Elasticsearch connection test failed: {response.StatusCode} - {response.ReasonPhrase}");
            Console.WriteLine($"📄 Error response: {errorContent}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Elasticsearch connection test error: {ex.Message}");
        Console.WriteLine($"📄 Full exception: {ex}");
    }
}