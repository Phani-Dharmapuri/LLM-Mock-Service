using LlmMockService.Core.Configuration;
using LlmMockService.Core.Deployments;

namespace LlmMockService.Server.Admin;

/// <summary>Runtime control for test scripts: inspect quotas, inject outages, reset between runs.</summary>
internal static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/_admin");

        admin.MapGet("/deployments", (DeploymentCatalog catalog) => TypedResults.Ok(catalog.Describe()));

        admin.MapPut("/deployments/{name}/faults", (string name, FaultOptions faults, DeploymentCatalog catalog) =>
        {
            var errors = LlmMockOptionsValidator.ValidateFaults(faults);
            if (errors.Count > 0)
            {
                return Results.Problem(string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest, title: "Invalid fault options");
            }

            catalog.SetFaultOverride(name, faults);
            return Results.NoContent();
        });

        admin.MapDelete("/deployments/{name}/faults", (string name, DeploymentCatalog catalog) =>
            catalog.ClearFaultOverride(name) ? Results.NoContent() : Results.NotFound());

        admin.MapPost("/reset", (DeploymentCatalog catalog) =>
        {
            catalog.Reset();
            return Results.NoContent();
        });
    }
}
