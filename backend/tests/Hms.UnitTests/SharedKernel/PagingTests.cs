using Hms.SharedKernel;

namespace Hms.UnitTests.SharedKernel;

public class PagingTests
{
    [Theory]
    [InlineData(null, null, 1, 20)]
    [InlineData(0, 0, 1, 20)]
    [InlineData(-5, -1, 1, 20)]
    [InlineData(3, 50, 3, 50)]
    [InlineData(2, 10_000, 2, 100)]
    public void PageRequest_NormalizesBounds(int? page, int? size, int expectedPage, int expectedSize)
    {
        var request = new PageRequest(page, size);

        request.Page.Should().Be(expectedPage);
        request.PageSize.Should().Be(expectedSize);
        request.Skip.Should().Be((expectedPage - 1) * expectedSize);
    }

    [Fact]
    public void PagedResult_CalculatesTotalPages()
    {
        new PagedResult<int>([], 1, 20, 41).TotalPages.Should().Be(3);
        new PagedResult<int>([], 1, 20, 0).TotalPages.Should().Be(0);
    }
}
