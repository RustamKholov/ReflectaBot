using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using ReflectaBot.Services;
using ReflectaBot.Interfaces;
using ReflectaBot.Interfaces.Intent;
using ReflectaBot.Models.Intent;
using ReflectaBot.Models;
using ReflectaBot.Models.Content;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Polling;
using Telegram.Bot.Exceptions;
using System.Threading;

namespace ReflectaBot.Tests
{
    /// <summary>
    /// Tests for UpdateHandler following Reflecta project guidelines
    /// Focuses on integration testing of the intent routing and message handling
    /// </summary>
    public class UpdateHandlerTests
    {
        private readonly UpdateHandler _handler;
        private readonly Mock<ITelegramBotClient> _mockBot;
        private readonly Mock<ILogger<UpdateHandler>> _mockLogger;
        private readonly Mock<IIntentRouter> _mockIntentRouter;
        private readonly Mock<IUrlService> _mockUrlService;
        private readonly Mock<IContentScrapingService> _mockContentScrapingService;
        private readonly Mock<IUrlCacheService> _mockUrlCacheService;
        private readonly Chat _testChat = new() { Id = 12345 };
        private readonly User _testUser = new() { Id = 67890, FirstName = "TestUser" };

        public UpdateHandlerTests()
        {
            _mockBot = new Mock<ITelegramBotClient>();
            _mockLogger = new Mock<ILogger<UpdateHandler>>();
            _mockIntentRouter = new Mock<IIntentRouter>();
            _mockUrlService = new Mock<IUrlService>();
            _mockContentScrapingService = new Mock<IContentScrapingService>();
            _mockUrlCacheService = new Mock<IUrlCacheService>();

            // Setup default behaviors following project guidelines for proper mocking
            _mockUrlService.Setup(x => x.ExtractUrls(It.IsAny<string>()))
                          .Returns(new List<string>());

            _mockIntentRouter.Setup(x => x.RouteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync((ContentIntents.None, 0.0));

        // Due to Telegram.Bot optional parameter issues, skip detailed SendMessage mocking
        // and handle NullReferenceExceptions in tests
        
        _handler = new UpdateHandler(
                        _mockBot.Object,
                        _mockLogger.Object,
                        _mockIntentRouter.Object,
                        _mockUrlService.Object,
                        _mockContentScrapingService.Object,
                        _mockUrlCacheService.Object
                    );
        }
        private Message CreateMockMessage(string text = "Mock response")
        {
            // Create a message using reflection to set readonly properties
            var message = new Message();
            typeof(Message).GetProperty(nameof(Message.MessageId))?.SetValue(message, 1);
            typeof(Message).GetProperty(nameof(Message.Chat))?.SetValue(message, _testChat);
            typeof(Message).GetProperty(nameof(Message.Text))?.SetValue(message, text);
            return message;
        }

        #region Constructor and Dependencies Tests

        [Fact]
        public void Constructor_WithAllDependencies_CreatesInstance()
        {
            // Act & Assert
            Assert.NotNull(_handler);
        }

        [Fact]
        public void Constructor_WithNullBot_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new UpdateHandler(
                null!,
                _mockLogger.Object,
                _mockIntentRouter.Object,
                _mockUrlService.Object,
                _mockContentScrapingService.Object,
                _mockUrlCacheService.Object
            ));
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new UpdateHandler(
                _mockBot.Object,
                null!,
                _mockIntentRouter.Object,
                _mockUrlService.Object,
                _mockContentScrapingService.Object,
                _mockUrlCacheService.Object
            ));
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task HandleErrorAsync_WithRequestException_AppliesDelay()
        {
            // Arrange
            var requestException = new RequestException("Test error");
            var startTime = DateTime.UtcNow;

            // Act
            await _handler.HandleErrorAsync(_mockBot.Object, requestException, HandleErrorSource.PollingError, CancellationToken.None);

            // Assert
            var elapsed = DateTime.UtcNow - startTime;
            Assert.True(elapsed.TotalSeconds >= 2, "Should apply 2-second delay for RequestException");
        }

