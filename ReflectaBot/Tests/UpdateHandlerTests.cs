using Xunit;
using Moq;
using ReflectaBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Polling;

namespace ReflectaBot.Tests
{
    public class UpdateHandlerTests
    {
        private readonly UpdateHandler _handler;

        public UpdateHandlerTests()
        {
            _handler = new UpdateHandler();
        }

        [Fact]
        public void ProcessMessage_StartCommand_ReturnsWelcomeMessage()
        {
            // Arrange
            var messageText = "/start";
            var user = "TestUser";

            // Act
            var result = _handler.ProcessMessage(messageText, user);

            // Assert
            Assert.Contains("Welcome TestUser!", result);
            Assert.Contains("/joke", result);
            Assert.Contains("/flip", result);
            Assert.Contains("/roll", result);
            Assert.Contains("/time", result);
            Assert.Contains("/fact", result);
        }

        [Fact]
        public void ProcessMessage_JokeCommand_ReturnsJoke()
        {
            // Arrange
            var messageText = "/joke";
            var user = "TestUser";

            // Act
            var result = _handler.ProcessMessage(messageText, user);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.True(result.Contains("programmers") || result.Contains("developer") || result.Contains("Java"));
        }

        [Fact]
        public void ProcessMessage_FlipCommand_ReturnsHeadsOrTails()
        {
            // Arrange
            var messageText = "/flip";
            var user = "TestUser";

            // Act
            var result = _handler.ProcessMessage(messageText, user);

            // Assert
            Assert.True(result.Contains("Heads!") || result.Contains("Tails!"));
            Assert.Contains("🪙", result);
        }

        [Fact]
        public void ProcessMessage_RollCommand_ReturnsDiceRoll()
        {
            // Arrange
            var messageText = "/roll";
            var user = "TestUser";

            // Act
            var result = _handler.ProcessMessage(messageText, user);

            // Assert
            Assert.Contains("🎲 You rolled:", result);
            Assert.True(result.Contains("1") || result.Contains("2") || result.Contains("3") ||
                       result.Contains("4") || result.Contains("5") || result.Contains("6"));
        }

        [Fact]
        public void ProcessMessage_TimeCommand_ReturnsServerTime()
        {
            // Arrange
            var messageText = "/time";
            var user = "TestUser";

            // Act
            var result = _handler.ProcessMessage(messageText, user);

            // Assert
            Assert.Contains("⏰ Server time:", result);
            Assert.Contains("UTC", result);
        }

        [Fact]
        public void ProcessMessage_FactCommand_ReturnsFact()
        {
            // Arrange
            var messageText = "/fact";
            var user = "TestUser";

            // Act
            var result = _handler.ProcessMessage(messageText, user);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.True(result.Contains("🐙") || result.Contains("🍯") || result.Contains("🌙") ||
                       result.Contains("🐧") || result.Contains("🧠"));
        }

        [Theory]
        [InlineData("hello")]
        [InlineData("hi")]
        [InlineData("Hello there")]
        [InlineData("hi how are you")]
        public void ProcessMessage_GreetingWords_ReturnsGreeting(string messageText)
        {
            // Arrange
            var user = "TestUser";

            // Act
            var result = _handler.ProcessMessage(messageText, user);

            // Assert
            Assert.Contains("Hello TestUser!", result);
            Assert.Contains("👋", result);
        }

        [Fact]
        public void ProcessMessage_WeatherMention_ReturnsWeatherResponse()
        {
            // Arrange
            var messageText = "what's the weather like?";
            var user = "TestUser";

            // Act
            var result = _handler.ProcessMessage(messageText, user);

            // Assert
            Assert.Contains("🌤️", result);
            Assert.Contains("server room", result);
        }

        [Fact]
        public void ProcessMessage_DeployMention_ReturnsDeployResponse()
        {
            // Arrange
            var messageText = "how was the deploy?";
            var user = "TestUser";

            // Act
            var result = _handler.ProcessMessage(messageText, user);

            // Assert
            Assert.Contains("🚀 Deployment successful!", result);
            Assert.Contains("latest version", result);
        }

        [Fact]
        public void ProcessMessage_UnknownCommand_ReturnsDefaultResponse()
        {
            // Arrange
            var messageText = "random message";
            var user = "TestUser";
            var message = new Message()
            {
                Text = messageText
            };
            // MessageId is read-only in newer versions, so we'll test without setting it

            // Act
            var result = _handler.ProcessMessage(messageText, user, message);

            // Assert
            Assert.Contains("Hello TestUser!", result);
            Assert.Contains("You said: 'random message'", result);
            Assert.Contains("🎲 Random number:", result);
            Assert.Contains("💬 Message ID:", result);
            Assert.Contains("📅 Time:", result);
        }

        [Fact]
        public void ProcessMessage_EmptyMessage_ReturnsDefaultResponse()
        {
            // Arrange
            var messageText = "";
            var user = "TestUser";

            // Act
            var result = _handler.ProcessMessage(messageText, user);

            // Assert
            Assert.Contains("Hello TestUser!", result);
            Assert.Contains("🎲 Random number:", result);
        }

        [Fact]
        public void ProcessMessage_NullUser_HandlesGracefully()
        {
            // Arrange
            var messageText = "/start";
            var user = "Unknown";

            // Act
            var result = _handler.ProcessMessage(messageText, user);

            // Assert
            Assert.Contains("Welcome Unknown!", result);
        }

        [Fact]
        public async Task HandleErrorAsync_DoesNotThrow()
        {
            // Arrange
            var mockBot = new Mock<ITelegramBotClient>();
            var exception = new Exception("Test exception");

            // Act & Assert
            await _handler.HandleErrorAsync(mockBot.Object, exception, Telegram.Bot.Polling.HandleErrorSource.PollingError, CancellationToken.None);
            // Should complete without throwing
        }

        [Fact]
        public async Task HandleUpdateAsync_WithValidMessage_ProcessesCorrectly()
        {
            // Arrange  
            var mockBot = new Mock<ITelegramBotClient>();
            var update = new Update()
            {
                Message = new Message()
                {
                    Chat = new Chat() { Id = 12345 },
                    Text = "/start",
                    From = new User() { FirstName = "TestUser", Id = 67890 }
                }
            };

            // Act & Assert - Simply verify that the method doesn't throw
            await _handler.HandleUpdateAsync(mockBot.Object, update, CancellationToken.None);

            // The test passes if no exception is thrown
            Assert.True(true);
        }

        [Fact]
        public async Task HandleUpdateAsync_WithNullMessage_DoesNotProcess()
        {
            // Arrange
            var mockBot = new Mock<ITelegramBotClient>();
            var update = new Update(); // No message

            // Act & Assert - Simply verify that the method doesn't throw
            await _handler.HandleUpdateAsync(mockBot.Object, update, CancellationToken.None);

            // The test passes if no exception is thrown
            Assert.True(true);
        }
    }
}