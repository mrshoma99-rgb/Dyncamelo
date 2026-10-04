using CamelGraph.Core.Editing;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>The library search treats British and American spellings as the same word (VAL-24).</summary>
public class SearchSpellingTests
{
    [Theory]
    [InlineData("colour", "color")]
    [InlineData("colours", "colors")]
    [InlineData("coloured", "colored")]
    [InlineData("colourblind", "colorblind")]
    [InlineData("grey", "gray")]
    [InlineData("greyscale", "grayscale")]
    [InlineData("centre", "center")]
    [InlineData("centred", "centered")]
    [InlineData("centreline", "centerline")]
    [InlineData("metre", "meter")]
    [InlineData("metres", "meters")]
    [InlineData("millimetre", "millimeter")]
    [InlineData("litre", "liter")]
    [InlineData("storey", "story")]
    [InlineData("storeys", "stories")]
    [InlineData("ifcbuildingstorey", "ifcbuildingstory")]
    [InlineData("normalise", "normalize")]
    [InlineData("normalised", "normalized")]
    [InlineData("normalising", "normalizing")]
    [InlineData("normalisation", "normalization")]
    [InlineData("analyse", "analyze")]
    [InlineData("analysed", "analyzed")]
    [InlineData("capitalise", "capitalize")]
    [InlineData("neighbour", "neighbor")]
    [InlineData("catalogue", "catalog")]
    public void BritishSpellingsAreFoldedToAmericanOnes(string british, string american)
    {
        Assert.Equal(american, SearchSpelling.Fold(british));
        Assert.Equal(american, SearchSpelling.Fold(american));
    }

    [Theory]
    [InlineData("color.byhsv")]
    [InlineData("rise")]
    [InlineData("analysis")]
    [InlineData("history")]
    [InlineData("center of the model")]
    [InlineData("metric")]
    [InlineData("")]
    public void OtherTextIsUntouched(string text)
    {
        Assert.Equal(text, SearchSpelling.Fold(text));
    }

    [Fact]
    public void ASentenceIsFoldedWordByWord()
    {
        Assert.Equal(
            "overrides the color of items near the center, in meters",
            SearchSpelling.Fold("overrides the colour of items near the centre, in metres"));
        Assert.Equal(string.Empty, SearchSpelling.Fold(null));
    }

    [Fact]
    public void ASearchWordAndANodeTextMeetWhicheverSpellingEachUses()
    {
        var node = SearchSpelling.Fold("color.byhsv colour hue saturation");
        var query = SearchSpelling.Fold("colour");

        Assert.Contains(query, node);
        Assert.Contains(SearchSpelling.Fold("color"), SearchSpelling.Fold("a colourful node"));
        Assert.Contains(SearchSpelling.Fold("gray"), SearchSpelling.Fold("convert to grey"));
    }
}
