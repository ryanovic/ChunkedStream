using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChunkedStream
{
    public unsafe sealed class ChunkedStringWriter : TextWriter
    {
        private static byte[] _newLine = Encoding.Unicode.GetBytes(Environment.NewLine);

        private bool _disposed = false;

        private Encoding _unicode;
        private ChunkedStream _stream;
        private bool _leaveOpen;

        public ChunkedStringWriter(ChunkedStream stream, IFormatProvider formatProvider, bool leaveOpen = false)
            : base(formatProvider)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            this._stream = stream;
            this._leaveOpen = leaveOpen;
        }

        public ChunkedStringWriter(ChunkedStream stream, bool leaveOpen = false)
            : this(stream, null, leaveOpen)
        {
        }

        public ChunkedStringWriter(IFormatProvider formatProvider)
            : this(new ChunkedStream(), formatProvider, leaveOpen: false)
        {
        }

        public ChunkedStringWriter()
            : this(new ChunkedStream(), null, leaveOpen: false)
        {
        }

        public override Encoding Encoding
        {
            get
            {
                if (_unicode == null)
                    _unicode = new UnicodeEncoding(false, false);

                return _unicode;
            }
        }

        public override void Write(char value)
        {
            if (_disposed)
                throw new ObjectDisposedException(null);

            _stream.WriteByte((byte)(value & 0x00FF));
            _stream.WriteByte((byte)(value >> 8));
        }

        public override void Write(char[] buffer)
        {
            if (_disposed)
                throw new ObjectDisposedException(null);

            if (buffer != null)
            {
                fixed (char* pbuff = buffer)
                {
                    _stream.Write((byte*)pbuff, buffer.Length * 2);
                }
            }
        }

        public override void Write(char[] buffer, int offset, int count)
        {
            if (count == 0) return;

            if (_disposed)
                throw new ObjectDisposedException(null);

            ChunkedStream.VerifyInputBuffer(buffer, offset, count);

            fixed (char* pbuffer = &buffer[offset])
            {
                _stream.Write((byte*)pbuffer, count * 2);
            }
        }

        public override void Write(string value)
        {
            if (_disposed)
                throw new ObjectDisposedException(null);

            if (!String.IsNullOrEmpty(value))
            {
                fixed (char* pstr = value)
                {
                    _stream.Write((byte*)pstr, value.Length * 2);
                }
            }
        }

        public override void WriteLine()
        {
            if (_disposed)
                throw new ObjectDisposedException(null);

            _stream.Write(_newLine, 0, _newLine.Length);
        }

        // base.WriteLine join value & NewLine first
        // so just override and call separately

        public override void WriteLine(string value)
        {
            this.Write(value);
            this.WriteLine();
        }

        // copy all bytes into new string

        public override string ToString()
        {
            if (_disposed)
                throw new ObjectDisposedException(null);

            if (_stream.Length == 0)
                return String.Empty;

            _stream.Position = 0;

            var str = new String((char)0, (int)(_stream.Length / 2));

            fixed (char* pstr = str)
            {
                _stream.CopyTo((byte*)pstr);
            }

            return str;
        }

        // dispose

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                if (!_leaveOpen)
                    _stream.Dispose();

                _stream = null;
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
