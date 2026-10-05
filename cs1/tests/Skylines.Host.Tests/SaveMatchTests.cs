using System;
using System.Collections.Generic;
using Skylines.Host;
using Xunit;

namespace Skylines.Host.Tests
{
    public class SaveMatchTests
    {
        private static SaveCandidate S(string pkg, string asset, string city, int day)
        {
            return new SaveCandidate { PackageName = pkg, AssetName = asset, CityName = city, Timestamp = new DateTime(2026, 1, day) };
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void BlankWantedReturnsMinusOne(string wanted)
        {
            Assert.Equal(-1, SaveMatch.Pick(new List<SaveCandidate> { S("a", "a", "a", 1) }, wanted));
        }

        [Fact]
        public void NoMatchAndEmptyListReturnMinusOne()
        {
            Assert.Equal(-1, SaveMatch.Pick(new List<SaveCandidate> { S("a", "b", "c", 1) }, "zzz"));
            Assert.Equal(-1, SaveMatch.Pick(new List<SaveCandidate>(), "a"));
        }

        [Fact]
        public void MatchesCaseInsensitivelyAndTrimsWanted()
        {
            Assert.Equal(1, SaveMatch.Pick(new List<SaveCandidate> { S("x", "x", "x", 1), S("MyCity", null, null, 1) }, "  mycity "));
        }

        [Fact]
        public void PackageTierBeatsNewerAssetAndCityMatches()
        {
            var l = new List<SaveCandidate> { S(null, null, "foo", 20), S(null, "foo", null, 21), S("foo", null, null, 1) };
            Assert.Equal(2, SaveMatch.Pick(l, "foo"));
        }

        [Fact]
        public void AssetTierBeatsCityTier()
        {
            var l = new List<SaveCandidate> { S(null, null, "foo", 20), S(null, "FOO", null, 1) };
            Assert.Equal(1, SaveMatch.Pick(l, "foo"));
        }

        [Fact]
        public void CityTierUsedWhenOthersMiss()
        {
            Assert.Equal(0, SaveMatch.Pick(new List<SaveCandidate> { S("p", "a", "Foo", 1) }, "foo"));
        }

        [Fact]
        public void NewestTimestampWinsWithinTier()
        {
            var l = new List<SaveCandidate> { S("foo", null, null, 3), S("foo", null, null, 9), S("foo", null, null, 5) };
            Assert.Equal(1, SaveMatch.Pick(l, "foo"));
        }

        [Fact]
        public void EqualTimestampsPickLowestIndex()
        {
            var l = new List<SaveCandidate> { S("a", null, null, 1), S("foo", null, null, 4), S("foo", null, null, 4) };
            Assert.Equal(1, SaveMatch.Pick(l, "foo"));
        }

        [Fact]
        public void NullFieldsNeverMatch()
        {
            Assert.Equal(-1, SaveMatch.Pick(new List<SaveCandidate> { S(null, null, null, 1) }, "null"));
        }
    }
}
