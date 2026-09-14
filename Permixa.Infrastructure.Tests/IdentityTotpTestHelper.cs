using System.Net;
using System.Security.Cryptography;
using Permixa.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Permixa.Infrastructure.Tests;

/// <summary>
/// Test-only TOTP matching ASP.NET Core Identity's authenticator provider.
/// Identity's AuthenticatorTokenProvider.GenerateAsync returns empty.
/// </summary>
internal static class IdentityTotpTestHelper
{
    private const string Base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static async Task<string> GenerateAsync(UserManager<ApplicationUser> users, Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        var key = await users.GetAuthenticatorKeyAsync(user!);
        Assert.False(string.IsNullOrWhiteSpace(key));
        return Compute(key!);
    }

    internal static string Compute(string base32Key)
    {
        var keyBytes = FromBase32(base32Key);
        var timestep = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var timestepBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((long)timestep));
        var hash = HMACSHA1.HashData(keyBytes, timestepBytes);
        var offset = hash[^1] & 0xf;
        var binary = ((hash[offset] & 0x7f) << 24)
                     | (hash[offset + 1] << 16)
                     | (hash[offset + 2] << 8)
                     | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] FromBase32(string input)
    {
        var trimmed = input.AsSpan().TrimEnd('=');
        if (trimmed.Length == 0)
            return Array.Empty<byte>();

        var output = new byte[trimmed.Length * 5 / 8];
        var bitIndex = 0;
        var inputIndex = 0;
        var outputBits = 0;
        var outputIndex = 0;
        while (outputIndex < output.Length)
        {
            var byteIndex = Base32Chars.IndexOf(char.ToUpperInvariant(trimmed[inputIndex]));
            if (byteIndex < 0)
                throw new FormatException("Authenticator key is not valid Base32.");

            var bits = Math.Min(5 - bitIndex, 8 - outputBits);
            output[outputIndex] <<= bits;
            output[outputIndex] |= (byte)(byteIndex >> (5 - (bitIndex + bits)));

            bitIndex += bits;
            if (bitIndex >= 5)
            {
                inputIndex++;
                bitIndex = 0;
            }

            outputBits += bits;
            if (outputBits >= 8)
            {
                outputIndex++;
                outputBits = 0;
            }
        }

        return output;
    }
}