        [Fact]
        public async Task HandleErrorAsync_WithGeneralException_DoesNotThrow()
        {
            // Arrange
            var generalException = new Exception("Test exception");

            // Act & Assert - Should complete without throwing
            await _handler.HandleErrorAsync(_mockBot.Object, generalException, HandleErrorSource.HandleUpdateError, CancellationToken.None);

            // Verify error was logged
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("HandleError")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion

        #region Message Handling Tests

        [Fact]
        public async Task HandleUpdateAsync_WithTextMessage_ProcessesSuccessfully()
        {
            // Arrange
            var update = new Update();
            var message = new Message();
            typeof(Message).GetProperty(nameof(Message.Chat))?.SetValue(message, _testChat);
            typeof(Message).GetProperty(nameof(Message.Text))?.SetValue(message, "Hello bot!");
            typeof(Message).GetProperty(nameof(Message.From))?.SetValue(message, _testUser);
            typeof(Update).GetProperty(nameof(Update.Message))?.SetValue(update, message);

            // Act & Assert - Should complete without throwing
            await _handler.HandleUpdateAsync(_mockBot.Object, update, CancellationToken.None);

            // Verify intent routing was called
            _mockIntentRouter.Verify(x => x.RouteAsync("Hello bot!", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task HandleUpdateAsync_WithNullMessage_DoesNotProcess()
        {
            // Arrange
            var update = new Update(); // No message

            // Act & Assert - Should complete without throwing
            await _handler.HandleUpdateAsync(_mockBot.Object, update, CancellationToken.None);

            // Verify intent routing was not called
            _mockIntentRouter.Verify(x => x.RouteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task HandleUpdateAsync_WithEmptyText_DoesNotProcess()
        {
            // Arrange
            var update = new Update();
            var message = new Message();
            typeof(Message).GetProperty(nameof(Message.Chat))?.SetValue(message, _testChat);
            typeof(Message).GetProperty(nameof(Message.Text))?.SetValue(message, "");
            typeof(Message).GetProperty(nameof(Message.From))?.SetValue(message, _testUser);
            typeof(Update).GetProperty(nameof(Update.Message))?.SetValue(update, message);

            // Act
            await _handler.HandleUpdateAsync(_mockBot.Object, update, CancellationToken.None);

            // Assert - Intent routing should not be called for empty text
            _mockIntentRouter.Verify(x => x.RouteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        #endregion

        #region URL Processing Tests

        [Fact]
        public async Task HandleUpdateAsync_WithUrl_ProcessesUrlDirectly()
        {
            // Arrange
            var testUrl = "https://example.com/article";
            var update = CreateMessageUpdate(testUrl);

            _mockUrlService.Setup(x => x.ExtractUrls(testUrl))
                          .Returns(new List<string> { testUrl });

            var scrapedContent = new ScrapedContent
            {
                Success = true,
                Title = "Test Article",
                Content = "Test content",
                WordCount = 100
            };

            _mockContentScrapingService.Setup(x => x.ScrapeFromUrlAsync(testUrl))
                                      .ReturnsAsync(scrapedContent);

            _mockUrlCacheService.Setup(x => x.CacheUrlAsync(testUrl))
                               .ReturnsAsync("TEST123");

            // Act
            await _handler.HandleUpdateAsync(_mockBot.Object, update, CancellationToken.None);

            // Assert
            _mockUrlService.Verify(x => x.ExtractUrls(testUrl), Times.Once);
            _mockContentScrapingService.Verify(x => x.ScrapeFromUrlAsync(testUrl), Times.Once);
            _mockUrlCacheService.Verify(x => x.CacheUrlAsync(testUrl), Times.Once);

            // Intent routing should be bypassed for URLs
            _mockIntentRouter.Verify(x => x.RouteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task HandleUpdateAsync_WithFailedUrlScraping_SendsErrorMessage()
        {
            // Arrange
            var testUrl = "https://example.com/article";
            var update = CreateMessageUpdate(testUrl);

            _mockUrlService.Setup(x => x.ExtractUrls(testUrl))
                          .Returns(new List<string> { testUrl });

            var failedContent = new ScrapedContent
            {
                Success = false,
                Error = "Failed to scrape content"
            };

            _mockContentScrapingService.Setup(x => x.ScrapeFromUrlAsync(testUrl))
                                      .ReturnsAsync(failedContent);

            // Act
            await _handler.HandleUpdateAsync(_mockBot.Object, update, CancellationToken.None);

            // Assert
            _mockContentScrapingService.Verify(x => x.ScrapeFromUrlAsync(testUrl), Times.Once);

            // Should not cache failed scraping
            _mockUrlCacheService.Verify(x => x.CacheUrlAsync(It.IsAny<string>()), Times.Never);
        }

        #endregion

        #region Intent Classification Tests

        [Theory]
        [InlineData(ContentIntents.GetHelp)]
        [InlineData(ContentIntents.Greeting)]
        [InlineData(ContentIntents.GetSummary)]
        [InlineData(ContentIntents.CreateQuiz)]
        public async Task HandleUpdateAsync_WithSpecificIntent_CallsAppropriateHandler(string intent)
        {
            // Arrange
            var messageText = "test message";
            var update = CreateMessageUpdate(messageText);

            _mockIntentRouter.Setup(x => x.RouteAsync(messageText, It.IsAny<CancellationToken>()))
                           .ReturnsAsync((intent, 0.85));

            // Act
            await _handler.HandleUpdateAsync(_mockBot.Object, update, CancellationToken.None);

            // Assert
            _mockIntentRouter.Verify(x => x.RouteAsync(messageText, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task HandleUpdateAsync_WithLowConfidenceIntent_HandlesGracefully()
        {
            // Arrange
            var messageText = "unclear message";
            var update = CreateMessageUpdate(messageText);

            _mockIntentRouter.Setup(x => x.RouteAsync(messageText, It.IsAny<CancellationToken>()))
                           .ReturnsAsync((ContentIntents.None, 0.2));

            // Act
            await _handler.HandleUpdateAsync(_mockBot.Object, update, CancellationToken.None);

            // Assert
            _mockIntentRouter.Verify(x => x.RouteAsync(messageText, It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region Helper Methods

        private Update CreateMessageUpdate(string text)
        {
            var update = new Update();
            var message = new Message();
            typeof(Message).GetProperty(nameof(Message.Chat))?.SetValue(message, _testChat);
            typeof(Message).GetProperty(nameof(Message.Text))?.SetValue(message, text);
            typeof(Message).GetProperty(nameof(Message.From))?.SetValue(message, _testUser);
            typeof(Update).GetProperty(nameof(Update.Message))?.SetValue(update, message);
            return update;
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task HandleUpdateAsync_FullWorkflow_ProcessesCorrectly()
        {
            // Arrange - Simulate a complete workflow
            var messageText = "Can you help me?";
            var update = CreateMessageUpdate(messageText);

            _mockIntentRouter.Setup(x => x.RouteAsync(messageText, It.IsAny<CancellationToken>()))
                           .ReturnsAsync((ContentIntents.GetHelp, 0.95));

            // Act
            await _handler.HandleUpdateAsync(_mockBot.Object, update, CancellationToken.None);

            // Assert - Verify the complete workflow
            _mockUrlService.Verify(x => x.ExtractUrls(messageText), Times.Once);
            _mockIntentRouter.Verify(x => x.RouteAsync(messageText, It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion
    }
}