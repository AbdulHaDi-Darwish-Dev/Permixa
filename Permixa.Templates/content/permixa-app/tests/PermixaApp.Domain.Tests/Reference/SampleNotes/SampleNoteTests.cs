using PermixaApp.Domain.Reference.SampleNotes;
using Xunit;

namespace PermixaApp.Domain.Tests.Reference.SampleNotes;

/// <summary>REFERENCE / SAFE TO DELETE</summary>
public sealed class SampleNoteTests
{
    [Fact]
    public void Create_RequiresTitle()
    {
        Assert.Throws<ArgumentException>(() =>
            SampleNote.Create("  ", "body", DateTime.UtcNow));
    }

    [Fact]
    public void Create_TrimsTitleAndBody()
    {
        var note = SampleNote.Create("  Hello  ", "  World  ", DateTime.UtcNow);
        Assert.Equal("Hello", note.Title);
        Assert.Equal("World", note.Body);
        Assert.NotEqual(Guid.Empty, note.Id);
    }
}
