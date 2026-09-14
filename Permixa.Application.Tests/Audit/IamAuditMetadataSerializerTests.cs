using Permixa.Application.Audit;
using Permixa.Domain.Audit;

namespace Permixa.Application.Tests.Audit;

public sealed class IamAuditMetadataSerializerTests
{
    [Fact]
    public void SerializeDeserialize_RoundTripsStringDictionary()
    {
        var metadata = new Dictionary<string, string>
        {
            ["OldRoleLevel"] = "10",
            ["NewRoleLevel"] = "11",
            ["Placement"] = "Below"
        };

        var json = IamAuditMetadataSerializer.Serialize(metadata);
        Assert.NotNull(json);

        var restored = IamAuditMetadataSerializer.Deserialize(json);
        Assert.NotNull(restored);
        Assert.Equal(metadata, restored);
    }

    [Fact]
    public void Serialize_EmptyOrNull_ReturnsNull()
    {
        Assert.Null(IamAuditMetadataSerializer.Serialize(null));
        Assert.Null(IamAuditMetadataSerializer.Deserialize(null));
        Assert.Null(IamAuditMetadataSerializer.Serialize(new Dictionary<string, string>()));
    }

    [Fact]
    public void Serialize_OversizedMetadata_Throws()
    {
        var huge = new Dictionary<string, string>
        {
            ["Payload"] = new string('x', IamAuditLog.MaxMetadataJsonLength)
        };

        var ex = Assert.Throws<InvalidOperationException>(() => IamAuditMetadataSerializer.Serialize(huge));
        Assert.Contains(IamAuditLog.MaxMetadataJsonLength.ToString(), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OutcomeMapping_RoundTrips()
    {
        Assert.Equal("Success", IamAuditMetadataSerializer.ToPersistedOutcome(IamAuditOutcome.Success));
        Assert.Equal(IamAuditOutcome.Success, IamAuditMetadataSerializer.ParseOutcome("Success"));
        Assert.Equal(IamAuditOutcome.Failure, IamAuditMetadataSerializer.ParseOutcome("Failure"));
    }
}
