using Moq;
using PermixaApp.Application.Abstractions;
using PermixaApp.Application.Reference.SampleNotes;
using PermixaApp.Domain.Reference.SampleNotes;
using Xunit;

namespace PermixaApp.Application.Tests.Reference.SampleNotes;

/// <summary>REFERENCE / SAFE TO DELETE</summary>
public sealed class CreateSampleNoteUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_PersistsNote()
    {
        SampleNote? saved = null;
        var repo = new Mock<ISampleNoteRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<SampleNote>(), It.IsAny<CancellationToken>()))
            .Callback<SampleNote, CancellationToken>((n, _) => saved = n)
            .Returns(Task.CompletedTask);

        var uow = new Mock<IAppUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var clock = new Mock<IAppClock>();
        var now = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        clock.SetupGet(c => c.UtcNow).Returns(now);

        var useCase = new CreateSampleNoteUseCase(repo.Object, uow.Object, clock.Object);
        var dto = await useCase.ExecuteAsync(new CreateSampleNoteRequest
        {
            Title = "Note",
            Body = "Body"
        });

        Assert.Equal("Note", dto.Title);
        Assert.Equal("Body", dto.Body);
        Assert.Equal(now, dto.CreatedAtUtc);
        Assert.NotNull(saved);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
