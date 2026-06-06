using Chat.Domain;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.ModelCatalog.ValueObjects;

public sealed class IdentifierValueObjectTests
{
    [Fact]
    public void LlmProviderIdNewReturnsNonEmptyId()
    {
        LlmProviderId id = LlmProviderId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
        Assert.Equal(id.Value.ToString(), id.ToString());
    }

    [Fact]
    public void LlmProviderIdCreateReturnsValueWhenGuidIsNotEmpty()
    {
        Guid value = Guid.NewGuid();

        ErrorOr<LlmProviderId> result = LlmProviderId.Create(value);

        Assert.False(result.IsError);
        Assert.Equal(value, result.Value.Value);
        Assert.Equal(value.ToString(), result.Value.ToString());
    }

    [Fact]
    public void LlmProviderIdCreateReturnsEmptyValidationWhenGuidIsEmpty()
    {
        ErrorOr<LlmProviderId> result = LlmProviderId.Create(Guid.Empty);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("LlmProviderId.Empty", error.Code);
    }

    [Fact]
    public void LlmProviderIdFromDatabaseThrowsDomainExceptionWhenGuidIsEmpty()
    {
        Assert.Throws<DomainException>(() => LlmProviderId.FromDatabase(Guid.Empty));
    }

    [Fact]
    public void LlmModelIdNewReturnsNonEmptyId()
    {
        LlmModelId id = LlmModelId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
        Assert.Equal(id.Value.ToString(), id.ToString());
    }

    [Fact]
    public void LlmModelIdCreateReturnsValueWhenGuidIsNotEmpty()
    {
        Guid value = Guid.NewGuid();

        ErrorOr<LlmModelId> result = LlmModelId.Create(value);

        Assert.False(result.IsError);
        Assert.Equal(value, result.Value.Value);
        Assert.Equal(value.ToString(), result.Value.ToString());
    }

    [Fact]
    public void LlmModelIdCreateReturnsEmptyValidationWhenGuidIsEmpty()
    {
        ErrorOr<LlmModelId> result = LlmModelId.Create(Guid.Empty);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("LlmModelId.Empty", error.Code);
    }

    [Fact]
    public void LlmModelIdFromDatabaseThrowsDomainExceptionWhenGuidIsEmpty()
    {
        Assert.Throws<DomainException>(() => LlmModelId.FromDatabase(Guid.Empty));
    }
}