using Xunit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ReflectaBot.Controllers;
using Telegram.Bot.Types;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ReflectaBot.Tests
{
    public class WebHookControllerTests_Disabled
    {
        /*
        private WebHookController CreateController()
        {
            var configDict = new Dictionary<string, string>
            {
                ["Telegram:BotToken"] = "123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk"
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configDict!)
                .Build();

            return new WebHookController(configuration);
        }
        */

        /*
        [Fact]
        public async Task Post_WithNullMessage_ReturnsOk()
        {
            // Arrange
            var controller = CreateController();
            var update = new Update();

            // Act
            var result = await controller.Post(update);

            // Assert
            Assert.IsType<OkResult>(result);
        }

        [Fact]
        public async Task Post_WithNullChatId_ReturnsOk()
        {
            // Arrange
            var controller = CreateController();
            var update = new Update
            {
                Message = new Message()
            };

            // Act
            var result = await controller.Post(update);

            // Assert
            Assert.IsType<OkResult>(result);
        }

        [Fact]
        public void Post_ControllerExists_IsNotNull()
        {
            // Arrange & Act
            var controller = CreateController();

            // Assert
            Assert.NotNull(controller);
        }
        */
    }
}