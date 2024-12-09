namespace Core.Entities.Images;

public record ImageInfos(Guid Id, ReadOnlyMemory<Half> ImageHash);