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
        _botClient = new TelegramBotClient((Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? configuration["Telegram:BotToken"]) ?? string.Empty);;
        
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] Update update)
    {
        if (update.Message?.Chat.Id != null)
        {
            var chatId = update.Message.Chat.Id;
            var messageText = update.Message.Text;
            await _botClient.SendMessage(
                chatId: chatId,
                text: $"Hello from controller, your text: {messageText}");
        }
        return Ok();
    }
    
}