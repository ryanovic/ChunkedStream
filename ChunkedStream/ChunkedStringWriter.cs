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
        private static Encoding _encoding = new UnicodeEncoding(false, false);

        private bool _disposed = false;

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

        public ChunkedStream Stream
        {
            get
            {
                return _stream;
            }
        }

        public override Encoding Encoding
        {
            get
            {
                return _encoding;
            }
        }

        public override void Write(char value)
        {
            #region Validate

            if (_disposed)
                throw new ObjectDisposedException(null);

            #endregion

            _stream.WriteByte((byte)(value & 0x00FF));
            _stream.WriteByte((byte)(value >> 8));
        }

        public override void Write(char[] buffer)
        {
            #region Validate

            if (_disposed)
                throw new ObjectDisposedException(null);

            #endregion

            if (buffer != null && buffer.Length > 0)
            {
                fixed (char* pbuff = buffer)
                {
                    _stream.Write((byte*)pbuff, buffer.Length * 2);
                }
            }
        }

        public void Write(char* pbuff, int count)
        {
            #region Validate

            if (_disposed)
                throw new ObjectDisposedException(null);

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            #endregion

            if (count > 0)
            {
                _stream.Write((byte*)pbuff, count * 2);
            }
        }

        public override void Write(char[] buffer, int index, int count)
        {
            #region Validate

            if (_disposed)
                throw new ObjectDisposedException(null);

            if (buffer == null)
                throw new NullReferenceException(nameof(buffer));

            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (index + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(index) + " + " + nameof(count));

            #endregion            

            if (count > 0)
            {
                fixed (char* pbuff = &buffer[index])
                {
                    _stream.Write((byte*)pbuff, count * 2);
                }
            }
        }

        public override void Write(string value)
        {
            #region Validate

            if (_disposed)
                throw new ObjectDisposedException(null);

            #endregion

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
            #region Validate

            if (_disposed)
                throw new ObjectDisposedException(null);

            #endregion

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
            #region Validate

            if (_disposed)
                throw new ObjectDisposedException(null);

            #endregion

            if (_stream.Length == 0) return String.Empty;

            _stream.Position = 0;

            var str = new String((char)0, (int)(_stream.Length / 2));

            fixed (char* pstr = str)
            {
                _stream.Read((byte*)pstr, str.Length * 2);
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
