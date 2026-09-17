using PermixaApp.Application.Authorization;
using PermixaApp.Application.Reference.SampleNotes;
using Permixa.AspNetCore.Authorization;

namespace PermixaApp.Api.Endpoints;

public static class SampleEndpoints
{
    public static IEndpointRouteBuilder MapSampleEndpoints(this IEndpointRouteBuilder app)
    {
        // REFERENCE / SAFE TO DELETE — demonstrates RequirePermission after app permission seed.
        app.MapGet("/sample/protected", () =>
                Results.Ok(new { message = "SampleNotes.Read granted.", reference = true }))
            .RequireAuthorization()
            .RequirePermission(AppPermissions.SampleNotes.Read);

        var notes = app.MapGroup("/sample/notes");
        notes.MapPost("/", async (CreateSampleNoteRequest request, CreateSampleNoteUseCase useCase) =>
            {
                var created = await useCase.ExecuteAsync(request);
                return Results.Created($"/sample/notes/{created.Id}", created);
            })
            .RequireAuthorization()
            .RequirePermission(AppPermissions.SampleNotes.Write);

        notes.MapGet("/{id:guid}", async (Guid id, GetSampleNoteByIdUseCase useCase) =>
            {
                var note = await useCase.ExecuteAsync(id);
                return note is null ? Results.NotFound() : Results.Ok(note);
            })
            .RequireAuthorization()
            .RequirePermission(AppPermissions.SampleNotes.Read);

        notes.MapGet("/", async (ListSampleNotesUseCase useCase) =>
            {
                var list = await useCase.ExecuteAsync();
                return Results.Ok(list);
            })
            .RequireAuthorization()
            .RequirePermission(AppPermissions.SampleNotes.Read);

        return app;
    }
}
