using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.ModelCatalog;

public sealed class LlmProviderErrorsTests
{
    [Fact]
    public void ModelAlreadyExistsReturnsConflictWithExternalModelId()
    {
        ExternalModelId externalModelId = ExternalModelId.FromDatabase("gpt-4.1");

        Error error = LlmProviderErrors.ModelAlreadyExists(externalModelId);

        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal("LlmProvider.ModelAlreadyExists", error.Code);
        Assert.Contains("'gpt-4.1'", error.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelNotFoundReturnsNotFoundWithModelId()
    {
        Guid value = Guid.NewGuid();
        LlmModelId modelId = LlmModelId.FromDatabase(value);

        Error error = LlmProviderErrors.ModelNotFound(modelId);

        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("LlmProvider.ModelNotFound", error.Code);
        Assert.Contains(value.ToString(), error.Description, StringComparison.Ordinal);
    }
}