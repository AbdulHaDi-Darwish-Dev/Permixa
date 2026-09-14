namespace Permixa.AspNetCore.RateLimiting;

internal static class PermixaRateLimitingValidation
{
    public static void ValidateFixedWindow(string policyName, FixedWindowRateLimitPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureKnownPartition(policyName, options.Partition);

        if (options.PermitLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.PermitLimit),
                $"Fixed Window policy '{policyName}' requires PermitLimit > 0.");
        }

        if (options.Window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.Window),
                $"Fixed Window policy '{policyName}' requires Window > TimeSpan.Zero.");
        }

        if (options.QueueLimit < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.QueueLimit),
                $"Fixed Window policy '{policyName}' requires QueueLimit >= 0.");
        }
    }

    public static void ValidateSlidingWindow(string policyName, SlidingWindowRateLimitPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureKnownPartition(policyName, options.Partition);

        if (options.PermitLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.PermitLimit),
                $"Sliding Window policy '{policyName}' requires PermitLimit > 0.");
        }

        if (options.Window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.Window),
                $"Sliding Window policy '{policyName}' requires Window > TimeSpan.Zero.");
        }

        if (options.SegmentsPerWindow <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.SegmentsPerWindow),
                $"Sliding Window policy '{policyName}' requires SegmentsPerWindow > 0.");
        }

        if (options.QueueLimit < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.QueueLimit),
                $"Sliding Window policy '{policyName}' requires QueueLimit >= 0.");
        }
    }

    public static void ValidateTokenBucket(string policyName, TokenBucketRateLimitPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureKnownPartition(policyName, options.Partition);

        if (options.TokenLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.TokenLimit),
                $"Token Bucket policy '{policyName}' requires TokenLimit > 0.");
        }

        if (options.TokensPerPeriod <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.TokensPerPeriod),
                $"Token Bucket policy '{policyName}' requires TokensPerPeriod > 0.");
        }

        if (options.ReplenishmentPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.ReplenishmentPeriod),
                $"Token Bucket policy '{policyName}' requires ReplenishmentPeriod > TimeSpan.Zero.");
        }

        if (options.QueueLimit < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.QueueLimit),
                $"Token Bucket policy '{policyName}' requires QueueLimit >= 0.");
        }
    }

    public static void ValidateConcurrency(string policyName, ConcurrencyRateLimitPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureKnownPartition(policyName, options.Partition);

        if (options.PermitLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.PermitLimit),
                $"Concurrency policy '{policyName}' requires PermitLimit > 0.");
        }

        if (options.QueueLimit < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.QueueLimit),
                $"Concurrency policy '{policyName}' requires QueueLimit >= 0.");
        }
    }

    private static void EnsureKnownPartition(string policyName, PermixaRateLimitPartitionKind partition)
    {
        if (!Enum.IsDefined(partition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(partition),
                $"Policy '{policyName}' has an unsupported partition kind '{partition}'.");
        }
    }
}
