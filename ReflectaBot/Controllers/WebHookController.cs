using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ReflectaBot.Models;
using ReflectaBot.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace ReflectaBot.Controllers;

[ApiController]
[Route("api/update")]
public class WebHookController(IOptions<TelegramBotConfiguration> BotConfig) : ControllerBase
{

    [HttpGet("setWebhook")]
    public async Task<string> SetWebHook([FromServices] ITelegramBotClient telegramBot, CancellationToken ct)
    {
        var webhookUrl = BotConfig.Value.WebhookUrl.AbsoluteUri;
        await telegramBot.SetWebhook(webhookUrl, allowedUpdates: [], secretToken: BotConfig.Value.SecretToken, cancellationToken: ct);
        return $"Webhook set to {webhookUrl}";
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] Update update, [FromServices] ITelegramBotClient botClient, [FromServices] IUpdateHandler updateHandlerService, CancellationToken ct)
    {
        if (Request.Headers["X-Telegram-Bot-Api-Secret-Token"] != BotConfig.Value.SecretToken)
        {
            return Forbid();
        }
        try
        {
            await updateHandlerService.HandleUpdateAsync(botClient, update, ct);
        }
        catch (Exception ex)
        {
            await updateHandlerService.HandleErrorAsync(botClient, ex, HandleErrorSource.HandleUpdateError, ct);
        }
        return Ok();
    }

}