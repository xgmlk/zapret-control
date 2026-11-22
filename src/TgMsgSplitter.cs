using System;
using System.Collections.Generic;

namespace ZapretControl
{
    // Splits the re-encrypted client-to-Telegram stream into individual MTProto
    // transport packets so each is delivered as one WebSocket frame (what the
    // /apiws endpoint expects). A parallel decryptor, seeded like the Telegram
    // encryptor, reads plaintext packet lengths while the ciphertext is
    // forwarded untouched.
    internal sealed class TgMsgSplitter : IDisposable
    {
        private readonly TgAesCtr _decryptor;
        private readonly uint _protocol;
        private readonly List<byte> _cipher = new List<byte>();
        private readonly List<byte> _plain = new List<byte>();
        private bool _disabled;

        internal TgMsgSplitter(byte[] relayInit, uint protocol)
        {
            _decryptor = new TgAesCtr(
                TgProtocol.Slice(relayInit, TgProtocol.SkipLen,
                    TgProtocol.PrekeyLen),
                TgProtocol.Slice(relayInit,
                    TgProtocol.SkipLen + TgProtocol.PrekeyLen,
                    TgProtocol.IvLen));
            _decryptor.Update(new byte[TgProtocol.HandshakeLen]);
            _protocol = protocol;
        }

        internal List<byte[]> Split(byte[] chunk)
        {
            List<byte[]> parts = new List<byte[]>();
            if (chunk == null || chunk.Length == 0)
            {
                return parts;
            }
            if (_disabled)
            {
                parts.Add(chunk);
                return parts;
            }

            _cipher.AddRange(chunk);
            _plain.AddRange(_decryptor.Update(chunk));

            int offset = 0;
            int bufferLength = _cipher.Count;
            while (offset < bufferLength)
            {
                bool unknown;
                int packetLength = NextPacketLength(
                    offset, bufferLength - offset, out unknown);
                if (unknown)
                {
                    // A packet length we cannot parse: stop splitting and send
                    // the rest verbatim rather than corrupt the stream.
                    break;
                }
                if (packetLength < 0)
                {
                    break;
                }
                if (packetLength == 0)
                {
                    parts.Add(_cipher.GetRange(
                        offset, bufferLength - offset).ToArray());
                    offset = bufferLength;
                    _disabled = true;
                    break;
                }
                parts.Add(_cipher.GetRange(offset, packetLength).ToArray());
                offset += packetLength;
            }

            if (offset > 0)
            {
                _cipher.RemoveRange(0, offset);
                _plain.RemoveRange(0, offset);
            }
            return parts;
        }

        internal List<byte[]> Flush()
        {
            List<byte[]> parts = new List<byte[]>();
            if (_cipher.Count == 0)
            {
                return parts;
            }
            parts.Add(_cipher.ToArray());
            _cipher.Clear();
            _plain.Clear();
            return parts;
        }

        // -1 = need more bytes, 0 = unknown framing (stop splitting),
        // >0 = complete packet length including its header.
        private int NextPacketLength(
            int offset, int available, out bool unknown)
        {
            unknown = false;
            if (available <= 0)
            {
                return -1;
            }
            if (_protocol == TgProtocol.ProtoAbridgedInt)
            {
                return NextAbridgedLength(offset, available, out unknown);
            }
            if (_protocol == TgProtocol.ProtoIntermediateInt ||
                _protocol == TgProtocol.ProtoPaddedIntermediateInt)
            {
                return NextIntermediateLength(offset, available);
            }
            unknown = true;
            return 0;
        }

        private int NextAbridgedLength(
            int offset, int available, out bool unknown)
        {
            unknown = false;
            byte first = _plain[offset];
            int payloadLength;
            int headerLength;
            if (first == 0x7F || first == 0xFF)
            {
                if (available < 4)
                {
                    return -1;
                }
                payloadLength = (_plain[offset + 1] |
                                 (_plain[offset + 2] << 8) |
                                 (_plain[offset + 3] << 16)) * 4;
                headerLength = 4;
            }
            else
            {
                payloadLength = (first & 0x7F) * 4;
                headerLength = 1;
            }
            if (payloadLength <= 0)
            {
                unknown = true;
                return 0;
            }
            int packetLength = headerLength + payloadLength;
            if (available < packetLength)
            {
                return -1;
            }
            return packetLength;
        }

        private int NextIntermediateLength(int offset, int available)
        {
            if (available < 4)
            {
                return -1;
            }
            uint payloadLength =
                (uint)(_plain[offset] |
                       (_plain[offset + 1] << 8) |
                       (_plain[offset + 2] << 16) |
                       (_plain[offset + 3] << 24)) & 0x7FFFFFFFu;
            if (payloadLength == 0)
            {
                return 0;
            }
            long packetLength = 4 + payloadLength;
            if (available < packetLength)
            {
                return -1;
            }
            return (int)packetLength;
        }

        public void Dispose()
        {
            _decryptor.Dispose();
        }
    }
}
