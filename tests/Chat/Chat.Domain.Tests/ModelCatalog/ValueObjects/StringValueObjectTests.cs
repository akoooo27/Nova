using Chat.Domain;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.ModelCatalog.ValueObjects;

public sealed class StringValueObjectTests
{
    [Fact]
    public void ProviderNameCreateTrimsWhitespaceWhenValueIsValid()
    {
        ErrorOr<ProviderName> result = ProviderName.Create("  OpenAI  ");

        Assert.False(result.IsError);
        Assert.Equal("OpenAI", result.Value.Value);
        Assert.Equal("OpenAI", result.Value.ToString());
    }

    [Fact]
    public void ProviderNameCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        ErrorOr<ProviderName> result = ProviderName.Create("   ");

        AssertRequiredError(result, "ProviderName.Required");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" value ")]
    public void ProviderNameFromDatabaseThrowsDomainExceptionWhenValueIsBlankOrUntrimmed(string value)
    {
        Assert.Throws<DomainException>(() => ProviderName.FromDatabase(value));
    }

    [Fact]
    public void ProviderSlugCreateNormalizesValidSlug()
    {
        ErrorOr<ProviderSlug> result = ProviderSlug.Create("  Open-AI-123  ");

        Assert.False(result.IsError);
        Assert.Equal("open-ai-123", result.Value.Value);
        Assert.Equal("open-ai-123", result.Value.ToString());
    }

    [Fact]
    public void ProviderSlugCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        ErrorOr<ProviderSlug> result = ProviderSlug.Create("   ");

        AssertRequiredError(result, "ProviderSlug.Required");
    }

    [Theory]
    [InlineData("open_ai")]
    [InlineData("-openai")]
    [InlineData("openai-")]
    [InlineData("open ai")]
    public void ProviderSlugCreateReturnsInvalidValidationWhenSlugIsNotUrlSafe(string value)
    {
        ErrorOr<ProviderSlug> result = ProviderSlug.Create(value);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("ProviderSlug.Invalid", error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("open_ai")]
    [InlineData("-openai")]
    [InlineData("openai-")]
    [InlineData("OpenAI")]
    public void ProviderSlugFromDatabaseThrowsDomainExceptionWhenSlugIsInvalid(string value)
    {
        Assert.Throws<DomainException>(() => ProviderSlug.FromDatabase(value));
    }

    [Fact]
    public void ExternalModelIdCreateTrimsWhitespaceWhenValueIsValid()
    {
        ErrorOr<ExternalModelId> result = ExternalModelId.Create("  gpt-4.1  ");

        Assert.False(result.IsError);
        Assert.Equal("gpt-4.1", result.Value.Value);
        Assert.Equal("gpt-4.1", result.Value.ToString());
    }

    [Fact]
    public void ExternalModelIdCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        ErrorOr<ExternalModelId> result = ExternalModelId.Create("   ");

        AssertRequiredError(result, "ExternalModelId.Required");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" value ")]
    public void ExternalModelIdFromDatabaseThrowsDomainExceptionWhenValueIsBlankOrUntrimmed(string value)
    {
        Assert.Throws<DomainException>(() => ExternalModelId.FromDatabase(value));
    }

    [Fact]
    public void ModelNameCreateTrimsWhitespaceWhenValueIsValid()
    {
        ErrorOr<ModelName> result = ModelName.Create("  GPT-4.1  ");

        Assert.False(result.IsError);
        Assert.Equal("GPT-4.1", result.Value.Value);
        Assert.Equal("GPT-4.1", result.Value.ToString());
    }

    [Fact]
    public void ModelNameCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        ErrorOr<ModelName> result = ModelName.Create("   ");

        AssertRequiredError(result, "ModelName.Required");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" value ")]
    public void ModelNameFromDatabaseThrowsDomainExceptionWhenValueIsBlankOrUntrimmed(string value)
    {
        Assert.Throws<DomainException>(() => ModelName.FromDatabase(value));
    }

    [Fact]
    public void ModelDescriptionCreateTrimsWhitespaceWhenValueIsValid()
    {
        ErrorOr<ModelDescription> result = ModelDescription.Create("  General purpose model  ");

        Assert.False(result.IsError);
        Assert.Equal("General purpose model", result.Value.Value);
        Assert.Equal("General purpose model", result.Value.ToString());
    }

    [Fact]
    public void ModelDescriptionCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        ErrorOr<ModelDescription> result = ModelDescription.Create("   ");

        AssertRequiredError(result, "ModelDescription.Required");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" value ")]
    public void ModelDescriptionFromDatabaseThrowsDomainExceptionWhenValueIsBlankOrUntrimmed(string value)
    {
        Assert.Throws<DomainException>(() => ModelDescription.FromDatabase(value));
    }

    private static void AssertRequiredError<T>(ErrorOr<T> result, string code)
    {
        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
    }
}