namespace ReflectaBot.Models;

public class TelegramBotConfiguration
{
    public string BotToken { get; set; }
    public Uri WebhookUrl { get; set; }
    public string SecretToken { get; set; }
}