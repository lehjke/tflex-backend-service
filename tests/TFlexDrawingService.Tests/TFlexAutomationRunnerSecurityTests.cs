using System.Globalization;

namespace TFlexDrawingService.Tests;

public sealed class TFlexAutomationRunnerSecurityTests
{
    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "0")]
    [InlineData(42, "42")]
    [InlineData(12.5, "12.5")]
    public void RealVariableValueFormatter_AcceptsOnlyLiteralValueTypes(
        object value,
        string expected)
    {
        Assert.Equal(
            expected,
            TFlexAutomationRunner.RealVariableValueFormatter.Format(value));
    }

    [Fact]
    public void RealVariableValueFormatter_RejectsExpressionText()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => TFlexAutomationRunner.RealVariableValueFormatter.Format("setg(\"x\", 1)"));

        Assert.Contains("numeric or boolean", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RealVariableValueFormatter_UsesInvariantDecimalFormatting()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            Assert.Equal(
                "12.5",
                TFlexAutomationRunner.RealVariableValueFormatter.Format(12.5m));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
