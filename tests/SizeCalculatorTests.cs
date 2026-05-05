using Aiursoft.HowToCookViewer.Services.FileStorage;

namespace Aiursoft.HowToCookViewer.Tests;

[TestClass]
public class SizeCalculatorTests
{
    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(1, 1)]
    [DataRow(2, 2)]
    [DataRow(3, 4)]
    [DataRow(15, 16)]
    [DataRow(16384, 16384)]
    [DataRow(16385, 16384)]
    public void TestCeiling(int input, int expected)
    {
        var result = SizeCalculator.Ceiling(input);
        Assert.AreEqual(expected, result);
    }
}
