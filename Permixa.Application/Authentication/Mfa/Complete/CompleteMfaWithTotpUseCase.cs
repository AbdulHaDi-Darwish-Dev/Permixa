using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Common.Results;

namespace Permixa.Application.Authentication.Mfa.Complete;

public sealed class CompleteMfaWithTotpUseCase
{
    private readonly MfaLoginCompletion _completion;
    private readonly IIdentityMfa _mfa;

    public CompleteMfaWithTotpUseCase(
        MfaLoginCompletion completion,
        IIdentityMfa mfa)
    {
        _completion = completion;
        _mfa = mfa;
    }

    public async Task<Result<AuthenticationResult>> ExecuteAsync(
        CompleteMfaWithTotpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolved = await _completion.ResolveActiveAsync(request.MfaProof, cancellationToken);
        if (resolved.IsFailure)
            return Result.Failure<AuthenticationResult>(resolved.Error!);

        var challenge = resolved.Value;
        var gate = await _completion.EnsureIssuableAsync(challenge, cancellationToken);
        if (gate is not null)
            return Result.Failure<AuthenticationResult>(gate.Error!);

        var code = MfaLoginCompletion.NormalizeTotp(request.TotpCode);
        if (string.IsNullOrWhiteSpace(code)
            || !await _mfa.VerifyAuthenticatorCodeAsync(challenge.UserId, code, cancellationToken))
        {
            var failed = await _completion.RegisterInvalidCodeAsync(challenge, cancellationToken);
            return Result.Failure<AuthenticationResult>(failed.Error!);
        }

        return await _completion.ConsumeAndIssueAsync(challenge, cancellationToken);
    }
}
