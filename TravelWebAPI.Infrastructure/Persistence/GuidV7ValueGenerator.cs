using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace TravelWebAPI.Infrastructure.Persistence;

/// <summary>Creates time-ordered (version 7) GUIDs in the app, which keeps primary key indexes compact.</summary>
internal sealed class GuidV7ValueGenerator : ValueGenerator<Guid>
{
    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry) => Guid.CreateVersion7();
}
