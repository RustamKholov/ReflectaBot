using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ReflectaBot.Models;
using Telegram.Bot;

namespace ReflectaBot.Services
{
    public class TelegrammWebhookService : IHostedService
    {
        private readonly ITelegramBotClient _telegramBotClient;
        private readonly ILogger<TelegrammWebhookService> _logger;
        private readonly TelegramBotConfiguration _configuration;
        public TelegrammWebhookService(
            ITelegramBotClient telegramBotClient,
            ILogger<TelegrammWebhookService> logger,
            IOptions<TelegramBotConfiguration> cofigurations)
        {
            _telegramBotClient = telegramBotClient;
            _logger = logger;
            _configuration = cofigurations.Value;
        }
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Setting up webhook for production...");
                _logger.LogInformation("Webhook URL: {WebhookUrl}", _configuration.WebhookUrl);

                await _telegramBotClient.SetWebhook(
                    url: _configuration.WebhookUrl.ToString(),
                    secretToken: _configuration.SecretToken,
                    dropPendingUpdates: true,
                    cancellationToken: cancellationToken
                );
                _logger.LogInformation("Webhook set successfully!");

                var webhookInfo = await _telegramBotClient.GetWebhookInfo(cancellationToken: cancellationToken);
                _logger.LogInformation("Webhook Status: {Url} | Pending {PendingCount}", webhookInfo.Url, webhookInfo.PendingUpdateCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set webhook");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Webhook service stopping...");
            await Task.CompletedTask;
        }
    }
}