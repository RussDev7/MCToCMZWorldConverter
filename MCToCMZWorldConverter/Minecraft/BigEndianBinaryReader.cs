/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2026 RussDev7
This file is part of https://github.com/RussDev7/MCToCMZWorldConverter - see LICENSE for details.
*/

using System.Text;
using System.IO;
using System;

namespace MCToCMZWorldConverter.Minecraft
{
    /// <summary>
    /// Minimal big-endian binary reader used for Minecraft Anvil/region file headers.
    /// </summary>
    /// <remarks>
    /// Minecraft region metadata stores integers in big-endian order, while .NET's
    /// <see cref="BinaryReader"/> reads little-endian integers by default. This wrapper
    /// keeps the base stream open and only overrides the reads currently needed by the
    /// converter.
    /// </remarks>
    internal sealed class BigEndianBinaryReader : IDisposable
    {
        #region Fields

        /// <summary>
        /// Underlying reader used for byte-level stream access.
        /// </summary>
        private readonly BinaryReader _reader;

        #endregion

        #region Construction

        /// <summary>
        /// Creates a reader over an existing stream without taking ownership of that stream.
        /// </summary>
        /// <param name="stream">The stream containing big-endian binary data.</param>
        public BigEndianBinaryReader(Stream stream)
        {
            _reader = new BinaryReader(stream, Encoding.UTF8, true);
        }
        #endregion

        #region Big-endian primitive reads

        /// <summary>
        /// Reads a 32-bit signed integer encoded in big-endian byte order.
        /// </summary>
        /// <exception cref="EndOfStreamException">
        /// Thrown when fewer than four bytes are available.
        /// </exception>
        public int ReadInt32()
        {
            byte[] b = _reader.ReadBytes(4);
            if (b.Length != 4)
                throw new EndOfStreamException();

            return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
        }

        /// <summary>
        /// Reads a single byte from the stream.
        /// </summary>
        public byte ReadByte() => _reader.ReadByte();

        /// <summary>
        /// Reads the requested number of bytes from the stream.
        /// </summary>
        /// <param name="count">The number of bytes to read.</param>
        public byte[] ReadBytes(int count) => _reader.ReadBytes(count);

        #endregion

        #region IDisposable

        /// <summary>
        /// Disposes the wrapper reader.
        /// </summary>
        public void Dispose() => _reader.Dispose();

        #endregion
    }
}