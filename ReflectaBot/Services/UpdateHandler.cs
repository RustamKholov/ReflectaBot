using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ReflectaBot.Interfaces;
using ReflectaBot.Interfaces.Intent;
using ReflectaBot.Models.Intent;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace ReflectaBot.Services
{
    public class UpdateHandler : IUpdateHandler
    {
        private readonly ITelegramBotClient _bot;
        private readonly ILogger<UpdateHandler> _logger;
        private readonly IIntentRouter _intentRouter;
        private readonly IUrlService _urlService;
        private readonly IContentScrapingService _contentScrapingService;
        private readonly IUrlCacheService _urlCacheService;

        public UpdateHandler(
            ITelegramBotClient bot,
            ILogger<UpdateHandler> logger,
            IIntentRouter intentRouter,
            IUrlService urlService,
            IContentScrapingService contentScrapingService,
            IUrlCacheService urlCacheService)
        {
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _intentRouter = intentRouter ?? throw new ArgumentNullException(nameof(intentRouter));
            _urlService = urlService ?? throw new ArgumentNullException(nameof(urlService));
            _contentScrapingService = contentScrapingService ?? throw new ArgumentNullException(nameof(contentScrapingService));
            _urlCacheService = urlCacheService ?? throw new ArgumentNullException(nameof(urlCacheService));
        }

        public async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
        {
            _logger.LogError(exception, "HandleError from {Source}", source);

            // Cooldown for network errors following project guidelines for resilience
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
            _logger.LogInformation("Receive message type: {MessageType}", message.Type);

            if (message.Text is not { } messageText)
                return;

            string user = message.From?.FirstName ?? "Unknown";

            var urls = _urlService.ExtractUrls(messageText);
            if (urls.Count != 0)
            {
                await HandleUrlsDetected(message, urls, user);
                return;
            }

            var (intent, confidence) = await _intentRouter.RouteAsync(messageText);
            _logger.LogInformation("Intent detected: {Intent} with confidence: {Confidence:F3} for user: {User}",
                intent, confidence, user);

            Message msg = await (intent switch
            {
                //Content Processing Intents
                ContentIntents.ProcessUrl => HandleUrlProcessing(message, user),
                ContentIntents.GetSummary => HandleSummaryRequest(message, user),
                ContentIntents.CreateQuiz => HandleQuizRequest(message, user),
                ContentIntents.SaveForLater => HandleSaveForLater(message, user),

                //Learning System Intents
                ContentIntents.StudyFlashcards => HandleStudySession(message, user),
                ContentIntents.ReviewDue => HandleReviewCheck(message, user),
                ContentIntents.TakeQuiz => HandleTakeQuiz(message, user),
                ContentIntents.GetProgress => HandleProgressRequest(message, user),

                //Content Discovery Intents
                ContentIntents.BrowseArticles => HandleBrowseArticles(message, user),
                ContentIntents.ShareArticle => HandleShareArticle(message, user),
                ContentIntents.FindSimilar => HandleFindSimilar(message, user),
                ContentIntents.GetRecommendations => HandleGetRecommendations(message, user),

                //Support Intents
                ContentIntents.SetPreferences => HandleSetPreferences(message, user),
                ContentIntents.GetHelp => HandleHelpRequest(message, user),
                ContentIntents.GetStats => HandleGetStats(message, user),
                ContentIntents.Greeting => HandleGreeting(message, user),

                //LEGACY
                "joke" or "humor" => HandleLegacyJoke(message, user),
                "dice" or "roll" or "random" => HandleLegacyDice(message, user),
                "fact" or "trivia" => HandleLegacyFact(message, user),
                "coin" or "flip" => HandleLegacyCoin(message, user),
                "time" or "clock" => HandleLegacyTime(message, user),
                "weather" => HandleLegacyWeather(message, user),

                //LEGACY: Command handlers
                _ when messageText.StartsWith("/start") => SendStart(message),
                _ when messageText.StartsWith("/clear") => ClearKeyboard(message),
                _ when messageText.StartsWith("/legacy") => ShowLegacyFeatures(message, user),

                //fallback 
                ContentIntents.None when confidence < 0.5 => HandleUnknownIntent(message, messageText, confidence),

                _ => HandleUnknownIntent(message, messageText, confidence)
            });

            _logger.LogInformation("Response sent with id: {SentMessageId}", msg.Id);
        }

        #region URL Processing Methods (Fully Implemented)

        private async Task HandleUrlsDetected(Message message, List<string> urls, string user)
        {
            _logger.LogInformation("Processing {UrlCount} URLs from {User}", urls.Count, user);

            var url = urls.First();

            try
            {
                var processingMessage = await _bot.SendMessage(
                    chatId: message.Chat.Id,
                    text: $"🔍 Processing article from {new Uri(url).Host}...\n\n" +
                          "This may take a few seconds while I extract and analyze the content."
                );

                var scrapedContent = await _contentScrapingService.ScrapeFromUrlAsync(url);

                if (!scrapedContent.Success)
                {
                    await _bot.EditMessageText(
                        chatId: message.Chat.Id,
                        messageId: processingMessage.MessageId,
                        text: $"❌ Sorry, I couldn't process that article:\n\n" +
                              $"**Error:** {scrapedContent.Error}\n\n" +
                              "Please try sharing a different article URL."
                    );
                    return;
                }

                var urlId = await _urlCacheService.CacheUrlAsync(url);
                _logger.LogDebug("Cached URL {Url} with identifier: {UrlId}", url, urlId);

                var previewText = scrapedContent.Content.Length > 300
                    ? scrapedContent.Content.Substring(0, 300) + "..."
                    : scrapedContent.Content;

                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("📝 Generate Summary", $"summary:{urlId}"),
                        InlineKeyboardButton.WithCallbackData("🧠 Create Quiz", $"quiz:{urlId}")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("💾 Save for Later", $"save:{urlId}"),
                        InlineKeyboardButton.WithCallbackData("🔍 Find Similar", $"similar:{urlId}")
                    }
                });

                await _bot.EditMessageText(
                    chatId: message.Chat.Id,
                    messageId: processingMessage.MessageId,
                    text: $"📄 **{scrapedContent.Title}**\n\n" +
                          $"📊 *{scrapedContent.WordCount} words • ~{EstimateReadingTime(scrapedContent.WordCount)} min read*\n\n" +
                          $"{previewText}\n\n" +
                          "What would you like to do with this article?",
                    replyMarkup: keyboard,
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown
                );

                _logger.LogInformation("Successfully processed article for {User}: {Title} ({WordCount} words)",
                    user, scrapedContent.Title, scrapedContent.WordCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing URL {Url} for user {User}", url, user);

                await _bot.SendMessage(
                    chatId: message.Chat.Id,
                    text: "❌ An unexpected error occurred while processing the article. Please try again later."
                );
            }
        }

        #endregion

        #region Content Processing Intent Handlers

        private async Task<Message> HandleUrlProcessing(Message message, string user)
        {
            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: $"🔍 Hi {user}! I'm ready to help you learn from web articles.\n\n" +
                      "**Just share any article URL and I'll:**\n" +
                      "📝 Extract and summarize the content\n" +
                      "🧠 Create interactive quizzes\n" +
                      "💾 Save articles for later study\n" +
                      "🎯 Help you build lasting knowledge\n\n" +
                      "Try sharing a link to an interesting article!",
                replyMarkup: GetContentProcessingKeyboard()
            );
        }

        private async Task<Message> HandleSummaryRequest(Message message, string user)
        {
            // TODO: Implement AI summary generation following project guidelines
            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: $"📝 {user}, I'd love to create a summary for you!\n\n" +
                      "Please share an article URL and I'll extract the key points and main ideas.\n\n" +
                      "*🚧 Coming soon: AI-powered summaries with bullet points and key takeaways using OpenAI API*",
                replyMarkup: GetMainMenuKeyboard()
            );
        }

        private async Task<Message> HandleQuizRequest(Message message, string user)
        {
            // TODO: Implement AI  quiz generation following project guidelines
            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: $"🧠 {user}, ready to test your knowledge?\n\n" +
                      "Share an article URL and I'll create personalized quiz questions to help you learn and remember the content!\n\n" +
                      "*🚧 Coming soon: Interactive quizzes with immediate feedback and spaced repetition*",
                replyMarkup: GetMainMenuKeyboard()
            );
        }

        private async Task<Message> HandleSaveForLater(Message message, string user)
        {
            // TODO: Implement article saving with database
            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: $"💾 {user}, your personal learning library is being prepared!\n\n" +
                      "*🚧 Coming soon: Save articles, organize by topics, and track your reading progress*",
                replyMarkup: GetMainMenuKeyboard()
            );
        }

        #endregion

        #region Learning System Intent Handlers

        private async Task<Message> HandleStudySession(Message message, string user)
        {
            // TODO: Implement spaced repetition
            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: $"🎯 {user}, let's start a focused study session!\n\n" +
                      "*🚧 Coming soon: Personalized flashcard reviews based on spaced repetition algorithm*",
                replyMarkup: GetStudyKeyboard()
            );
        }

        private async Task<Message> HandleReviewCheck(Message message, string user)
        {
            // TODO: Implement review scheduling
            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: $"📅 {user}, checking your review schedule...\n\n" +
                      "*🚧 Coming soon: Smart review reminders based on your learning progress*",
                replyMarkup: GetMainMenuKeyboard()
            );
        }

        private async Task<Message> HandleTakeQuiz(Message message, string user)
        {
            // TODO: Implement interactive quiz
            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: $"📝 {user}, ready for a knowledge challenge?\n\n" +
                      "*🚧 Coming soon: Interactive multiple-choice quizzes with immediate feedback*",
                replyMarkup: GetMainMenuKeyboard()
            );
        }

        private async Task<Message> HandleProgressRequest(Message message, string user)
        {
            // TODO: Implement progress tracking
            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: $"📊 {user}, here's your learning progress!\n\n" +
                      "*🚧 Coming soon: Detailed analytics on reading speed, quiz scores, and retention rates*",
                replyMarkup: GetMainMenuKeyboard()
            );
        }

        #endregion

        #region Content Discovery Intent Handlers

        private async Task<Message> HandleBrowseArticles(Message message, string user)
        {
            // TODO: Implement article browsing
            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: $"📚 {user}, browse your saved articles and discover new content!\n\n" +
                      "*🚧 Coming soon: Smart article organization with tags and search functionality*",
                replyMarkup: GetBrowseKeyboard()
            );
        }

        private async Task<Message> HandleShareArticle(Message message, string user)
        {
            // TODO: Implement article sharing functionality
            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: $"🔗 {user}, share interesting articles with the community!\n\n" +
                      "*🚧 Coming soon: Social features for sharing and discussing articles*",
                replyMarkup: GetMainMenuKeyboard()
            );
        }

        private async Task<Message> HandleFindSimilar(Message message, string user)
        {
            // TODO: Implement AI content similarity using embeddings
            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: $"🔍 {user}, finding articles similar to your interests...\n\n" +
                      "*🚧 Coming soon: AI-powered content discovery using semantic similarity*",
                replyMarkup: GetMainMenuKeyboard()
            );
        }

        private async Task<Message> HandleGetRecommendations(Message message, string user)
        {
            // TODO: Implement personalized recommendations based on user behavior
            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: $"🎯 {user}, here are personalized recommendations for you!\n\n" +
                      "*🚧 Coming soon: ML-powered recommendations based on your reading history*",
                replyMarkup: GetMainMenuKeyboard()
            );
        }

        #endregion

        #region Meta & Support Intent Handlers

        private async Task<Message> HandleSetPreferences(Message message, string user)
        {
            // TODO: Implement user preferences management
            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: $"⚙️ {user}, customize your learning experience!\n\n" +
                      "*🚧 Coming soon: Personalization settings for summaries, quizzes, and notifications*",
                replyMarkup: GetPreferencesKeyboard()
            );
        }

        private async Task<Message> HandleHelpRequest(Message message, string user)
        {
            const string helpText = """
                🤖 **ReflectaBot - Your AI Learning Companion**

                I help you learn from web articles using AI! Here's what I can do:

                **📝 Article Processing:**
                • Share any article URL for instant processing
                • Get AI-generated summaries with key points
                • Create interactive quizzes for better retention
                • Save articles to your personal learning library
                
                **🎯 Learning Features:**
                • Spaced repetition system for long-term retention *(coming soon)*
                • Progress tracking and learning analytics *(coming soon)*
                • Personalized content recommendations *(coming soon)*
                
                **🔗 Getting Started:**
                Just share any article URL to begin learning!
                
                **Examples:**
                • Share: https://example.com/interesting-article
                • Ask: "Create a quiz from this article"
                • Say: "Show my reading progress"
                
                **🎭 Legacy Features:**
                Type /legacy for classic entertainment features
                """;

            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: helpText,
                replyMarkup: GetMainMenuKeyboard()
            );
        }

        private async Task<Message> HandleGetStats(Message message, string user)
        {
            // TODO: Implement comprehensive statistics
            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: $"📈 {user}, here are your learning statistics!\n\n" +
                      "*🚧 Coming soon: Detailed insights on articles read, quizzes completed, and knowledge retention*",
                replyMarkup: GetMainMenuKeyboard()
            );
        }

        private async Task<Message> HandleGreeting(Message message, string user)
        {
            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: $"👋 Hello {user}! I'm ReflectaBot, your AI learning companion!\n\n" +
                      "🔗 **Share any article URL** and I'll help you:\n" +
                      "📝 Create comprehensive summaries\n" +
                      "🧠 Generate knowledge-testing quizzes\n" +
                      "💾 Build your personal learning library\n" +
                      "🎯 Develop lasting understanding through spaced repetition\n\n" +
                      "Try sharing a link to an article you'd like to learn from!\n\n" +
                      "🎭 *Tip: Type /legacy for entertainment features*",
                replyMarkup: GetMainMenuKeyboard()
            );
        }

        #endregion

        #region Legacy Entertainment Handlers (Preserved)

        private async Task<Message> HandleLegacyJoke(Message message, string user)
        {
            return await _bot.SendMessage(message.Chat, GetRandomJoke(), replyMarkup: GetLegacyKeyboard());
        }

        private async Task<Message> HandleLegacyDice(Message message, string user)
        {
            var result = $"🎲 {user}, you rolled: {Random.Shared.Next(1, 7)}!";
            return await _bot.SendMessage(message.Chat, result, replyMarkup: GetLegacyKeyboard());
        }

        private async Task<Message> HandleLegacyFact(Message message, string user)
        {
            return await _bot.SendMessage(message.Chat, GetRandomFact(), replyMarkup: GetLegacyKeyboard());
        }

        private async Task<Message> HandleLegacyCoin(Message message, string user)
        {
            var result = Random.Shared.Next(2) == 0 ? "🪙 Heads!" : "🪙 Tails!";
            return await _bot.SendMessage(message.Chat, $"{user}, {result}", replyMarkup: GetLegacyKeyboard());
        }

        private async Task<Message> HandleLegacyTime(Message message, string user)
        {
            var result = $"⏰ Server time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} UTC";
            return await _bot.SendMessage(message.Chat, result, replyMarkup: GetLegacyKeyboard());
        }

        private async Task<Message> HandleLegacyWeather(Message message, string user)
        {
            return await _bot.SendMessage(message.Chat,
                "🌤️ I can't check weather yet, but it's always sunny in the server room!",
                replyMarkup: GetMainMenuKeyboard());
        }

        #endregion

        #region Support Methods

        private async Task<Message> HandleUnknownIntent(Message message, string messageText, double confidence)
        {
            string user = message.From?.FirstName ?? "Unknown";

            _logger.LogWarning("Unknown intent for message: '{MessageText}' with confidence: {Confidence:F3}",
                messageText, confidence);

            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: $"🤔 I'm not sure how to help with that, {user}.\n\n" +
                      "💡 **Try:**\n" +
                      "🔗 Share an article URL to get started\n" +
                      "❓ Ask for help or summaries\n" +
                      "🎯 Request a quiz or study session\n" +
                      "📚 Browse your saved articles\n" +
                      "🎭 Type /legacy for entertainment\n\n" +
                      "What would you like to learn about today?",
                replyMarkup: GetMainMenuKeyboard()
            );
        }

        private async Task<Message> ShowLegacyFeatures(Message message, string user)
        {
            const string legacyText = """
                🎭 **Legacy Entertainment Features**
                
                These classic features are still available for fun:
                
                🎪 **Entertainment:**
                • Ask for a joke or say "tell me a joke"
                • Roll dice with "roll dice" or "random number"  
                • Get fun facts with "fun fact" or "trivia"
                • Flip coin with "flip coin" or "heads or tails"
                • Check server time with "what time is it"
                • Ask about weather for a fun response
                
                💡 **Just type naturally - I understand various ways of asking!**
                
                🚀 **Ready for modern learning?** Share an article URL!
                """;

            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: legacyText,
                replyMarkup: GetLegacyKeyboard()
            );
        }

        private async Task<Message> SendStart(Message message)
        {
            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: "👋 **Welcome to ReflectaBot!** 🤖\n\n" +
                      "Your AI-powered learning companion is ready to help you master any topic!\n\n" +
                      "🔗 **Share an article URL** to get started with:\n" +
                      "📝 Intelligent summaries\n" +
                      "🧠 Interactive quizzes\n" +
                      "💾 Personal learning library\n" +
                      "🎯 Spaced repetition system\n\n" +
                      "🎭 *Or try /legacy for entertainment features*",
                replyMarkup: GetMainMenuKeyboard()
            );
        }

        private async Task<Message> ClearKeyboard(Message message)
        {
            return await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: "🧹 Keyboard cleared! Send me a message or use /start to see options again.",
                replyMarkup: new ReplyKeyboardRemove()
            );
        }

        #endregion

        #region Callback Query Handler

        private async Task OnCallbackQuery(CallbackQuery callbackQuery)
        {
            await _bot.AnswerCallbackQuery(callbackQuery.Id);

            string user = callbackQuery.From?.FirstName ?? "Unknown";
            _logger.LogInformation("Received callback query from {User}: {Data}", user, callbackQuery.Data);

            if (callbackQuery.Message is { } message && callbackQuery.Data is { } data)
            {
                if (data.Contains(':'))
                {
                    var parts = data.Split(':', 2);
                    var action = parts[0];
                    var urlId = parts.Length > 1 ? parts[1] : "";

                    var originalUrl = await _urlCacheService.GetUrlAsync(urlId);
                    if (string.IsNullOrEmpty(originalUrl))
                    {
                        await _bot.EditMessageText(
                            chatId: message.Chat.Id,
                            messageId: message.MessageId,
                            text: "❌ Sorry, the article reference has expired. Please share the URL again.",
                            replyMarkup: GetMainMenuKeyboard()
                        );
                        return;
                    }

                    _logger.LogInformation("Processing callback action '{Action}' for URL: {Url}", action, originalUrl);

                    var responseText = action switch
                    {
                        "summary" => $"📝 {user}, generating AI summary for this article...\n\n*🚧 Feature in development - AI-powered summarization coming soon!*\n\n🔗 Article: {new Uri(originalUrl).Host}",
                        "quiz" => $"🧠 {user}, creating interactive quiz questions...\n\n*🚧 Feature in development - Personalized quizzes with spaced repetition coming soon!*\n\n🔗 Article: {new Uri(originalUrl).Host}",
                        "save" => $"💾 {user}, saving article to your personal library...\n\n*🚧 Feature in development - Personal article management coming soon!*\n\n🔗 Article: {new Uri(originalUrl).Host}",
                        "similar" => $"🔍 {user}, finding similar articles...\n\n*🚧 Feature in development - AI-powered content discovery coming soon!*\n\n🔗 Article: {new Uri(originalUrl).Host}",
                        _ => "🤖 Unknown action selected."
                    };

                    await _bot.EditMessageText(
                        chatId: message.Chat.Id,
                        messageId: message.MessageId,
                        text: responseText,
                        replyMarkup: GetMainMenuKeyboard()
                    );
                }
                else
                {
                    await HandleNonUrlCallback(message, data, user);
                }
            }
        }

        private async Task HandleNonUrlCallback(Message message, string data, string user)
        {
            var (responseText, keyboard) = data switch
            {
                // Legacy
                "joke" => (GetRandomJoke(), GetLegacyKeyboard()),
                "dice" => ($"🎲 {user}, you rolled: {Random.Shared.Next(1, 7)}!", GetLegacyKeyboard()),
                "fact" => (GetRandomFact(), GetLegacyKeyboard()),
                "coin" => ($"{user}, {(Random.Shared.Next(2) == 0 ? "🪙 Heads!" : "🪙 Tails!")}", GetLegacyKeyboard()),
                "time" => ($"⏰ Server time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} UTC", GetLegacyKeyboard()),

                // Main menu callbacks
                "browse" => ($"📚 {user}, browsing your article library...\n\n*🚧 Coming soon: Smart article organization and search*", GetBrowseKeyboard()),
                "study" => ($"🎯 {user}, starting your study session...\n\n*🚧 Coming soon: Personalized flashcard reviews*", GetStudyKeyboard()),
                "progress" => ($"📊 {user}, here's your learning progress!\n\n*🚧 Coming soon: Detailed analytics and insights*", GetMainMenuKeyboard()),
                "help" => (GetHelpText(), GetMainMenuKeyboard()),

                _ => ("🤖 Unknown command!", GetMainMenuKeyboard())
            };

            await _bot.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.MessageId,
                text: responseText,
                replyMarkup: keyboard
            );
        }

        #endregion

        #region UI Helper Methods

        private static InlineKeyboardMarkup GetMainMenuKeyboard()
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📚 Browse Articles", "browse"),
                    InlineKeyboardButton.WithCallbackData("🎯 Study Session", "study")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📊 My Progress", "progress"),
                    InlineKeyboardButton.WithCallbackData("❓ Help", "help")
                }
            });
        }

        private static InlineKeyboardMarkup GetContentProcessingKeyboard()
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📝 Summary Examples", "summary_help"),
                    InlineKeyboardButton.WithCallbackData("🧠 Quiz Examples", "quiz_help")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("💾 Save Articles", "save_help"),
                    InlineKeyboardButton.WithCallbackData("🔙 Main Menu", "main_menu")
                }
            });
        }

        private static InlineKeyboardMarkup GetStudyKeyboard()
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📇 Review Flashcards", "review_cards"),
                    InlineKeyboardButton.WithCallbackData("📝 Take Quiz", "take_quiz")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📊 Study Stats", "study_stats"),
                    InlineKeyboardButton.WithCallbackData("🔙 Main Menu", "main_menu")
                }
            });
        }

        private static InlineKeyboardMarkup GetBrowseKeyboard()
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📖 Recent Articles", "recent"),
                    InlineKeyboardButton.WithCallbackData("⭐ Favorites", "favorites")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🔍 Search Articles", "search"),
                    InlineKeyboardButton.WithCallbackData("🔙 Main Menu", "main_menu")
                }
            });
        }

        private static InlineKeyboardMarkup GetPreferencesKeyboard()
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📝 Summary Style", "summary_prefs"),
                    InlineKeyboardButton.WithCallbackData("🧠 Quiz Difficulty", "quiz_prefs")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("⏰ Reminders", "reminder_prefs"),
                    InlineKeyboardButton.WithCallbackData("🔙 Main Menu", "main_menu")
                }
            });
        }

        private static InlineKeyboardMarkup GetLegacyKeyboard()
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("😄 Joke", "joke"),
                    InlineKeyboardButton.WithCallbackData("🎲 Dice", "dice")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🧠 Fun Fact", "fact"),
                    InlineKeyboardButton.WithCallbackData("🪙 Coin Flip", "coin")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("⏰ Time", "time"),
                    InlineKeyboardButton.WithCallbackData("🔙 Main Menu", "main_menu")
                }
            });
        }

        #endregion

        #region Static Content Methods

        private static string GetRandomJoke()
        {
            var jokes = new[]
            {
                "Why do programmers prefer dark mode? Because light attracts bugs! 🐛",
                "How many programmers does it take to change a light bulb? None, that's a hardware problem! 💡",
                "Why did the developer go broke? Because he used up all his cache! 💸",
                "What's a programmer's favorite hangout place? Foo Bar! 🍺",
                "Why do Java developers wear glasses? Because they don't C#! 👓",
                "What do you call a programmer from Finland? Nerdic! 🇫🇮"
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
                "🧠 Your brain uses about 20% of your body's total energy!",
                "🦈 Sharks have been around longer than trees!"
            };
            return facts[Random.Shared.Next(facts.Length)];
        }

        private static string GetHelpText()
        {
            return """
                🤖 **ReflectaBot Help**

                **🔗 Article Processing:**
                Share any URL to get summaries and quizzes

                **📚 Learning Features:**
                • Browse saved articles
                • Take personalized quizzes
                • Track learning progress
                • Spaced repetition reviews

                **💡 Tips:**
                • Start by sharing an interesting article
                • Use natural language - I understand context
                • Check your progress regularly

                **🎭 Fun:** Type /legacy for entertainment!
                """;
        }

        private static int EstimateReadingTime(int wordCount)
        {
            const int wordsPerMinute = 200;
            return Math.Max(1, (int)Math.Ceiling((double)wordCount / wordsPerMinute));
        }

        #endregion

        #region Event Handlers (Placeholders)

        private async Task OnInlineQuery(InlineQuery inlineQuery)
        {
            _logger.LogDebug("Received inline query: {Query}", inlineQuery.Query);
            // TODO: Implement inline search
        }

        private async Task OnChosenInlineResult(ChosenInlineResult result)
        {
            _logger.LogDebug("Chosen inline result: {ResultId}", result.ResultId);
            // TODO: Implement analytics for inline results
        }

        private async Task OnPoll(Poll poll)
        {
            _logger.LogDebug("Received poll update: {PollId}", poll.Id);
            // TODO: Implement poll-based quizzes
        }

        private async Task OnPollAnswer(PollAnswer pollAnswer)
        {
            _logger.LogDebug("Received poll answer from user: {UserId}", pollAnswer.User?.Id);
            // TODO: Implement poll answer processing for quiz results
        }

        private Task UnknownUpdateHandlerAsync(Update update)
        {
            _logger.LogInformation("Unknown update type: {UpdateType}", update.Type);
            return Task.CompletedTask;
        }

        #endregion
    }
}