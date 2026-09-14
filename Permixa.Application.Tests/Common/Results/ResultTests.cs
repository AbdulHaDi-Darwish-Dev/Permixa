using Permixa.Application.Common.Results;

namespace Permixa.Application.Tests.Common.Results;

public sealed class ResultTests
{
    [Fact]
    public void Success_HasNoError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_ContainsError()
    {
        var error = Error.NotFound("Identity.UserNotFound", "User was not found.");

        var result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void Failure_NullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
    }

    [Fact]
    public void GenericSuccess_ReturnsValue()
    {
        var result = Result<string>.Success("ok");

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void GenericFailure_DoesNotExposeValue()
    {
        var error = Error.Unauthorized("Identity.InvalidCredentials", "Invalid credentials.");
        var result = Result<string>.Failure(error);

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void ResultFactory_SuccessAndFailure_WorkForGeneric()
    {
        var success = Result.Success(42);
        var failure = Result.Failure<int>(ApplicationErrors.ValidationFailed);

        Assert.Equal(42, success.Value);
        Assert.Equal(ApplicationErrors.ValidationFailed, failure.Error);
    }
}

public sealed class ErrorTests
{
    [Fact]
    public void Error_Equality_IsByCodeDescriptionAndType()
    {
        var left = Error.Conflict("Authorization.OverrideUnchanged", "Override already has that effect.");
        var right = Error.Conflict("Authorization.OverrideUnchanged", "Override already has that effect.");
        var different = Error.NotFound("Authorization.OverrideUnchanged", "Override already has that effect.");

        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.NotEqual(left, different);
        Assert.True(left != different);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Error_RejectsEmptyCodeOrDescription()
    {
        Assert.Throws<ArgumentException>(() => new Error(" ", "desc", ErrorType.Failure));
        Assert.Throws<ArgumentException>(() => new Error("code", " ", ErrorType.Failure));
    }
}
