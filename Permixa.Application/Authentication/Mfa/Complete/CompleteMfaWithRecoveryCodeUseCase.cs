using Permixa.Application.Authentication.Models;
using Permixa.Application.Common.Results;

namespace Permixa.Application.Authentication.Mfa.Complete;

public sealed class CompleteMfaWithRecoveryCodeUseCase
{
    private readonly MfaLoginCompletion _completion;

    public CompleteMfaWithRecoveryCodeUseCase(MfaLoginCompletion completion)
    {
        _completion = completion;
    }

    public async Task<Result<AuthenticationResult>> ExecuteAsync(
        CompleteMfaWithRecoveryCodeRequest request,
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

        var code = request.RecoveryCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            var empty = await _completion.RegisterInvalidCodeAsync(challenge, cancellationToken);
            return Result.Failure<AuthenticationResult>(empty.Error!);
        }

        var completed = await _completion.RedeemConsumeAndIssueAsync(challenge, code, cancellationToken);
        if (completed.IsFailure && completed.Error == MfaErrors.CodeInvalid)
            return Result.Failure<AuthenticationResult>(
                (await _completion.RegisterInvalidCodeAsync(challenge, cancellationToken)).Error!);

        return completed;
    }
}
