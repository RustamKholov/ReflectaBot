using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReflectaBot.Interfaces;
using ReflectaBot.Models.Content;

namespace ReflectaBot.Services
{
    public class DocumentProcessor : IDocumentProcessor
    {
        public Task<ProcessedContent> ProcessDocumentAsync(string text)
        {
            throw new NotImplementedException();
        }
    }
}