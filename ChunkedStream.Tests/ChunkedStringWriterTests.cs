using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChunkedStream.Tests
{
    [TestClass]
    public class ChunkedStringWriterTests
    {
        [TestMethod]
        public void ChunkedStringWriter_WriteChar()
        {
            var pool = new MemoryPool(4, 2);
            var stream = new ChunkedStream(pool);

            using (var writer = new ChunkedStringWriter(stream))
            {
                writer.Write('a');
                writer.Write('b');
                writer.Write('c');
                writer.Write('d');
                writer.Write('e');
                writer.Write('f');
                writer.Write('g');

                Assert.AreEqual(14, stream.Length);
                Assert.AreEqual("abcdefg", writer.ToString());
            }

            Assert.AreEqual(0, pool.TotalAllocated);
        }

        [TestMethod]
        public void ChunkedStringWriter_WriteSingleChar()
        {
            var pool = new MemoryPool(4, 2);
            var stream = new ChunkedStream(pool);

            using (var writer = new ChunkedStringWriter(stream))
            {
                writer.Write('x');

                Assert.AreEqual(2, stream.Length);
                Assert.AreEqual("x", writer.ToString());
            }

            Assert.AreEqual(0, pool.TotalAllocated);
        }

        [TestMethod]
        public void ChunkedStringWriter_WriteNewLine()
        {
            var pool = new MemoryPool(4, 2);
            var stream = new ChunkedStream(pool);

            using (var writer = new ChunkedStringWriter(stream))
            {
                writer.WriteLine();
                Assert.AreEqual(Environment.NewLine, writer.ToString());
            }

            Assert.AreEqual(0, pool.TotalAllocated);
        }

        [TestMethod]
        public void ChunkedStringWriter_WriteString()
        {
            var pool = new MemoryPool(4, 2);
            var stream = new ChunkedStream(pool);

            using (var writer = new ChunkedStringWriter(stream))
            {
                writer.Write("abcdefg");
                writer.Write((string)null);

                Assert.AreEqual(14, stream.Length);
                Assert.AreEqual("abcdefg", writer.ToString());
            }

            Assert.AreEqual(0, pool.TotalAllocated);
        }

        [TestMethod]
        public void ChunkedStringWriter_WriteChars_WhenBufferAtOnce()
        {
            var pool = new MemoryPool(4, 2);
            var stream = new ChunkedStream(pool);

            var zerro = new char[] { };
            var a = new char[] { 'a' };
            var b = new char[] { 'b', 'c' };
            var c = new char[] { 'd', 'e', 'f', 'g' };

            using (var writer = new ChunkedStringWriter(stream))
            {
                writer.Write(zerro);
                writer.Write(a);
                writer.Write(b);
                writer.Write(c);
                writer.Write((char[])null);

                Assert.AreEqual(14, stream.Length);
                Assert.AreEqual("abcdefg", writer.ToString());
            }

            Assert.AreEqual(0, pool.TotalAllocated);
        }

        [TestMethod]
        public void ChunkedStringWriter_WriteChars_ManyCalls()
        {
            var pool = new MemoryPool(4, 2);
            var stream = new ChunkedStream(pool);

            var zerro = new char[] { };
            var a = new char[] { 'a' };
            var b = new char[] { 'b', 'c' };
            var c = new char[] { 'd', 'e', 'f', 'g' };

            using (var writer = new ChunkedStringWriter(stream))
            {
                writer.Write(zerro, 0, 0);
                writer.Write(a, 0, 0);
                writer.Write(a, 0, 1);
                writer.Write(b, 0, 1);
                writer.Write(b, 1, 1);
                writer.Write(c, 0, 2);
                writer.Write(c, 2, 2);

                Assert.AreEqual(14, stream.Length);
                Assert.AreEqual("abcdefg", writer.ToString());
            }

            Assert.AreEqual(0, pool.TotalAllocated);
        }

        [TestMethod]
        public void ChunkedStringWriter_WhenNoWrites()
        {
            var pool = new MemoryPool(4, 2);
            var stream = new ChunkedStream(pool);

            using (var writer = new ChunkedStringWriter(stream))
            {
                Assert.AreEqual(String.Empty, writer.ToString());
            }

            Assert.AreEqual(0, pool.TotalAllocated);
        }


        [TestMethod]
        public void ChunkedStringWriter_EqualsToStringWriter()
        {
            var pool = new MemoryPool(4, 2);
            var stream = new ChunkedStream(pool);

            var zerro = new char[] { };
            var a = new char[] { 'a' };
            var b = new char[] { 'b', 'c' };
            var c = new char[] { 'd', 'e', 'f', 'g' };

            using (var writer = new ChunkedStringWriter(stream))
            {
                var expected = new System.IO.StringWriter();

                writer.Write(zerro);
                expected.Write(zerro);

                writer.Write((string)null);
                expected.Write((string)null);

                writer.Write((char[])null);
                expected.Write((char[])null);

                writer.Write(1);
                expected.Write(1);

                writer.Write(-1);
                expected.Write(-1);

                writer.WriteLine();
                expected.WriteLine();

                var now = DateTime.Now;

                writer.Write(now);
                expected.Write(now);

                writer.Write(a);
                expected.Write(a);

                writer.WriteLine("test");
                expected.WriteLine("test");

                writer.Write(c, 1, 2);
                expected.Write(c, 1, 2);

                Assert.AreEqual(expected.ToString(), writer.ToString());
            }

            Assert.AreEqual(0, pool.TotalAllocated);
        }

        [TestMethod]
        public void ChunkedStringWriter_InParallel()
        {
            int threadCount = 8, attempts = 100;
            string str = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

            var pool = new MemoryPool(chunkSize: 16, chunkCount: threadCount * 4);

            var actions = Enumerable.Range(0, threadCount).Select(_ => new Action(() =>
            {
                for (int i = 0; i < attempts; i++)
                {
                    var stream = new ChunkedStream(pool);
                    using (var writer = new ChunkedStringWriter(stream))
                    {
                        writer.Write(str);
                        Assert.AreEqual(str, writer.ToString());
                    }
                }

            })).ToArray();

            Parallel.Invoke(actions);
            Assert.AreEqual(0, pool.TotalAllocated);
        }

        [TestMethod]
        public void ChunkedStringWriter_OverrideNewLine()
        {
            using (var writer = new ChunkedStringWriter())
            {
                writer.NewLine = "<CR>";
                writer.WriteLine("line1");
                writer.WriteLine("line2");
                writer.Write("line3");

                Assert.AreEqual("line1<CR>line2<CR>line3", writer.ToString());
            }
        }

        [TestMethod]
        public void ChunkedStringWriter_WithStreamReader()
        {
            var pool = new MemoryPool(chunkSize: 4, chunkCount: 4);

            using (var stream = new ChunkedStream(pool, asOutputStreamOnDispose: true))
            {

                System.Text.Encoding encoding;

                using (var writer = new ChunkedStringWriter(stream))
                {
                    encoding = writer.Encoding;

                    writer.WriteLine("line1");
                    writer.WriteLine("line2");
                    writer.WriteLine("line3");
                }

                Assert.AreEqual(ChunkedStreamState.ReadForward, stream.State);
                Assert.AreEqual(0, stream.Position);

                using (var reader = new System.IO.StreamReader(stream, encoding))
                {
                    Assert.AreEqual("line1", reader.ReadLine());
                    Assert.AreEqual("line2", reader.ReadLine());
                    Assert.AreEqual("line3", reader.ReadLine());
                    Assert.IsNull(reader.ReadLine());
                }

                Assert.AreEqual(ChunkedStreamState.Closed, stream.State);
            }
        }

        [TestMethod]
        public void ChunkedStringWriter_WithStringReader()
        {
            var pool = new MemoryPool(chunkSize: 4, chunkCount: 4);

            using (var stream = new ChunkedStream(pool))
            {

                string content;

                using (var writer = new ChunkedStringWriter(stream))
                {
                    writer.WriteLine("line1");
                    writer.WriteLine("line2");
                    writer.WriteLine("line3");

                    content = writer.ToString();
                }

                Assert.AreEqual(ChunkedStreamState.Closed, stream.State);

                using (var reader = new System.IO.StringReader(content))
                {
                    Assert.AreEqual("line1", reader.ReadLine());
                    Assert.AreEqual("line2", reader.ReadLine());
                    Assert.AreEqual("line3", reader.ReadLine());
                    Assert.IsNull(reader.ReadLine());
                }
            }
        }
    }
}
