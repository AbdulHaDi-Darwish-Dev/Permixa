namespace PermixaApp.Application.Authorization;

/// <summary>
/// Application-owned permission catalog (not Permixa IAM permissions).
/// </summary>
public static class AppPermissions
{
    public static class SampleNotes
    {
        public const string Read = "SampleNotes.Read";
        public const string Write = "SampleNotes.Write";
    }

    public static IReadOnlyList<string> All { get; } =
    [
        SampleNotes.Read,
        SampleNotes.Write
    ];
}
