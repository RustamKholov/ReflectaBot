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
public class WebHookController(IOptions<TelegramBotConfiguration> BotConfig, ILogger<WebHookController> logger) : ControllerBase
{

    [HttpGet("setWebhook")]
    public async Task<string> SetWebHook([FromServices] ITelegramBotClient telegramBot, CancellationToken ct)
    {

        var webhookUrl = BotConfig.Value.WebhookUrl.AbsoluteUri;

        await telegramBot.SetWebhook(webhookUrl, allowedUpdates: [], secretToken: BotConfig.Value.SecretToken, cancellationToken: ct);
        logger.LogInformation("Webhook set to {0}", webhookUrl);
        return $"Webhook set to {webhookUrl}";
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] Update update, [FromServices] ITelegramBotClient botClient, [FromServices] IUpdateHandler updateHandlerService, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(BotConfig.Value.SecretToken))
        {
            var receivedToken = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();
            if (receivedToken != BotConfig.Value.SecretToken)
            {
                return Unauthorized("Invalid secret token");
            }
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