using System.Text.RegularExpressions;

namespace Permixa.Infrastructure.Tests;

public sealed class SensitiveLoggingReviewTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static readonly (string RelativePath, string[] ForbiddenInTemplates)[] KnownLogSites =
    [
        (
            "Permixa.Infrastructure/Email/EmailVerificationDispatcher.cs",
            ["Password", "RefreshToken", "MfaProof", "Totp", "RecoveryCode", "Secret", "TokenHash"]
        ),
        (
            "Permixa.Email.Resend/ResendEmailSender.cs",
            ["Password", "RefreshToken", "MfaProof", "Totp", "RecoveryCode", "Secret", "TokenHash"]
        ),
        (
            "Permixa.Caching.Redis/RedisPermissionCache.cs",
            ["Password", "RefreshToken", "MfaProof", "Totp", "RecoveryCode", "Secret", "TokenHash"]
        ),
        (
            "Permixa.AspNetCore/Authorization/PermissionAuthorizationHandler.cs",
            ["Password", "RefreshToken", "MfaProof", "Totp", "RecoveryCode", "Secret", "TokenHash"]
        ),
        (
            "Permixa.AspNetCore/Exceptions/PermixaExceptionHandler.cs",
            ["Password", "RefreshToken", "MfaProof", "Totp", "RecoveryCode", "Secret", "TokenHash"]
        )
    ];

    [Fact]
    public void KnownProductionLogSites_DoNotReferenceSensitiveTokensInMessageTemplates()
    {
        var logStringPattern = new Regex(
            @"Log(?:Information|Warning|Error|Debug|Trace)\(\s*""([^""]+)""",
            RegexOptions.Compiled);

        foreach (var (relativePath, forbidden) in KnownLogSites)
        {
            var fullPath = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(fullPath), $"Expected production file at {fullPath}");

            var source = File.ReadAllText(fullPath);
            foreach (Match match in logStringPattern.Matches(source))
            {
                var template = match.Groups[1].Value;
                foreach (var word in forbidden)
                {
                    Assert.DoesNotContain(
                        word,
                        template,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }
}
