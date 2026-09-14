using System.Collections.Concurrent;
using Permixa.Infrastructure.Email;
using Microsoft.AspNetCore.WebUtilities;

namespace Permixa.IntegrationTests.Support;

public sealed class CapturedEmailStore
{
    public ConcurrentQueue<EmailOutgoingMessage> Messages { get; } = new();

    public EmailOutgoingMessage Last =>
        Messages.LastOrDefault()
        ?? throw new InvalidOperationException("No email was captured.");

    public void Add(EmailOutgoingMessage message) => Messages.Enqueue(message);

    public string ExtractOtp()
    {
        var text = Last.TextBody;
        const string marker = "Use this verification code to confirm your email address:";
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            throw new InvalidOperationException("OTP marker was not found in the captured text body.");

        var after = text[(index + marker.Length)..];
        var line = after
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);

        if (string.IsNullOrWhiteSpace(line))
            throw new InvalidOperationException("OTP value was not found in the captured text body.");

        return line;
    }

    public (Guid ChallengeId, string Token, string Url) ExtractLink(string urlMarker)
    {
        var text = Last.TextBody;
        var index = text.IndexOf(urlMarker, StringComparison.Ordinal);
        if (index < 0)
            throw new InvalidOperationException($"URL marker '{urlMarker}' was not found in the captured text body.");

        var after = text[(index + urlMarker.Length)..];
        var url = after
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("http", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Verification URL was not found in the captured text body.");

        var uri = new Uri(url);
        var query = QueryHelpers.ParseQuery(uri.Query);
        if (!query.TryGetValue("challengeId", out var challengeValues)
            || !Guid.TryParse(challengeValues.ToString(), out var challengeId))
        {
            throw new InvalidOperationException("challengeId was missing from the captured URL.");
        }

        if (!query.TryGetValue("token", out var tokenValues))
            throw new InvalidOperationException("token was missing from the captured URL.");

        return (challengeId, tokenValues.ToString(), url);
    }
}

internal sealed class CapturingEmailSender : IEmailSender
{
    private readonly CapturedEmailStore _store;

    public CapturingEmailSender(CapturedEmailStore store)
    {
        _store = store;
    }

    public Task SendAsync(EmailOutgoingMessage message, CancellationToken cancellationToken = default)
    {
        _store.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingEmailSender : IEmailSender
{
    public Task SendAsync(EmailOutgoingMessage message, CancellationToken cancellationToken = default) =>
        throw new EmailDeliveryException("Test email sender refused delivery.");
}
