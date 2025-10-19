
using ReflectaBot.Interfaces;
using ReflectaBot.Interfaces.Intent;
using ReflectaBot.Models.Intent;
using ReflectaBot.Models.Content;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using ReflectaBot.Interfaces.Enums;
using Elasticsearch.Net.Specification.CatApi;
using System.Net.Mime;
using ReflectaBot.Models.AI;

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
        private readonly IContentProcessor _contentProcessor;
        private readonly IContentCacheService _contentCacheService;
        private readonly IAIContentService _aIContentService;

        public UpdateHandler(
            ITelegramBotClient bot,
            ILogger<UpdateHandler> logger,
            IIntentRouter intentRouter,
            IUrlService urlService,
            IContentScrapingService contentScrapingService,
            IUrlCacheService urlCacheService,
            IContentProcessor contentProcessor,
            IContentCacheService contentCacheService,
            IAIContentService aIContentService)
        {
            _bot = bot;
            _logger = logger;
            _intentRouter = intentRouter;
            _urlService = urlService;
            _contentScrapingService = contentScrapingService;
            _urlCacheService = urlCacheService;
            _contentProcessor = contentProcessor;
            _contentCacheService = contentCacheService;
            _aIContentService = aIContentService;
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

            var contentType = DetectContentType(messageText, message);

            if (contentType != ContentSourceType.Unknow)
            {
                await HandleContentProcessing(message, messageText, contentType, user);
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


        private async Task HandleContentProcessing(Message message, string text, ContentSourceType sourceType, string user)
        {
            try
            {
                var processingMessage = await _bot.SendMessage(
                    chatId: message.Chat.Id,
                    text: $"🔍 Processing {GetContentTypeDescription(sourceType)}...\n\n" +
                  "Analyzing content and preparing learning materials."
                );

                var processedContent = await _contentProcessor.ProcessAsync(text, sourceType);

                if (!processedContent.Success)
                {
                    await _bot.EditMessageText(
                        chatId: message.Chat.Id,
                        messageId: processingMessage.MessageId,
                        text: $"❌ Sorry, I couldn't process that content:\n\n" +
                                $"**Error:** {processedContent.Error}\n\n" +
                                GetContentProcessingTips(sourceType)
                    );
                    return;
                }
                var contentId = await _contentCacheService.CacheContentAsync(processedContent);
                var keyboard = CreateContentActionKeyboard(contentId, sourceType);

                var previewText = processedContent.Content.Length > 400
                    ? processedContent.Content.Substring(0, 400) + "..."
                    : processedContent.Content;

                await _bot.EditMessageText(
                    chatId: message.Chat.Id,
                    messageId: processingMessage.MessageId,
                    text: FormatContentPreview(processedContent, previewText),
                    replyMarkup: keyboard,
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown
                );

                _logger.LogInformation("Successfully processed {ContentType} for {User}: {WordCount} words",
                    sourceType, user, processedContent.WordCount);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing {ContentType} for user {User}", sourceType, user);

                await _bot.SendMessage(
                    chatId: message.Chat.Id,
                    text: $"❌ An error occurred while processing the content. {GetContentProcessingTips(sourceType)}"
                );
            }
        }
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

        private ContentSourceType DetectContentType(string text, Message message)
        {
            var urls = _urlService.ExtractUrls(text);
            if (urls.Any())
            {
                return ContentSourceType.Url;
            }

            if (message.Document != null)
            {
                var allowedTypes = new[] { ".txt", ".pdf", ".docx", ".md" };
                var extension = Path.GetExtension(message.Document.FileName?.ToLower());
                if (allowedTypes.Contains(extension))
                {
                    return ContentSourceType.Document;
                }
            }

            if (text.Length > 200 && text.Split(' ').Length > 30)
            {
                return ContentSourceType.PlainText;
            }
            return ContentSourceType.Unknow;
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
                    var parts = data.Split(':');
                    var action = parts[0];

                    // Handle quiz answer callbacks: "answer:contentId:questionIndex:selectedAnswer"
                    if (action == "answer" && parts.Length >= 4)
                    {
                        var contentId = parts[1];
                        var questionIndex = int.Parse(parts[2]);
                        var selectedAnswer = int.Parse(parts[3]);
                        await HandleQuizAnswer(message, contentId, questionIndex, selectedAnswer, user);
                    }
                    // Handle other content actions: "action:contentId"
                    else if (parts.Length >= 2)
                    {
                        var contentId = string.Join(":", parts.Skip(1)); // Rejoin in case contentId has colons
                        await HandleContentAction(message, action, contentId, user);
                    }
                }
                else
                {
                    await HandleNonContentCallback(message, data, user);
                }
            }
        }
        private async Task HandleContentAction(Message message, string action, string contentId, string user)
        {
            try
            {
                string actualContentId;

                if (action == "next_question" && contentId.Contains(':'))
                {
                    var parts = contentId.Split(':');
                    actualContentId = parts[0];
                    var questionIndex = parts.Length > 1 ? int.Parse(parts[1]) : 0;

                    _logger.LogDebug("Next question request - ContentId: {ContentId}, QuestionIndex: {QuestionIndex}",
                        actualContentId, questionIndex);

                    await HandleNextQuestion(message, actualContentId, questionIndex, user);
                    return;
                }
                else
                {
                    actualContentId = contentId;
                }

                var cachedContent = await _contentCacheService.GetContentAsync(actualContentId);
                if (cachedContent == null)
                {
                    _logger.LogWarning("Content not found for action '{Action}', ContentId: {ContentId}", action, actualContentId);
                    await _bot.EditMessageText(
                        chatId: message.Chat.Id,
                        messageId: message.MessageId,
                        text: "❌ Sorry, the content reference has expired. Please share the content again.",
                        replyMarkup: GetMainMenuKeyboard()
                    );
                    return;
                }

                _logger.LogInformation("Processing callback action '{Action}' for cached content: {WordCount} words",
                    action, cachedContent.WordCount);

                await (action switch
                {
                    "summary" => ProcessSummaryRequest(message, cachedContent, user),
                    "quiz" => ProcessQuizRequest(message, cachedContent, user),
                    "save" => ProcessSaveRequest(message, cachedContent, user),
                    "similar" => ProcessSimilarRequest(message, cachedContent, user),
                    "quiz_summary" => HandleQuizSummary(message, actualContentId, user),
                    _ => HandleUnknownAction(message, action, user)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing content action '{Action}' for user {User}", action, user);

                await _bot.EditMessageText(
                    chatId: message.Chat.Id,
                    messageId: message.MessageId,
                    text: "❌ An error occurred while processing your request. Please try again.",
                    replyMarkup: GetMainMenuKeyboard()
                );
            }
        }
        private async Task ProcessSummaryRequest(Message message, ProcessedContent content, string user)
        {
            try
            {
                await _bot.EditMessageText(
                    chatId: message.Chat.Id,
                    messageId: message.MessageId,
                    text: $"📝 Generating AI summary for {user}...\n\n" +
                          $"🔍 Analyzing {content.WordCount} words of content\n" +
                          $"⏱️ This may take a few seconds",
                    replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                InlineKeyboardButton.WithCallbackData("⏹️ Cancel", "cancel_summary")
                    })
                );

                var existingSummary = await CheckForExistingSummary(content);
                if (existingSummary != null)
                {
                    _logger.LogInformation("Using cached summary for content to optimize costs");
                    await DisplaySummaryResult(message, existingSummary, content, user);
                    return;
                }

                var summaryResult = await GenerateAiSummary(content);

                if (summaryResult.Success)
                {
                    await CacheSummaryResult(content, summaryResult);
                    await DisplaySummaryResult(message, summaryResult, content, user);
                }
                else
                {
                    await DisplaySummaryError(message, summaryResult.Error!, user);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating summary for user {User}", user);
                await DisplaySummaryError(message, "Summary generation failed", user);
            }
        }

        private async Task ProcessQuizRequest(Message message, ProcessedContent content, string user)
        {
            try
            {
                await _bot.EditMessageText(
                    chatId: message.Chat.Id,
                    messageId: message.MessageId,
                    text: $"🧠 Creating interactive quiz for {user}...\n\n" +
                          $"🔍 Analyzing {content.WordCount} words for key concepts\n" +
                          $"🎯 Generating multiple-choice questions\n" +
                          $"⏱️ This may take a few seconds",
                    replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                InlineKeyboardButton.WithCallbackData("⏹️ Cancel", "cancel_quiz")
                    })
                );

                var existingQuiz = await CheckForExistingQuiz(content);
                if (existingQuiz != null)
                {
                    _logger.LogInformation("Using cached quiz for content to optimize costs");
                    await DisplayQuizResult(message, existingQuiz, content, user);
                    return;
                }

                var quizResult = await GenerateAiQuiz(content);

                if (quizResult.Success)
                {
                    await CacheQuizResult(content, quizResult);
                    await DisplayQuizResult(message, quizResult, content, user);
                }
                else
                {
                    await DisplayQuizError(message, quizResult.Error!, user);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating quiz for user {User}", user);
                await DisplayQuizError(message, "Quiz generation failed", user);
            }
        }

        private async Task ProcessSaveRequest(Message message, ProcessedContent content, string user)
        {
            try
            {
                // TODO: Implement database saving with Entity Framework
                await _bot.EditMessageText(
                    chatId: message.Chat.Id,
                    messageId: message.MessageId,
                    text: $"💾 Saving content to {user}'s personal library...\n\n" +
                          $"📄 **{EscapeMarkdown(content.Title)}**\n" +
                          $"📊 {content.WordCount} words\n\n" +
                          "*🚧 Feature in development - Personal library with SQLite database coming soon!*",
                    replyMarkup: GetMainMenuKeyboard()
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving content for user {User}", user);
            }
        }

        private async Task ProcessSimilarRequest(Message message, ProcessedContent content, string user)
        {
            try
            {
                // TODO: Implement semantic similarity using embeddings
                await _bot.EditMessageText(
                    chatId: message.Chat.Id,
                    messageId: message.MessageId,
                    text: $"🔍 Finding articles similar to this content for {user}...\n\n" +
                          $"📄 **{EscapeMarkdown(content.Title)}**\n" +
                          $"🎯 Analyzing semantic content for recommendations\n\n" +
                          "*🚧 Feature in development - AI-powered content discovery using embeddings coming soon!*",
                    replyMarkup: GetMainMenuKeyboard()
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding similar content for user {User}", user);
            }
        }
        private async Task<AISummaryResult> GenerateAiSummary(ProcessedContent content)
        {
            try
            {
                _logger.LogInformation("Generating AI summary for content: {WordCount} words", content.WordCount);
                return await _aIContentService.GenerateSummaryAsync(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI summary generation failed");
                return new AISummaryResult
                {
                    Success = false,
                    Error = $"AI service error: {ex.Message}"
                };
            }
        }
        private async Task<AIQuizResult> GenerateAiQuiz(ProcessedContent content)
        {
            try
            {
                _logger.LogInformation("Generating AI quiz for content: {WordCount} words", content.WordCount);
                return await _aIContentService.GenerateQuizAsync(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI quiz generation failed");
                return new AIQuizResult
                {
                    Success = false,
                    Error = $"AI service error: {ex.Message}"
                };
            }
        }

        private async Task HandleNonContentCallback(Message message, string data, string user)
        {
            var (responseText, keyboard) = data switch
            {
                "joke" => (GetRandomJoke(), GetLegacyKeyboard()),
                "dice" => ($"🎲 {user}, you rolled: {Random.Shared.Next(1, 7)}!", GetLegacyKeyboard()),
                "fact" => (GetRandomFact(), GetLegacyKeyboard()),
                "coin" => ($"{user}, {(Random.Shared.Next(2) == 0 ? "🪙 Heads!" : "🪙 Tails!")}", GetLegacyKeyboard()),
                "time" => ($"⏰ Server time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} UTC", GetLegacyKeyboard()),

                "browse" => ($"📚 {user}, browsing your article library...\n\n*🚧 Coming soon: Smart article organization and search*", GetBrowseKeyboard()),
                "study" => ($"🎯 {user}, starting your study session...\n\n*🚧 Coming soon: Personalized flashcard reviews*", GetStudyKeyboard()),
                "progress" => ($"📊 {user}, here's your learning progress!\n\n*🚧 Coming soon: Detailed analytics and insights*", GetMainMenuKeyboard()),
                "help" => (GetHelpText(), GetMainMenuKeyboard()),
                "main_menu" => ("🏠 **Main Menu**\n\nWhat would you like to do?", GetMainMenuKeyboard()),

                "cancel_summary" => ("⏹️ Summary generation cancelled.", GetMainMenuKeyboard()),
                "cancel_quiz" => ("⏹️ Quiz generation cancelled.", GetMainMenuKeyboard()),

                _ => ("🤖 Unknown command!", GetMainMenuKeyboard())
            };

            await _bot.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.MessageId,
                text: responseText,
                replyMarkup: keyboard
            );
        }
        private async Task HandleUnknownAction(Message message, string action, string user)
        {
            await _bot.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.MessageId,
                text: $"🤖 Unknown action: {action}\n\nPlease try again or return to the main menu.",
                replyMarkup: GetMainMenuKeyboard()
            );
        }

        private async Task HandleQuizAnswer(Message message, string contentId, int questionIndex, int selectedAnswer, string user)
        {
            try
            {
                _logger.LogDebug("Processing quiz answer - ContentId: {ContentId}, Question: {QuestionIndex}, Answer: {SelectedAnswer}",
                    contentId, questionIndex, selectedAnswer);

                var cachedContent = await _contentCacheService.GetContentAsync(contentId);
                if (cachedContent == null)
                {
                    _logger.LogWarning("Content not found for quiz answer: {ContentId}", contentId);
                    await _bot.EditMessageText(
                        chatId: message.Chat.Id,
                        messageId: message.MessageId,
                        text: "❌ Sorry, the quiz session has expired. Please generate a new quiz.",
                        replyMarkup: GetMainMenuKeyboard()
                    );
                    return;
                }

                var quizResult = await GetQuizByContentId(contentId);
                if (quizResult == null || !quizResult.Success)
                {
                    _logger.LogWarning("Quiz not found for answer processing, regenerating for content: {ContentId}", contentId);

                    quizResult = await GenerateAiQuiz(cachedContent);
                    if (quizResult.Success)
                    {
                        await CacheQuizResult(cachedContent, quizResult);
                    }
                    else
                    {
                        await DisplayQuizError(message, "Quiz questions not available", user);
                        return;
                    }
                }

                if (questionIndex >= quizResult.Questions.Length)
                {
                    _logger.LogWarning("Invalid question index for answer: {QuestionIndex}", questionIndex);
                    await DisplayQuizError(message, "Question not found", user);
                    return;
                }

                var currentQuestion = quizResult.Questions[questionIndex];
                if (selectedAnswer >= currentQuestion.Options.Length)
                {
                    _logger.LogWarning("Invalid answer index: {SelectedAnswer} for question with {OptionCount} options",
                        selectedAnswer, currentQuestion.Options.Length);
                    await DisplayQuizError(message, "Invalid answer selection", user);
                    return;
                }

                var isCorrect = currentQuestion.CorrectAnswer == selectedAnswer;
                var correctOption = (char)('A' + currentQuestion.CorrectAnswer);
                var selectedOption = (char)('A' + selectedAnswer);

                var resultText = $"🧠 **Quiz Answer for {user}**\n\n" +
                                $"📄 **{EscapeMarkdown(cachedContent.Title)}**\n\n" +
                                $"**Question {questionIndex + 1}:** {EscapeMarkdown(currentQuestion.Question)}\n\n" +
                                $"**Your Answer:** {selectedOption}. {EscapeMarkdown(currentQuestion.Options[selectedAnswer])}\n" +
                                $"**Correct Answer:** {correctOption}. {EscapeMarkdown(currentQuestion.Options[currentQuestion.CorrectAnswer])}\n\n" +
                                (isCorrect ? "✅ **Correct!**" : "❌ **Incorrect**") + "\n\n" +
                                $"💡 **Explanation:** {EscapeMarkdown(currentQuestion.Explanation)}";

                var nextQuestionIndex = questionIndex + 1;
                var isLastQuestion = nextQuestionIndex >= quizResult.Questions.Length;

                InlineKeyboardMarkup keyboard;
                if (isLastQuestion)
                {
                    keyboard = new InlineKeyboardMarkup(new[]
                    {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🔄 Retry Quiz", $"quiz:{contentId}"),
                    InlineKeyboardButton.WithCallbackData("📝 Get Summary", $"summary:{contentId}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("💾 Save Article", $"save:{contentId}"),
                    InlineKeyboardButton.WithCallbackData("🔙 Main Menu", "main_menu")
                }
            });

                    resultText += "\n\n🎉 **Quiz Completed!**\nGreat job working through all the questions!";
                }
                else
                {
                    keyboard = new InlineKeyboardMarkup(new[]
                    {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("➡️ Next Question", $"next_question:{contentId}:{nextQuestionIndex}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📊 Quiz Summary", $"quiz_summary:{contentId}"),
                    InlineKeyboardButton.WithCallbackData("🔙 Main Menu", "main_menu")
                }
            });
                }

                await _bot.EditMessageText(
                    chatId: message.Chat.Id,
                    messageId: message.MessageId,
                    text: resultText,
                    replyMarkup: keyboard,
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown
                );

                _logger.LogInformation("Quiz answer processed successfully: {User} answered question {QuestionNumber} {Result}",
                    user, questionIndex + 1, isCorrect ? "correctly" : "incorrectly");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling quiz answer for user {User}", user);
                await DisplayQuizError(message, "Error processing quiz answer", user);
            }
        }

        private async Task HandleNextQuestion(Message message, string contentId, int questionIndex, string user)
        {
            try
            {
                _logger.LogDebug("Loading next question - ContentId: {ContentId}, QuestionIndex: {QuestionIndex}",
                    contentId, questionIndex);

                var cachedContent = await _contentCacheService.GetContentAsync(contentId);
                if (cachedContent == null)
                {
                    _logger.LogWarning("Original content not found for ID: {ContentId}", contentId);
                    await DisplayQuizError(message, "Quiz session expired - content not found", user);
                    return;
                }

                var quizResult = await GetQuizByContentId(contentId);
                if (quizResult == null || !quizResult.Success)
                {
                    _logger.LogWarning("Cached quiz not found for content ID: {ContentId}, regenerating...", contentId);

                    quizResult = await GenerateAiQuiz(cachedContent);
                    if (quizResult.Success)
                    {
                        await CacheQuizResult(cachedContent, quizResult);
                    }
                    else
                    {
                        await DisplayQuizError(message, "Failed to load quiz questions", user);
                        return;
                    }
                }

                // Validate question index
                if (questionIndex >= quizResult.Questions.Length)
                {
                    _logger.LogWarning("Invalid question index: {QuestionIndex} for quiz with {QuestionCount} questions",
                        questionIndex, quizResult.Questions.Length);
                    await DisplayQuizError(message, "Question not available", user);
                    return;
                }

                var question = quizResult.Questions[questionIndex];
                var questionText = $"🧠 **Knowledge Quiz for {user}**\n\n" +
                                  $"📄 **{EscapeMarkdown(cachedContent.Title)}**\n\n" +
                                  $"**Question {questionIndex + 1} of {quizResult.Questions.Length}:**\n" +
                                  $"{EscapeMarkdown(question.Question)}\n\n" +
                                  "Choose your answer:";

                var optionButtons = question.Options.Select((option, index) =>
                    new[] { InlineKeyboardButton.WithCallbackData(
                $"{(char)('A' + index)}. {option}",
                $"answer:{contentId}:{questionIndex}:{index}") }
                ).ToArray();

                var controlButtons = new[]
                {
            InlineKeyboardButton.WithCallbackData("📊 Quiz Stats", $"quiz_summary:{contentId}"),
            InlineKeyboardButton.WithCallbackData("🔙 Main Menu", "main_menu")
        };

                var keyboard = new InlineKeyboardMarkup(optionButtons.Append(controlButtons));

                await _bot.EditMessageText(
                    chatId: message.Chat.Id,
                    messageId: message.MessageId,
                    text: questionText,
                    replyMarkup: keyboard,
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown
                );

                _logger.LogInformation("Successfully loaded question {QuestionIndex} for user {User}", questionIndex + 1, user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling next question for user {User}, contentId: {ContentId}", user, contentId);
                await DisplayQuizError(message, "Error loading next question", user);
            }
        }

        private async Task HandleQuizSummary(Message message, string contentId, string user)
        {
            try
            {
                var cachedContent = await _contentCacheService.GetContentAsync(contentId);
                if (cachedContent == null)
                {
                    await DisplayQuizError(message, "Content not found", user);
                    return;
                }

                // TODO: Implement proper quiz statistics tracking
                var summaryText = $"📊 **Quiz Summary for {user}**\n\n" +
                                 $"📄 **{EscapeMarkdown(cachedContent.Title)}**\n\n" +
                                 $"🎯 This quiz tests understanding of key concepts\n" +
                                 $"📝 {cachedContent.WordCount} words of content analyzed\n\n" +
                                 "*🚧 Detailed statistics coming soon with SQLite database!*";

                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🔄 Restart Quiz", $"quiz:{contentId}"),
                        InlineKeyboardButton.WithCallbackData("📝 Get Summary", $"summary:{contentId}")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("💾 Save Article", $"save:{contentId}"),
                        InlineKeyboardButton.WithCallbackData("🔙 Main Menu", "main_menu")
                    }
                });

                await _bot.EditMessageText(
                    chatId: message.Chat.Id,
                    messageId: message.MessageId,
                    text: summaryText,
                    replyMarkup: keyboard,
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing quiz summary for user {User}", user);
                await DisplayQuizError(message, "Error loading quiz summary", user);
            }
        }
        private async Task DisplaySummaryResult(Message message, AISummaryResult summaryResult, ProcessedContent content, string user)
        {
            var summaryText = $"📝 **AI Summary for {user}**\n\n" +
                             $"📄 **{EscapeMarkdown(content.Title)}**\n" +
                             $"📊 *{content.WordCount} words • {EstimateReadingTime(content.WordCount)} min read*\n\n" +
                             $"**🎯 Key Points:**\n{summaryResult.Summary}\n\n" +
                             $"⏱️ *Generated in {summaryResult.ProcessingTimeMs}ms*";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🧠 Create Quiz", $"quiz:{await _contentCacheService.CacheContentAsync(content)}"),
                    InlineKeyboardButton.WithCallbackData("💾 Save Article", $"save:{await _contentCacheService.CacheContentAsync(content)}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🔍 Find Similar", $"similar:{await _contentCacheService.CacheContentAsync(content)}"),
                    InlineKeyboardButton.WithCallbackData("🔙 Main Menu", "main_menu")
                }
            });

            await _bot.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.MessageId,
                text: summaryText,
                replyMarkup: keyboard,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown
            );
        }
        private async Task DisplayQuizResult(Message message, AIQuizResult quizResult, ProcessedContent content, string user)
        {
            if (!quizResult.Questions.Any())
            {
                await DisplayQuizError(message, "No quiz questions generated", user);
                return;
            }

            var firstQuestion = quizResult.Questions.First();
            var questionText = $"🧠 **Knowledge Quiz for {user}**\n\n" +
                              $"📄 **{EscapeMarkdown(content.Title)}**\n\n" +
                              $"**Question 1 of {quizResult.Questions.Length}:**\n" +
                              $"{EscapeMarkdown(firstQuestion.Question)}\n\n" +
                              "Choose your answer:";

            var cachedContentId = await _contentCacheService.CacheContentAsync(content);

            var optionButtons = firstQuestion.Options.Select((option, index) =>
                new[] { InlineKeyboardButton.WithCallbackData($"{(char)('A' + index)}. {option}", $"answer:{cachedContentId}:0:{index}") }
            ).ToArray();

            var controlButtons = new[]
            {
                InlineKeyboardButton.WithCallbackData("📊 Quiz Stats", $"quiz_stats:{cachedContentId}"),
                InlineKeyboardButton.WithCallbackData("🔙 Main Menu", "main_menu")
            };

            var keyboard = new InlineKeyboardMarkup(optionButtons.Append(controlButtons));

            await _bot.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.MessageId,
                text: questionText,
                replyMarkup: keyboard,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown
            );
        }
        private async Task DisplaySummaryError(Message message, string error, string user)
        {
            await _bot.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.MessageId,
                text: $"❌ **Summary Generation Failed**\n\n" +
                      $"Sorry {user}, I couldn't generate a summary:\n" +
                      $"**Error:** {error}\n\n" +
                      "💡 **Try:**\n" +
                      "• Check if the content is suitable for summarization\n" +
                      "• Try again in a few moments\n" +
                      "• Share different content",
                replyMarkup: GetMainMenuKeyboard()
            );
        }
        private async Task DisplayQuizError(Message message, string error, string user)
        {
            await _bot.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.MessageId,
                text: $"❌ **Quiz Generation Failed**\n\n" +
                      $"Sorry {user}, I couldn't create a quiz:\n" +
                      $"**Error:** {error}\n\n" +
                      "💡 **Try:**\n" +
                      "• Ensure content has sufficient educational material\n" +
                      "• Try again in a few moments\n" +
                      "• Share more detailed content",
                replyMarkup: GetMainMenuKeyboard()
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
        private static InlineKeyboardMarkup CreateContentActionKeyboard(string contentId, ContentSourceType sourceType)
        {
            var baseActions = new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📝 Generate Summary", $"summary:{contentId}"),
                    InlineKeyboardButton.WithCallbackData("🧠 Create Quiz", $"quiz:{contentId}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("💾 Save for Later", $"save:{contentId}"),
                    InlineKeyboardButton.WithCallbackData("🔍 Find Similar", $"similar:{contentId}")
                }
            };
            var actions = sourceType switch
            {
                ContentSourceType.Url => baseActions.Append(new[]
                {
            InlineKeyboardButton.WithCallbackData("🔗 View Original", $"original:{contentId}"),
            InlineKeyboardButton.WithCallbackData("📋 Copy Text", $"copy:{contentId}")
                }).ToArray(),

                ContentSourceType.Document => baseActions.Append(new[]
                {
            InlineKeyboardButton.WithCallbackData("📄 Document Info", $"docinfo:{contentId}"),
            InlineKeyboardButton.WithCallbackData("📋 Extract Text", $"extract:{contentId}")
                }).ToArray(),

                _ => baseActions
            };

            return new InlineKeyboardMarkup(actions);
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

        private static string GetContentTypeDescription(ContentSourceType contentType) => contentType switch
        {
            ContentSourceType.Url => "web article",
            ContentSourceType.PlainText => "text content",
            ContentSourceType.Document => "document",
            ContentSourceType.PastedContent => "pasted content",
            _ => "content"
        };
        private static string GetContentProcessingTips(ContentSourceType contentType) => contentType switch
        {
            ContentSourceType.Url => "💡 **Tips for better URL processing:**\n" +
                                     "• Try copying the article text directly\n" +
                                     "• Use reader mode if available\n" +
                                     "• Some sites block automated access",

            ContentSourceType.PlainText => "💡 **For text analysis:**\n" +
                                          "• Paste longer text (200+ words works best)\n" +
                                          "• Include the main content, not just excerpts",

            ContentSourceType.Document => "💡 **Supported documents:**\n" +
                                         "• .txt, .pdf, .docx, .md files\n" +
                                         "• Max 10MB file size",

            _ => "💡 Try pasting the article text directly for best results."
        };
        private static string FormatContentPreview(ProcessedContent content, string previewText)
        {
            var title = !string.IsNullOrEmpty(content.Title) ? content.Title : "Content Analysis";
            var sourceInfo = GetSourceInfo(content);
            var readingTime = EstimateReadingTime(content.WordCount);

            return $"📄 **{EscapeMarkdown(title)}**\n\n" +
                   $"📊 *{content.WordCount} words • ~{readingTime} min read* {sourceInfo}\n\n" +
                   $"{EscapeMarkdown(previewText)}\n\n" +
                   "🎯 **What would you like to do with this content?**";
        }
        private static string GetSourceInfo(ProcessedContent content)
        {
            return content.SourceType switch
            {
                ContentSourceType.Url when content.SourceUrl != null =>
                    $"• 🌐 {GetDomainFromUrl(content.SourceUrl)}",
                ContentSourceType.Document =>
                    $"• 📄 {content.Metadata.ProcessorUsed ?? "Document"}",
                ContentSourceType.PlainText =>
                    $"• 📝 Text Analysis",
                _ => ""
            };
        }
        private static string GetDomainFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                return uri.Host.StartsWith("www.") ? uri.Host.Substring(4) : uri.Host;
            }
            catch
            {
                return "External Source";
            }
        }
        private static string EscapeMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var markdownChars = new[] { '*', '_', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' };

            foreach (var ch in markdownChars)
            {
                text = text.Replace(ch.ToString(), $"\\{ch}");
            }

            return text;
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
        #region Helper Methods for AI Processing

        private async Task<AISummaryResult?> CheckForExistingSummary(ProcessedContent content)
        {
            // TODO: Implement summary caching with database
            return null;
        }


        private async Task<AIQuizResult?> CheckForExistingQuiz(ProcessedContent content)
        {
            try
            {
                var contentHash = GenerateContentHash(content.Content);
                var quizCacheKey = $"quiz_{contentHash}";

                _logger.LogDebug("Checking for cached quiz with key: {CacheKey}", quizCacheKey);

                var allCachedItems = await GetCachedQuizByHash(contentHash);
                if (allCachedItems != null)
                {
                    _logger.LogInformation("Found cached quiz with {QuestionCount} questions", allCachedItems.Questions?.Length ?? 0);
                    return allCachedItems;
                }

                _logger.LogDebug("No valid cached quiz found for hash: {ContentHash}", contentHash);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for existing quiz cache");
                return null;
            }
        }

        private async Task CacheSummaryResult(ProcessedContent content, AISummaryResult summary)
        {
            try
            {
                var summaryCacheKey = $"summary_{GenerateContentHash(content.Content)}";
                var summaryJson = System.Text.Json.JsonSerializer.Serialize(summary);

                var cacheContent = new ProcessedContent
                {
                    Title = $"Summary Cache - {content.Title}",
                    Content = summaryJson,
                    SourceType = ContentSourceType.PlainText,
                    WordCount = summaryJson.Length / 5,
                    Success = true,
                    ProcessedAt = DateTime.UtcNow
                };

                await _contentCacheService.CacheContentAsync(cacheContent);
                _logger.LogDebug("Cached summary result for content: {Title}", content.Title);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error caching summary result");
            }
        }
        private async Task<AIQuizResult?> GetCachedQuizByHash(string contentHash)
        {
            try
            {
                var deterministicQuizId = $"QUIZ{contentHash}";

                var cachedQuizContent = await _contentCacheService.GetContentAsync(deterministicQuizId);

                if (cachedQuizContent != null && !string.IsNullOrEmpty(cachedQuizContent.Content))
                {
                    try
                    {
                        var quizResult = System.Text.Json.JsonSerializer.Deserialize<AIQuizResult>(cachedQuizContent.Content);
                        if (quizResult?.Success == true && quizResult.Questions?.Length > 0)
                        {
                            return quizResult;
                        }
                    }
                    catch (System.Text.Json.JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to deserialize cached quiz");
                        await _contentCacheService.RemoveAsync(deterministicQuizId);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving cached quiz by hash: {ContentHash}", contentHash);
                return null;
            }
        }

        private async Task CacheQuizResult(ProcessedContent content, AIQuizResult quiz)
        {
            try
            {
                var contentHash = GenerateContentHash(content.Content);
                var deterministicQuizId = $"QUIZ{contentHash}";

                var quizJson = System.Text.Json.JsonSerializer.Serialize(quiz, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = false
                });

                var cacheContent = new ProcessedContent
                {
                    Title = $"Quiz Cache - {content.Title}",
                    Content = quizJson,
                    SourceType = ContentSourceType.PlainText,
                    WordCount = quiz.Questions?.Length ?? 0,
                    Success = true,
                    ProcessedAt = DateTime.UtcNow,
                    Metadata = new ProcessingMetadata
                    {
                        ProcessorUsed = "QuizCache",
                        Domain = contentHash
                    }
                };

                await CacheWithDeterministicId(cacheContent, deterministicQuizId);

                _logger.LogInformation("Successfully cached quiz with deterministic ID: {QuizId}, Questions: {QuestionCount}",
                    deterministicQuizId, quiz.Questions?.Length ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error caching quiz result for content: {Title}", content.Title);
            }
        }

        private async Task CacheWithDeterministicId(ProcessedContent content, string deterministicId)
        {
            var cachedId = await _contentCacheService.CacheContentAsync(content);
            _logger.LogDebug("Cached quiz content with generated ID: {GeneratedId}, wanted: {DeterministicId}",
                cachedId, deterministicId);
        }


        private static string GenerateContentHash(string content)
        {
            if (string.IsNullOrEmpty(content))
                return "empty";

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content.Trim()));
            return Convert.ToBase64String(hash)[..16];
        }

        private async Task<AIQuizResult?> GetQuizByContentId(string contentId)
        {
            try
            {
                var content = await _contentCacheService.GetContentAsync(contentId);
                if (content == null)
                {
                    _logger.LogWarning("Content not found for ID: {ContentId}", contentId);
                    return null;
                }

                var contentHash = GenerateContentHash(content.Content);

                var cacheAttempts = new[]
                {
            $"quiz_{contentHash}",
            $"QUIZ{contentHash}",
            contentHash
        };

                foreach (var cacheKey in cacheAttempts)
                {
                    try
                    {
                        var cachedContent = await _contentCacheService.GetContentAsync(cacheKey);
                        if (cachedContent?.Content != null && cachedContent.Metadata?.ProcessorUsed == "QuizCache")
                        {
                            var quizResult = System.Text.Json.JsonSerializer.Deserialize<AIQuizResult>(cachedContent.Content);
                            if (quizResult?.Success == true && quizResult.Questions?.Length > 0)
                            {
                                _logger.LogInformation("Found cached quiz using key: {CacheKey}", cacheKey);
                                return quizResult;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Cache lookup failed for key: {CacheKey}", cacheKey);
                    }
                }

                _logger.LogDebug("No cached quiz found for content ID: {ContentId}", contentId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving quiz for content ID: {ContentId}", contentId);
                return null;
            }
        }
        #endregion
    }

}