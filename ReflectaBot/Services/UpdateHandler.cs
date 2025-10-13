using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;

namespace ReflectaBot.Services
{
    public class UpdateHandler(ITelegramBotClient bot, ILogger<UpdateHandler> logger) : IUpdateHandler
    {
        public async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
        {
            logger.LogInformation("HandleError: {Exception}", exception);
            // Cooldown in case of network connection error
            if (exception is RequestException)
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await (update switch
            {
                { Message: { } message } => OnMessage(message),
                { EditedMessage: { } message } => OnMessage(message),
                { CallbackQuery: { } callbackQuery } => OnCallbackQuery(callbackQuery),
                { InlineQuery: { } inlineQuery } => OnInlineQuery(inlineQuery),
                { ChosenInlineResult: { } chosenInlineResult } => OnChosenInlineResult(chosenInlineResult),
                { Poll: { } poll } => OnPoll(poll),
                { PollAnswer: { } pollAnswer } => OnPollAnswer(pollAnswer),
                _ => UnknownUpdateHandlerAsync(update)
            });
        }


        private async Task OnMessage(Message message)
        {
            logger.LogInformation("Receive message type: {MessageType}", message.Type);
            if (message.Text is not { } messageText)
                return;
            string user = message.From?.FirstName ?? "Unknown";
            Message msg = await (messageText switch
            {
                "/start" => SendStart(message),
                "joke" => bot.SendMessage(message.Chat, ProcessMessage("/joke", user)),
                "dice" => bot.SendMessage(message.Chat, ProcessMessage("/roll", user)),
                "fact" => bot.SendMessage(message.Chat, ProcessMessage("/fact", user)),
                "coin" => bot.SendMessage(message.Chat, ProcessMessage("/flip", user)),
                "time" => bot.SendMessage(message.Chat, ProcessMessage("/time", user)),
                _ => Usage(message)
            });
            logger.LogInformation("The message was sent with id: {SentMessageId}", msg.Id);
        }
        async Task<Message> SendStart(Message message)
        {
            return await bot.SendMessage(
                chatId: message.Chat.Id,
                text: "What would you like to do?",
                replyMarkup: new InlineKeyboardMarkup(
                [
                    [
                        InlineKeyboardButton.WithCallbackData("Get a joke", "joke"),
                        InlineKeyboardButton.WithCallbackData("Roll the dice", "dice")
                    ],
                    [
                        InlineKeyboardButton.WithCallbackData("Get a fun fact", "fact"),
                        InlineKeyboardButton.WithCallbackData("Flip a coin", "coin")
                    ],
                    [
                        InlineKeyboardButton.WithCallbackData("Get a server time", "time")
                    ]
                ])
            );
        }

        async Task<Message> Usage(Message message)
        {
            const string usage = """
            /start - start bot
            """;
            return await bot.SendMessage(message.Chat, usage);
        }

        private async Task OnCallbackQuery(CallbackQuery callbackQuery)
        {

        }
        private async Task OnInlineQuery(InlineQuery inlineQuery)
        {
            logger.LogInformation("Received inline query from: {InlineQueryFromId}", inlineQuery.From.Id);

            InlineQueryResult[] results = [ // displayed result
                new InlineQueryResultArticle("1", "Telegram.Bot", new InputTextMessageContent("hello")),
                new InlineQueryResultArticle("2", "is the best", new InputTextMessageContent("world"))
            ];
            await bot.AnswerInlineQuery(inlineQuery.Id, results, cacheTime: 0, isPersonal: true);
        }
        private async Task OnChosenInlineResult(ChosenInlineResult result)
        {

        }
        private async Task OnPoll(Poll poll)
        {

        }
        private async Task OnPollAnswer(PollAnswer pollAnswer)
        {

        }
        private Task UnknownUpdateHandlerAsync(Update update)
        {
            logger.LogInformation("Unknown update type: {UpdateType}", update.Type);
            return Task.CompletedTask;
        }


        public string ProcessMessage(string messageText, string user, Message? message = null)
        {
            return messageText switch
            {
                "/joke" => GetRandomJoke(),
                "/flip" => Random.Shared.Next(2) == 0 ? "🪙 Heads!" : "🪙 Tails!",
                "/roll" => $"🎲 You rolled: {Random.Shared.Next(1, 7)}",
                "/time" => $"⏰ Server time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} UTC",
                "/fact" => GetRandomFact(),

                var text when text.Contains("hello") || text.Contains("hi") =>
                    $"Hello {user}! 👋 Nice to meet you!",

                var text when text.Contains("weather") =>
                    "🌤️ I can't check weather yet, but it's always sunny in the server room!",

                var text when text.Contains("deploy") =>
                    "🚀 Deployment successful! I'm running the latest version!",

                _ => $"Hello {user}! You said: '{message?.Text}'\n" +
                     $"🎲 Random number: {Random.Shared.Next(1, 100)}\n" +
                     $"💬 Message ID: {message?.MessageId}\n" +
                     $"📅 Time: {DateTime.Now:HH:mm:ss}"
            };
        }

        private static string GetRandomJoke()
        {
            var jokes = new[]
            {
                "Why do programmers prefer dark mode? Because light attracts bugs! 🐛",
                "How many programmers does it take to change a light bulb? None, that's a hardware problem! 💡",
                "Why did the developer go broke? Because he used up all his cache! 💸",
                "What's a programmer's favorite hangout place? Foo Bar! 🍺",
                "Why do Java developers wear glasses? Because they don't C#! 👓"
            };
            return jokes[Random.Shared.Next(jokes.Length)];
        }

        private static string GetRandomFact()
        {
            var facts = new[]
            {
                "🐙 Octopuses have three hearts and blue blood!",
                "🍯 Honey never spoils - archaeologists have found edible honey in ancient Egyptian tombs!",
                "🌙 A day on Venus is longer than its year!",
                "🐧 Penguins have knees, they're just hidden under their feathers!",
                "🧠 Your brain uses about 20% of your body's total energy!"
            };
            return facts[Random.Shared.Next(facts.Length)];
        }
    }
}