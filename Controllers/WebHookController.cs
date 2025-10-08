using Microsoft.AspNetCore.Mvc;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ReflectaBot.Controllers;

[ApiController]
[Route("api/update")]
public class WebHookController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly TelegramBotClient _botClient;

    public WebHookController(IConfiguration configuration)
    {
        _configuration = configuration;
        _botClient = new TelegramBotClient((Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? configuration["Telegram:BotToken"]) ?? string.Empty); ;

    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] Update update)
    {
        if (update.Message?.Chat.Id == null) return Ok();

        var chatId = update.Message.Chat.Id;
        var messageText = update.Message.Text?.ToLower() ?? "";
        var user = update.Message.From?.FirstName ?? "Unknown";

        string response = messageText switch
        {
            "/start" => $"Welcome {user}! 🤖 Try these commands:\n" +
                       "/joke - Get a random joke\n" +
                       "/flip - Flip a coin\n" +
                       "/roll - Roll a dice\n" +
                       "/time - Get current server time\n" +
                       "/fact - Random fun fact",

            "/joke" => GetRandomJoke(),
            "/flip" => Random.Shared.Next(2) == 0 ? "🪙 Heads!" : "🪙 Tails!",
            "/roll" => $"🎲 You rolled: {Random.Shared.Next(1, 7)}",
            "/time" => $"⏰ Server time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} UTC",
            "/fact" => GetRandomFact(),

            var text when text.Contains("hello") || text.Contains("hi") =>
                $"Hello {user}! 👋 Nice to meet you!",

            var text when text.Contains("weather") =>
                "🌤️ I can't check weather yet, but it's always sunny in the server room!",

            var text when text.Contains("deploy") =>
                "🚀 Deployment successful! I'm running the latest version!",

            _ => $"Hello {user}! You said: '{update.Message.Text}'\n" +
                 $"🎲 Random number: {Random.Shared.Next(1, 100)}\n" +
                 $"💬 Message ID: {update.Message.MessageId}\n" +
                 $"📅 Time: {DateTime.Now:HH:mm:ss}"
        };

        await _botClient.SendMessage(chatId: chatId, text: response);
        return Ok();
    }

    private static string GetRandomJoke()
    {
        var jokes = new[]
        {
        "Why do programmers prefer dark mode? Because light attracts bugs! 🐛",
        "How many programmers does it take to change a light bulb? None, that's a hardware problem! 💡",
        "Why did the developer go broke? Because he used up all his cache! 💸",
        "What's a programmer's favorite hangout place? Foo Bar! 🍺",
        "Why do Java developers wear glasses? Because they don't C#! 👓"
    };
        return jokes[Random.Shared.Next(jokes.Length)];
    }

    private static string GetRandomFact()
    {
        var facts = new[]
        {
        "🐙 Octopuses have three hearts and blue blood!",
        "🍯 Honey never spoils - archaeologists have found edible honey in ancient Egyptian tombs!",
        "🌙 A day on Venus is longer than its year!",
        "🐧 Penguins have knees, they're just hidden under their feathers!",
        "🧠 Your brain uses about 20% of your body's total energy!"
    };
        return facts[Random.Shared.Next(facts.Length)];
    }

}