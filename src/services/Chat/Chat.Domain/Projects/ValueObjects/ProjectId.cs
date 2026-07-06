using ErrorOr;

namespace Chat.Domain.Projects.ValueObjects;

public sealed record ProjectId
{
    public Guid Value { get; }

    private ProjectId(Guid value)
    {
        Value = value;
    }

    public static ProjectId New() => new(Guid.CreateVersion7());

    public static ErrorOr<ProjectId> Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            return Error.Validation
            (
                code: "ProjectId.Empty",
                description: "Project id cannot be empty."
            );
        }

        return new ProjectId(value);
    }

    public static ProjectId FromDatabase(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("Database contained an empty project id.");

        return new ProjectId(value);
    }

    public override string ToString() => Value.ToString();
}