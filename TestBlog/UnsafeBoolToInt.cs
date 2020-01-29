using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace TestBlog
{
    public class UnsafeBoolToInt
    {
        [Test]
        public void UnsafeBoolToIntIsSane([Values(false, true)] bool testBool)
        {
            var i = Unsafe.As<bool, int>(ref testBool);

            var expectedValue = testBool ? 1 : 0;
            Assert.That(i, Is.EqualTo(expectedValue));
        }
    }
}