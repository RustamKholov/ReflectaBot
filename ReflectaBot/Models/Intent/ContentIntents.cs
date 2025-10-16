using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReflectaBot.Models.Intent
{
    public static class ContentIntents
    {
        public const string ProcessUrl = "process_url";
        public const string GetSummary = "get_summary";
        public const string CreateQuiz = "create_quiz";
        public const string SaveForLater = "save_for_later";


        public const string StudyFlashcards = "study_flashcards";
        public const string ReviewDue = "review_due";
        public const string TakeQuiz = "take_quiz";
        public const string GetProgress = "get_progress";


        public const string BrowseArticles = "browse_articles";
        public const string ShareArticle = "share_article";
        public const string FindSimilar = "find_similar";
        public const string GetRecommendations = "get_recommendations";


        public const string SetPreferences = "set_preferences";
        public const string GetHelp = "get_help";
        public const string GetStats = "get_stats";

        public const string Greeting = "greeting";
        public const string None = "none";

        public static readonly string[] AllIntents = {
        ProcessUrl, GetSummary, CreateQuiz, SaveForLater,
        StudyFlashcards, ReviewDue, TakeQuiz, GetProgress,
        BrowseArticles, ShareArticle, FindSimilar, GetRecommendations,
        SetPreferences, GetHelp, GetStats, Greeting, None
        };

        public static readonly Dictionary<string, IntentDefinition> Definitions = new()
        {
            [ProcessUrl] = new IntentDefinition
            {
                Intent = ProcessUrl,
                Description = "User wants to process, analyze, or learn from a URL/article",
                Examples = new List<string>
                {
                    "Can you summarize this article?",
                    "https://example.com/article",
                    "I found this interesting link",
                    "Process this URL for me",
                    "What's this article about?",
                    "Make a quiz from this link",
                    "Turn this into flashcards"
                }
            },

            [GetSummary] = new IntentDefinition
            {
                Intent = GetSummary,
                Description = "User wants a summary of previously processed content",
                Examples = new List<string>
                {
                    "Show me the summary",
                    "What were the key points?",
                    "Give me the main ideas",
                    "Summarize that article",
                    "What did it say?",
                    "Can I get a summary?",
                    "Main takeaways please"
                }
            },

            [CreateQuiz] = new IntentDefinition
            {
                Intent = CreateQuiz,
                Description = "User wants to create or take a quiz based on content",
                Examples = new List<string>
                {
                    "Make a quiz from this",
                    "Test my knowledge",
                    "Create questions",
                    "I want to take a quiz",
                    "Generate practice questions",
                    "Quiz me on this topic",
                    "Test what I learned"
                }
            },

            [StudyFlashcards] = new IntentDefinition
            {
                Intent = StudyFlashcards,
                Description = "User wants to study using flashcards or spaced repetition",
                Examples = new List<string>
                {
                    "Start studying",
                    "Review my flashcards",
                    "Time to study",
                    "Show me my cards",
                    "Practice session",
                    "Study mode",
                    "Review what I learned"
                }
            },

            [ReviewDue] = new IntentDefinition
            {
                Intent = ReviewDue,
                Description = "User wants to see what's due for review or schedule study",
                Examples = new List<string>
                {
                    "What's due for review?",
                    "Do I have anything to study?",
                    "Check my progress",
                    "What should I review today?",
                    "Any cards due?",
                    "Study reminder",
                    "What's next?"
                }
            },

            [GetProgress] = new IntentDefinition
            {
                Intent = GetProgress,
                Description = "User wants to see their learning progress and statistics",
                Examples = new List<string>
                {
                    "Show my progress",
                    "How am I doing?",
                    "My learning stats",
                    "Progress report",
                    "How much have I learned?",
                    "Study statistics",
                    "My achievements"
                }
            },

            [GetHelp] = new IntentDefinition
            {
                Intent = GetHelp,
                Description = "User needs help or wants to know available commands",
                Examples = new List<string>
                {
                    "Help",
                    "/help",
                    "What can you do?",
                    "How does this work?",
                    "Commands",
                    "How to use this bot?",
                    "I need assistance",
                    "Show me options"
                }
            },

            [Greeting] = new IntentDefinition
            {
                Intent = Greeting,
                Description = "User is greeting the bot or starting conversation",
                Examples = new List<string>
                {
                    "Hello",
                    "Hi",
                    "Hey there",
                    "/start",
                    "Good morning",
                    "What's up?",
                    "Hi bot"
                }
            },

            [None] = new IntentDefinition
            {
                Intent = None,
                Description = "No clear intent detected or unrecognized input",
                Examples = new List<string>
                {
                    "Random text",
                    "Unclear message",
                    "Gibberish input"
                }
            }
        };

        public static List<IntentDefinition> GetAllDefinitions()
        {
            return Definitions.Values.ToList();
        }

        public static bool IsUrlProcessingIntent(string intent)
        {
            return intent == ProcessUrl || intent == GetSummary ||
                   intent == CreateQuiz || intent == SaveForLater;
        }

        public static bool IsLearningIntent(string intent)
        {
            return intent == StudyFlashcards || intent == ReviewDue ||
                   intent == TakeQuiz || intent == GetProgress;
        }

    }
}