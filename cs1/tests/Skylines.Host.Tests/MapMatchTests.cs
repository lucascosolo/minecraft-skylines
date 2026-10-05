using System;
using System.Collections.Generic;
using Skylines.Host;
using Xunit;

namespace Skylines.Host.Tests
{
    public class MapMatchTests
    {
        private static SaveCandidate M(string pkg, string asset, string display, int day)
        {
            return new SaveCandidate { PackageName = pkg, AssetName = asset, CityName = display, Timestamp = new DateTime(2026, 1, day) };
        }

        private static List<SaveCandidate> Maps()
        {
            return new List<SaveCandidate>
            {
                M("GreenPlains", "GreenPlains", "Green Plains", 1),
                M("TwoRivers", "TwoRivers", "Two Rivers", 1),
                M("grassy_asset_pkg", "Grassy Fields", "Grassland", 1),
            };
        }

        [Fact]
        public void MatchesByPackageName()
        {
            Assert.Equal(0, SaveMatch.Pick(Maps(), "GreenPlains"));
        }

        [Fact]
        public void MatchesByAssetName()
        {
            Assert.Equal(2, SaveMatch.Pick(Maps(), "Grassy Fields"));
        }

        [Fact]
        public void MatchesByDisplayNameWithSpaces()
        {
            Assert.Equal(1, SaveMatch.Pick(Maps(), "Two Rivers"));
            Assert.Equal(0, SaveMatch.Pick(Maps(), "Green Plains"));
        }

        [Fact]
        public void IgnoresCaseAndSurroundingWhitespace()
        {
            Assert.Equal(0, SaveMatch.Pick(Maps(), "  green plains  "));
            Assert.Equal(1, SaveMatch.Pick(Maps(), "TWO RIVERS"));
        }

        [Fact]
        public void PackageNameBeatsDisplayNameOnAnotherMap()
        {
            var l = new List<SaveCandidate> { M("x", "x", "Green Plains", 20), M("Green Plains", "y", "y", 1) };
            Assert.Equal(1, SaveMatch.Pick(l, "Green Plains"));
        }

        [Fact]
        public void NoMatchReturnsMinusOne()
        {
            Assert.Equal(-1, SaveMatch.Pick(Maps(), "Nowhere"));
        }

        [Fact]
        public void NewPrefixIsNotStrippedByPick()
        {
            Assert.Equal(-1, SaveMatch.Pick(Maps(), "new:Green Plains"));
        }
    }
}
