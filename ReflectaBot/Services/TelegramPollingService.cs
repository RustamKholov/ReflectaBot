using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace ReflectaBot.Services
{
    public class TelegramPollingService : BackgroundService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TelegramPollingService> _logger;

        public TelegramPollingService(
            ITelegramBotClient botClient,
            IServiceProvider serviceProvider,
            ILogger<TelegramPollingService> logger)
        {
            _botClient = botClient;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Starting Telegram polling for development...");

                await _botClient.DeleteWebhook(dropPendingUpdates: true, cancellationToken: stoppingToken);
                _logger.LogInformation("Cleared existing webhook");

                var receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = [],
                    DropPendingUpdates = true
                };

                _botClient.StartReceiving(
                    updateHandler: new ScopedUpdateHandler(_serviceProvider).HandleUpdateAsync,
                    errorHandler: HandlePollingErrorAsync,
                    receiverOptions: receiverOptions,
                    cancellationToken: stoppingToken
                );

                _logger.LogInformation("Telegram polling started successfully");

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

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
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
    }

    public class ScopedUpdateHandler : IUpdateHandler
    {
        private readonly IServiceProvider _serviceProvider;

        public ScopedUpdateHandler(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<IUpdateHandler>();
            await handler.HandleErrorAsync(botClient, exception, source, cancellationToken);
        }

        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<IUpdateHandler>();
            await handler.HandleUpdateAsync(botClient, update, cancellationToken);
        }
    }
}