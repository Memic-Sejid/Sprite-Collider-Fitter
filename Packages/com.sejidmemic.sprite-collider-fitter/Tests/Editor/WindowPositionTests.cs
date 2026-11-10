using System.Globalization;
using System.Threading;
using NUnit.Framework;
using SpriteFit.Editor;
using UnityEngine;

namespace SpriteFit.Tests
{
    /// <summary>The window's last floating rect is stored as text in EditorPrefs and read back when it reopens.</summary>
    public class WindowPositionTests
    {
        [Test]
        public void RectRoundTrips()
        {
            // Off the main monitor and at a fractional position, as on a second screen with display scaling.
            var rect = new Rect(1923.5f, -12.25f, 380f, 677f);

            bool parsed = SpriteFitWindow.TryParseRect(SpriteFitWindow.FormatRect(rect), out Rect result);

            Assert.That(parsed, Is.True);
            Assert.That(result, Is.EqualTo(rect));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("1;2;3")]
        [TestCase("a;b;c;d")]
        [TestCase("1;2;NaN;4")]
        public void AnythingElseIsRejected(string text)
        {
            Assert.That(SpriteFitWindow.TryParseRect(text, out _), Is.False);
        }

        [Test]
        public void FormatDoesNotDependOnCulture()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                // German writes 1,5 for 1.5, which would break the separator and the parse.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var rect = new Rect(10.5f, 20.25f, 380f, 677.75f);

                string text = SpriteFitWindow.FormatRect(rect);

                Assert.That(text, Is.EqualTo("10.5;20.25;380;677.75"));
                Assert.That(SpriteFitWindow.TryParseRect(text, out Rect result), Is.True);
                Assert.That(result, Is.EqualTo(rect));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }
    }
}
