using System;
using Skylines.Host.Ui;
using Xunit;

namespace Skylines.Host.Tests
{
    public class DragGestureTests
    {
        [Fact]
        public void DefaultThresholdIsEightPixelsAndStartsIdle()
        {
            Assert.Equal(8f, DragGesture.DefaultThresholdPx);
            var g = new DragGesture();
            Assert.Equal(DragPhase.Idle, g.Phase);
            Assert.False(g.Active);
            g.Press(0, 0);
            Assert.Equal(DragOutcome.None, g.Move(7.9f, 0));
            Assert.Equal(DragOutcome.Started, g.Move(8f, 0));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        [InlineData(float.NaN)]
        public void ConstructorRejectsNonPositiveOrNaNThreshold(float t)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DragGesture(t));
        }

        [Fact]
        public void PressMovesIdleToPressedAndReturnsTrue()
        {
            var g = new DragGesture();
            Assert.True(g.Press(1, 2));
            Assert.Equal(DragPhase.Pressed, g.Phase);
            Assert.True(g.Active);
        }

        [Fact]
        public void PressWhilePressedIsIgnoredAndKeepsOriginalPoint()
        {
            var g = new DragGesture(10f);
            g.Press(0, 0);
            Assert.False(g.Press(100, 100));
            Assert.Equal(DragPhase.Pressed, g.Phase);
            Assert.Equal(DragOutcome.Started, g.Move(10f, 0));
        }

        [Fact]
        public void PressWhileDraggingIsIgnored()
        {
            var g = new DragGesture(5f);
            g.Press(0, 0);
            g.Move(5, 0);
            Assert.False(g.Press(0, 0));
            Assert.Equal(DragPhase.Dragging, g.Phase);
        }

        [Fact]
        public void MoveBelowThresholdStaysPressed()
        {
            var g = new DragGesture(10f);
            g.Press(0, 0);
            Assert.Equal(DragOutcome.None, g.Move(9.99f, 0));
            Assert.Equal(DragPhase.Pressed, g.Phase);
        }

        [Fact]
        public void MoveExactlyAtThresholdStarts()
        {
            var g = new DragGesture(10f);
            g.Press(0, 0);
            Assert.Equal(DragOutcome.Started, g.Move(0, 10f));
            Assert.Equal(DragPhase.Dragging, g.Phase);
        }

        [Fact]
        public void MoveAboveThresholdStarts()
        {
            var g = new DragGesture(10f);
            g.Press(0, 0);
            Assert.Equal(DragOutcome.Started, g.Move(-20f, 0));
            Assert.Equal(DragPhase.Dragging, g.Phase);
        }

        [Fact]
        public void DistanceIsEuclideanFromPressPoint()
        {
            var g = new DragGesture(10f);
            g.Press(100, 100);
            // 6,7 -> sqrt(85) = 9.22 < 10 although |dx|+|dy| = 13
            Assert.Equal(DragOutcome.None, g.Move(106, 107));
            // 6,8 -> exactly 10
            Assert.Equal(DragOutcome.Started, g.Move(106, 108));
        }

        [Fact]
        public void StartedIsReportedOnlyOnce()
        {
            var g = new DragGesture(5f);
            g.Press(0, 0);
            Assert.Equal(DragOutcome.Started, g.Move(5, 0));
            Assert.Equal(DragOutcome.None, g.Move(50, 50));
            Assert.Equal(DragOutcome.None, g.Move(0, 0));
            Assert.Equal(DragPhase.Dragging, g.Phase);
        }

        [Fact]
        public void MoveWhileIdleIsNoOp()
        {
            var g = new DragGesture(1f);
            Assert.Equal(DragOutcome.None, g.Move(500, 500));
            Assert.Equal(DragPhase.Idle, g.Phase);
        }

        [Fact]
        public void ReleaseFromPressedIsClickAndEndsIdle()
        {
            var g = new DragGesture();
            g.Press(0, 0);
            Assert.Equal(DragOutcome.Clicked, g.Release(false, false));
            Assert.Equal(DragPhase.Idle, g.Phase);
            Assert.False(g.Active);
        }

        [Fact]
        public void ReleaseAfterSmallMoveIsStillClick()
        {
            var g = new DragGesture(10f);
            g.Press(0, 0);
            g.Move(3, 3);
            Assert.Equal(DragOutcome.Clicked, g.Release(true, true));
        }

        [Fact]
        public void DropWithTargetAndNotOverUiIsDropped()
        {
            var g = new DragGesture(5f);
            g.Press(0, 0);
            g.Move(20, 0);
            Assert.Equal(DragOutcome.Dropped, g.Release(false, true));
            Assert.Equal(DragPhase.Idle, g.Phase);
        }

        [Fact]
        public void DropOverUiCancels()
        {
            var g = new DragGesture(5f);
            g.Press(0, 0);
            g.Move(20, 0);
            Assert.Equal(DragOutcome.Cancelled, g.Release(true, true));
            Assert.Equal(DragPhase.Idle, g.Phase);
        }

        [Fact]
        public void DropWithoutTargetCancels()
        {
            var g = new DragGesture(5f);
            g.Press(0, 0);
            g.Move(20, 0);
            Assert.Equal(DragOutcome.Cancelled, g.Release(false, false));
            Assert.Equal(DragPhase.Idle, g.Phase);
        }

        [Fact]
        public void ReleaseWhileIdleIsNone()
        {
            var g = new DragGesture();
            Assert.Equal(DragOutcome.None, g.Release(false, true));
            Assert.Equal(DragPhase.Idle, g.Phase);
        }

        [Fact]
        public void CancelFromPressedCancels()
        {
            var g = new DragGesture();
            g.Press(0, 0);
            Assert.Equal(DragOutcome.Cancelled, g.Cancel());
            Assert.Equal(DragPhase.Idle, g.Phase);
        }

        [Fact]
        public void CancelFromDraggingCancels()
        {
            var g = new DragGesture(5f);
            g.Press(0, 0);
            g.Move(20, 0);
            Assert.Equal(DragOutcome.Cancelled, g.Cancel());
            Assert.Equal(DragPhase.Idle, g.Phase);
        }

        [Fact]
        public void CancelWhileIdleIsNone()
        {
            var g = new DragGesture();
            Assert.Equal(DragOutcome.None, g.Cancel());
            Assert.Equal(DragPhase.Idle, g.Phase);
        }

        [Fact]
        public void ReusableAfterEveryOutcome()
        {
            var g = new DragGesture(5f);

            g.Press(0, 0);
            Assert.Equal(DragOutcome.Clicked, g.Release(false, false));

            Assert.True(g.Press(0, 0));
            g.Move(10, 0);
            Assert.Equal(DragOutcome.Dropped, g.Release(false, true));

            Assert.True(g.Press(0, 0));
            g.Move(10, 0);
            Assert.Equal(DragOutcome.Cancelled, g.Release(true, true));

            Assert.True(g.Press(0, 0));
            Assert.Equal(DragOutcome.Cancelled, g.Cancel());

            // A fresh press uses the new press point, not a stale one.
            Assert.True(g.Press(200, 200));
            Assert.Equal(DragOutcome.None, g.Move(201, 200));
            Assert.Equal(DragOutcome.Started, g.Move(205, 200));
        }
    }
}
