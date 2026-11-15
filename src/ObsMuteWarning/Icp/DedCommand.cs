// Adapted from BmsLightBridge (https://github.com/RedMeKool/BmsLightBridge),
// Services/Icp/DedCommand.cs.
//
// MIT License
// Copyright (c) 2026 RedMeKool
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace ObsMuteWarning.Icp;

/// <summary>
/// A DED command serialised into the HID send buffer.
///
/// Layout (little-endian):
///   Bytes  0- 3 : ProductId  (0x0000BF06)
///   Bytes  4- 7 : CommandType
///   Bytes  8-11 : Timestamp
///   Byte  12    : 0x00 (padding)
///   Bytes 13-16 : DataBuffer length
///   Bytes 17+   : DataBuffer
/// </summary>
internal sealed class DedCommand
{
    public const uint CmdWriteDisplayMem = 0x0102;
    public const uint CmdRefreshDisplay = 0x0103;

    public static readonly byte[] RefreshPayload = { 0 };

    public uint ProductId { get; init; } = 0x0000BF06;
    public uint CommandType { get; init; } = CmdRefreshDisplay;
    public uint TimeStamp { get; init; } = 0xFFFF;
    public byte[] DataBuffer { get; init; } = Array.Empty<byte>();

    public int SerializedSize => 17 + DataBuffer.Length;

    public void WriteTo(byte[] dest, int offset)
    {
        BitConverter.TryWriteBytes(dest.AsSpan(offset, 4), ProductId);
        BitConverter.TryWriteBytes(dest.AsSpan(offset + 4, 4), CommandType);
        BitConverter.TryWriteBytes(dest.AsSpan(offset + 8, 4), TimeStamp);
        dest[offset + 12] = 0x00;
        BitConverter.TryWriteBytes(dest.AsSpan(offset + 13, 4), (uint)DataBuffer.Length);
        DataBuffer.CopyTo(dest, offset + 17);
    }
}
