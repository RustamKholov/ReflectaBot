using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using ReflectaBot.Models;
using ReflectaBot.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;

var builder = WebApplication.CreateBuilder(args);

var envPath = Path.Combine(Directory.GetCurrentDirectory(), "..", ".env");
if (File.Exists(envPath))
{
    Env.Load(envPath);
}
else
{
    Env.Load();
}
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

