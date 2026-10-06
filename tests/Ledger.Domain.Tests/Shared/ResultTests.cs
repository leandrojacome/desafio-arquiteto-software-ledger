using Ledger.Domain.Shared;

namespace Ledger.Domain.Tests.Shared;

[Trait("Category", "Unit")]
public sealed class ResultTests
{
    private static readonly Error SampleError = new("SAMPLE", "Sample failure.", ErrorKind.Unprocessable);

    [Fact]
    public void Success_IsNotAFailure()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
    }

    [Fact]
    public void Error_OnSuccess_Throws()
    {
        var result = Result.Success();

        Should.Throw<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Error_OnSuccessWithValue_Throws()
    {
        var result = Result.Success("payload");

        Should.Throw<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_CarriesTheError()
    {
        var result = Result.Failure(SampleError);

        result.IsFailure.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(SampleError);
    }

    [Fact]
    public void SuccessWithValue_ExposesTheValue()
    {
        var result = Result.Success("payload");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("payload");
    }

    [Fact]
    public void SuccessWithDefaultValueType_ExposesTheValue()
    {
        var result = Result.Success(0);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(0);
    }

    [Fact]
    public void Value_OnFailure_Throws()
    {
        var result = Result.Failure<string>(SampleError);

        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void FailureOfValueType_ThrowsOnValueEvenWhenDefaultIsZero()
    {
        var result = Result.Failure<int>(SampleError);

        result.IsFailure.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void ImplicitConversionFromValue_ProducesSuccess()
    {
        Result<string> result = "converted";

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("converted");
    }

    [Fact]
    public void ImplicitConversionFromError_ProducesFailure()
    {
        Result<string> result = SampleError;

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SampleError);
    }

    [Fact]
    public void Error_UsesValueEquality()
    {
        var same = new Error("SAMPLE", "Sample failure.", ErrorKind.Unprocessable);

        same.ShouldBe(SampleError);
    }
}
