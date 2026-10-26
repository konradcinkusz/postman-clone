using PostmanClone.ServiceDefaults.Paging;
using Xunit;

namespace PostmanClone.ServiceDefaults.Tests.Paging;

/// <summary>Paging inputs are clamped at the kernel, so no list endpoint can ask for an unbounded page (DoS control).</summary>
public sealed class PageRequestTests
{
    [Fact]
    public void Missing_inputs_take_the_defaults()
    {
        var page = PageRequest.Clamp(null, null);

        Assert.Equal(1, page.Page);
        Assert.Equal(PageRequest.DefaultPageSize, page.PageSize);
        Assert.Equal(0, page.Skip);
    }

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-3, -10, 1, 1)]
    [InlineData(-1, 1000, 1, 100)]
    [InlineData(3, 20, 3, 20)]
    public void Page_is_at_least_one_and_page_size_stays_between_one_and_one_hundred(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        var clamped = PageRequest.Clamp(page, pageSize);

        Assert.Equal(expectedPage, clamped.Page);
        Assert.Equal(expectedPageSize, clamped.PageSize);
    }

    [Fact]
    public void Skip_is_computed_without_overflowing_at_the_largest_page()
    {
        var clamped = PageRequest.Clamp(int.MaxValue, PageRequest.MaxPageSize);

        Assert.Equal((long)(int.MaxValue - 1) * PageRequest.MaxPageSize, clamped.Skip);
    }
}
