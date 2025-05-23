﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TruckLib.Sii
{
    /// <summary>
    /// Functions for decoding and encoding the 3nK format.
    /// </summary>
    public static class ThreeNK
    {
        internal static readonly byte[] KeyTable = [
            0xF8, 0xD1, 0xAA, 0x83, 0x5C, 0x75, 0x0E, 0x27, 0xB0, 0x99, 0xE2, 0xCB, 0x14, 0x3D, 0x46, 0x6F,
            0x68, 0x41, 0x3A, 0x13, 0xCC, 0xE5, 0x9E, 0xB7, 0x20, 0x09, 0x72, 0x5B, 0x84, 0xAD, 0xD6, 0xFF,
            0xD8, 0xF1, 0x8A, 0xA3, 0x7C, 0x55, 0x2E, 0x07, 0x90, 0xB9, 0xC2, 0xEB, 0x34, 0x1D, 0x66, 0x4F,
            0x48, 0x61, 0x1A, 0x33, 0xEC, 0xC5, 0xBE, 0x97, 0x00, 0x29, 0x52, 0x7B, 0xA4, 0x8D, 0xF6, 0xDF,
            0xB8, 0x91, 0xEA, 0xC3, 0x1C, 0x35, 0x4E, 0x67, 0xF0, 0xD9, 0xA2, 0x8B, 0x54, 0x7D, 0x06, 0x2F,
            0x28, 0x01, 0x7A, 0x53, 0x8C, 0xA5, 0xDE, 0xF7, 0x60, 0x49, 0x32, 0x1B, 0xC4, 0xED, 0x96, 0xBF,
            0x98, 0xB1, 0xCA, 0xE3, 0x3C, 0x15, 0x6E, 0x47, 0xD0, 0xF9, 0x82, 0xAB, 0x74, 0x5D, 0x26, 0x0F,
            0x08, 0x21, 0x5A, 0x73, 0xAC, 0x85, 0xFE, 0xD7, 0x40, 0x69, 0x12, 0x3B, 0xE4, 0xCD, 0xB6, 0x9F,
            0x78, 0x51, 0x2A, 0x03, 0xDC, 0xF5, 0x8E, 0xA7, 0x30, 0x19, 0x62, 0x4B, 0x94, 0xBD, 0xC6, 0xEF,
            0xE8, 0xC1, 0xBA, 0x93, 0x4C, 0x65, 0x1E, 0x37, 0xA0, 0x89, 0xF2, 0xDB, 0x04, 0x2D, 0x56, 0x7F,
            0x58, 0x71, 0x0A, 0x23, 0xFC, 0xD5, 0xAE, 0x87, 0x10, 0x39, 0x42, 0x6B, 0xB4, 0x9D, 0xE6, 0xCF,
            0xC8, 0xE1, 0x9A, 0xB3, 0x6C, 0x45, 0x3E, 0x17, 0x80, 0xA9, 0xD2, 0xFB, 0x24, 0x0D, 0x76, 0x5F,
            0x38, 0x11, 0x6A, 0x43, 0x9C, 0xB5, 0xCE, 0xE7, 0x70, 0x59, 0x22, 0x0B, 0xD4, 0xFD, 0x86, 0xAF,
            0xA8, 0x81, 0xFA, 0xD3, 0x0C, 0x25, 0x5E, 0x77, 0xE0, 0xC9, 0xB2, 0x9B, 0x44, 0x6D, 0x16, 0x3F,
            0x18, 0x31, 0x4A, 0x63, 0xBC, 0x95, 0xEE, 0xC7, 0x50, 0x79, 0x02, 0x2B, 0xF4, 0xDD, 0xA6, 0x8F,
            0x88, 0xA1, 0xDA, 0xF3, 0x2C, 0x05, 0x7E, 0x57, 0xC0, 0xE9, 0x92, 0xBB, 0x64, 0x4D, 0x36, 0x1F
            ];

        /// <summary>
        /// Decodes a 3nK-encoded file.
        /// </summary>
        /// <param name="buffer">The buffer containing the encoded file.</param>
        /// <returns>The decoded file.</returns>
        public static byte[] Decode(byte[] buffer)
        {
            using var inMs = new MemoryStream(buffer);
            using var outMs = new MemoryStream();
            using var r = new BinaryReader(inMs);
            Decode(inMs, outMs);
            return outMs.ToArray();
        }

        /// <summary>
        /// Decodes a 3nK-encoded file.
        /// </summary>
        /// <param name="input">The stream containing the encoded file.</param>
        /// <param name="output">The stream to write the decoded file to.</param>
        public static void Decode(Stream input, Stream output)
        {
            using var r = new BinaryReader(input);
            var header = new ThreeNKHeader();
            header.Deserialize(r);
            Transcode(input, output, header.Seed);
        }

        /// <summary>
        /// Encodes a file to 3nK format.
        /// </summary>
        /// <param name="buffer">The buffer containing the file.</param>
        /// <param name="seed">The seed to use.</param>
        /// <returns>The encoded file.</returns>
        public static byte[] Encode(byte[] buffer, byte seed = 0)
        {
            using var inMs = new MemoryStream(buffer);
            using var outMs = new MemoryStream();
            Encode(inMs, outMs, seed);
            return outMs.ToArray();
        }

        /// <summary>
        /// Encodes a file to 3nK format.
        /// </summary>
        /// <param name="input">The stream containing the file.</param>
        /// <param name="output">The stream to write the encoded file to.</param>
        /// <param name="seed">The seed to use.</param>
        public static void Encode(Stream input, Stream output, byte seed = 0)
        {
            using var w = new BinaryWriter(output);
            var header = new ThreeNKHeader { Seed = seed };
            header.Serialize(w);
            Transcode(input, output, seed);
        }

        /// <summary>
        /// Transcodes bytes to or from 3nK.
        /// </summary>
        /// <param name="payload">The payload without a 3nK header.</param>
        /// <param name="seed">The seed to use.</param>
        public static void Transcode(byte[] payload, byte seed)
        {
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(payload[i] ^ KeyTable[(byte)(seed + i)]);
            }
        }

        /// <summary>
        /// Transcodes bytes to or from 3nK.
        /// </summary>
        /// <param name="input">The stream to read from.</param>
        /// <param name="output">The stream to write to.</param>
        /// <param name="seed">The seed to use.</param>
        public static void Transcode(Stream input, Stream output, byte seed)
        {
            int i = 0;
            while (input.Position < input.Length)
            {
                var b = (byte)input.ReadByte();
                b = (byte)(b ^ KeyTable[(byte)(seed + i++)]);
                output.WriteByte(b);
            }
        }
    }
}
