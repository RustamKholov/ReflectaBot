namespace ReflectaBot.Models.Configuration;

public class TelegramBotConfiguration
{
    public required string BotToken { get; set; }
    public required Uri WebhookUrl { get; set; }
    public required string SecretToken { get; set; }
}