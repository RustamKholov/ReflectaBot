using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReflectaBot.Services.Intent
{
    public interface IIntentRouter
    {
        Task<(string Intent, double Score)> RouteAsync(string userText, CancellationToken ct = default);
    }
}