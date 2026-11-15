// Adapted from BmsLightBridge (https://github.com/RedMeKool/BmsLightBridge),
// Services/Icp/IcpHidDevice.cs.
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

using HidSharp;

namespace ObsMuteWarning.Icp;

/// <summary>
/// Low-level USB HID communication with the WinWing ViperAce ICP.
/// Opens the device non-exclusively so it can coexist with SimAppPro / the
/// WinWing background service that keeps its own HID handle open.
/// </summary>
internal sealed class IcpHidDevice : IDisposable
{
    public const ushort VendorId = 0x4098;
    public const ushort ProductId = 0xBF06;

    private const int PayloadSize = 60;
    private const int PacketSize = 64;
    private const int SendBufferSize = 2048;

    private readonly HidStream _stream;
    private byte _seqNum = 1;
    private readonly byte[] _sendBuffer = new byte[SendBufferSize];
    private readonly byte[] _packetBuffer = new byte[PacketSize];

    public IcpHidDevice()
    {
        var device = DeviceList.Local
            .GetHidDevices(VendorId, ProductId)
            .OrderByDescending(d => SafeMaxOutputReportLength(d) >= PacketSize)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("WinWing ICP (VID 0x4098 / PID 0xBF06) not found.");

        var cfg = new OpenConfiguration();
        cfg.SetOption(OpenOption.Exclusive, false);

        _stream = device.Open(cfg);
        _stream.WriteTimeout = 500;

        // Session-start packet: OpType 0x02
        WritePacket(0x02, 0x00, new byte[] { 0 }, 0, 1);
    }

    public static bool IsPresent() => DeviceList.Local.GetHidDevices(VendorId, ProductId).Any();

    /// <summary>ICP brightness channel: 0 = button backlight, 1 = DED screen backlight.</summary>
    public const byte ScreenBacklightChannel = 1;

    /// <summary>
    /// Sets a brightness channel (0-255). Report layout from BmsLightBridge's WinWingService
    /// (captured from SimAppPro): 02 06 BF 00 00 03 49 [channel] [value] 00 00 00 00 00.
    /// </summary>
    public void SetBrightness(byte channel, byte value)
    {
        Array.Clear(_packetBuffer, 0, PacketSize);
        _packetBuffer[0] = 0x02;
        _packetBuffer[1] = 0x06;
        _packetBuffer[2] = 0xBF;
        _packetBuffer[5] = 0x03;
        _packetBuffer[6] = 0x49;
        _packetBuffer[7] = channel;
        _packetBuffer[8] = value;
        _stream.Write(_packetBuffer);
    }

    /// <summary>Serialises the commands and sends them as a sequence of 60-byte HID payload packets.</summary>
    public void WriteDedCommands(IEnumerable<DedCommand> commands)
    {
        int totalBytes = 0;
        foreach (var cmd in commands)
        {
            cmd.WriteTo(_sendBuffer, totalBytes);
            totalBytes += cmd.SerializedSize;
        }

        for (int start = 0; start < totalBytes; start += PayloadSize)
        {
            int payloadLen = Math.Min(PayloadSize, totalBytes - start);
            WritePacket(0x00, _seqNum, _sendBuffer, start, payloadLen);
            _seqNum = (byte)((_seqNum + 1) & 0xFF);
        }
    }

    private void WritePacket(byte opType, byte seqNum, byte[] source, int sourceOffset, int length)
    {
        Array.Clear(_packetBuffer, 0, PacketSize);
        _packetBuffer[0] = 0xF0;
        _packetBuffer[1] = opType;
        _packetBuffer[2] = seqNum;
        _packetBuffer[3] = (byte)length;
        Array.Copy(source, sourceOffset, _packetBuffer, 4, length);
        _stream.Write(_packetBuffer);
    }

    private static int SafeMaxOutputReportLength(HidDevice d)
    {
        try { return d.GetMaxOutputReportLength(); }
        catch { return 0; }
    }

    public void Dispose() => _stream.Dispose();
}
