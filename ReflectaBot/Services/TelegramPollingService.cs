using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;

namespace ReflectaBot.Services
{
    public class TelegramPollingService : BackgroundService
    {
        private readonly ITelegramBotClient _telegramBotClient;
        private readonly IUpdateHandler _updateHandler;
        private readonly ILogger<TelegramPollingService> _logger;
        public TelegramPollingService(ITelegramBotClient telegramBotClient, IUpdateHandler updateHandler, ILogger<TelegramPollingService> logger)
        {
            _telegramBotClient = telegramBotClient;
            _updateHandler = updateHandler;
            _logger = logger;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Starting telegram polling for development...");
                await _telegramBotClient.DeleteWebhook(dropPendingUpdates: true, cancellationToken: stoppingToken);
                _logger.LogInformation("Cleared existing webhook");

                var receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = [],
                    DropPendingUpdates = true
                };
                _telegramBotClient.StartReceiving(
                    updateHandler: _updateHandler.HandleUpdateAsync,
                    receiverOptions: receiverOptions,
                    errorHandler: HandlePollingErrorAsync,
                    cancellationToken: stoppingToken
                );
                _logger.LogInformation("Telegram polling started successfully!");

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Polling cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start polling");
                throw;
            }
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient telegramBotClient, Exception exception, CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException =>
                    $"Telegram API Error: [{apiRequestException.ErrorCode}] {apiRequestException.Message}",
                _ => $"Polling Error: {exception.Message}"
            };

            _logger.LogError("{ErrorMessage}", errorMessage);
            return Task.CompletedTask;
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Telegram polling...");
            await base.StopAsync(cancellationToken);
        }
    }
}