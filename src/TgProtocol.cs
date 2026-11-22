using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretControl
{
    // Decoded 64-byte obfuscated-2 client handshake.
    internal sealed class TgHandshake
    {
        internal int Dc;
        internal bool IsMedia;
        internal byte[] ProtoTag;
        internal byte[] PrekeyIv;
    }

    // Core wire protocol of the Telegram MTProto -> WebSocket bridge. Ported
    // from Zapret2UI (MIT) and Flowseal/tg-ws-proxy (MIT); only the "dd"
    // (padded intermediate) local transport is used because the client link
    // is loopback. See docs/TZ-telegram.md.
    internal static class TgProtocol
    {
        internal const int HandshakeLen = 64;
        internal const int SkipLen = 8;
        internal const int PrekeyLen = 32;
        internal const int KeyLen = 32;
        internal const int IvLen = 16;
        internal const int ProtoTagPos = 56;
        internal const int DcIdxPos = 60;
        internal const int TestDcOffset = 10000;

        internal static readonly byte[] ProtoTagAbridged =
            new byte[] { 0xef, 0xef, 0xef, 0xef };
        internal static readonly byte[] ProtoTagIntermediate =
            new byte[] { 0xee, 0xee, 0xee, 0xee };
        internal static readonly byte[] ProtoTagSecure =
            new byte[] { 0xdd, 0xdd, 0xdd, 0xdd };

        internal const uint ProtoAbridgedInt = 0xEFEFEFEF;
        internal const uint ProtoIntermediateInt = 0xEEEEEEEE;
        internal const uint ProtoPaddedIntermediateInt = 0xDDDDDDDD;

        internal const string DirectWsDomain = "web.telegram.org";

        private static readonly byte[][] ReservedStarts =
        {
            new byte[] { 0x48, 0x45, 0x41, 0x44 }, // HEAD
            new byte[] { 0x50, 0x4F, 0x53, 0x54 }, // POST
            new byte[] { 0x47, 0x45, 0x54, 0x20 }, // "GET "
            new byte[] { 0xee, 0xee, 0xee, 0xee },
            new byte[] { 0xdd, 0xdd, 0xdd, 0xdd },
            new byte[] { 0x16, 0x03, 0x01, 0x02 }
        };

        internal static int[] DcIndexes
        {
            get { return DataStore.DcIds; }
        }

        internal static string DcAddress(int dc)
        {
            return DataStore.DcAddress(dc);
        }

        internal static bool TryDirectTarget(int dc, out string address)
        {
            string sni;
            return DataStore.TryDirectTarget(NormalizeDc(dc), out address, out sni);
        }

        internal static int NormalizeDc(int dc)
        {
            if (dc == 203)
            {
                return 2;
            }
            if (dc >= 1 && dc <= 5)
            {
                return dc;
            }
            return 2;
        }

        internal static bool IsTestDc(int dc)
        {
            int index = dc - TestDcOffset;
            return index >= 1 && index <= 3;
        }

        internal static string[] WsDomains(int dc, bool isMedia)
        {
            int index = NormalizeDc(dc);
            string web = DataStore.WebHost;
            if (isMedia)
            {
                return new string[]
                {
                    "kws" + index + "-1." + web,
                    "kws" + index + "." + web
                };
            }
            return new string[]
            {
                "kws" + index + "." + web,
                "kws" + index + "-1." + web
            };
        }

        internal static TgHandshake TryHandshake(byte[] handshake, byte[] secret)
        {
            if (handshake == null || handshake.Length != HandshakeLen ||
                secret == null || secret.Length == 0)
            {
                return null;
            }
            byte[] prekeyIv = new byte[PrekeyLen + IvLen];
            Buffer.BlockCopy(handshake, SkipLen, prekeyIv, 0, prekeyIv.Length);
            byte[] prekey = Slice(prekeyIv, 0, PrekeyLen);
            byte[] iv = Slice(prekeyIv, PrekeyLen, IvLen);
            byte[] key = DeriveKey(prekey, secret);

            byte[] decrypted;
            using (TgAesCtr decryptor = new TgAesCtr(key, iv))
            {
                decrypted = decryptor.Update(handshake);
            }

            byte[] protoTag = Slice(decrypted, ProtoTagPos, 4);
            if (!Equal(protoTag, ProtoTagAbridged) &&
                !Equal(protoTag, ProtoTagIntermediate) &&
                !Equal(protoTag, ProtoTagSecure))
            {
                return null;
            }

            short dcIndex = (short)(decrypted[DcIdxPos] |
                                    (decrypted[DcIdxPos + 1] << 8));
            TgHandshake result = new TgHandshake();
            result.Dc = Math.Abs((int)dcIndex);
            result.IsMedia = dcIndex < 0;
            result.ProtoTag = protoTag;
            result.PrekeyIv = prekeyIv;
            return result;
        }

        internal static byte[] GenerateRelayInit(byte[] protoTag, int dcIndex)
        {
            byte[] random;
            while (true)
            {
                random = RandomBytes(HandshakeLen);
                if (random[0] == 0xef)
                {
                    continue;
                }
                bool reserved = false;
                for (int i = 0; i < ReservedStarts.Length; i++)
                {
                    if (Equal(Slice(random, 0, 4), ReservedStarts[i]))
                    {
                        reserved = true;
                        break;
                    }
                }
                if (reserved)
                {
                    continue;
                }
                if (random[4] == 0 && random[5] == 0 &&
                    random[6] == 0 && random[7] == 0)
                {
                    continue;
                }
                break;
            }

            byte[] encKey = Slice(random, SkipLen, PrekeyLen);
            byte[] encIv = Slice(random, SkipLen + PrekeyLen, IvLen);

            byte[] tailPlain = new byte[8];
            Buffer.BlockCopy(protoTag, 0, tailPlain, 0, 4);
            WriteInt16LittleEndian(tailPlain, 4, (short)dcIndex);
            Buffer.BlockCopy(RandomBytes(2), 0, tailPlain, 6, 2);

            byte[] encryptedFull;
            using (TgAesCtr encryptor = new TgAesCtr(encKey, encIv))
            {
                encryptedFull = encryptor.Update(random);
            }

            byte[] result = (byte[])random.Clone();
            for (int i = 0; i < 8; i++)
            {
                byte keyStream = (byte)(encryptedFull[ProtoTagPos + i] ^
                                        random[ProtoTagPos + i]);
                result[ProtoTagPos + i] = (byte)(tailPlain[i] ^ keyStream);
            }
            return result;
        }

        internal static byte[] GenerateClientInit(
            byte[] protoTag, int dcIndex, byte[] secret)
        {
            byte[] random;
            while (true)
            {
                random = RandomBytes(HandshakeLen);
                if (random[0] == 0xef)
                {
                    continue;
                }
                bool reserved = false;
                for (int i = 0; i < ReservedStarts.Length; i++)
                {
                    if (Equal(Slice(random, 0, 4), ReservedStarts[i]))
                    {
                        reserved = true;
                        break;
                    }
                }
                if (reserved)
                {
                    continue;
                }
                if (random[4] == 0 && random[5] == 0 &&
                    random[6] == 0 && random[7] == 0)
                {
                    continue;
                }
                break;
            }

            byte[] prekey = Slice(random, SkipLen, PrekeyLen);
            byte[] encKey = DeriveKey(prekey, secret);
            byte[] encIv = Slice(random, SkipLen + PrekeyLen, IvLen);

            byte[] tailPlain = new byte[8];
            Buffer.BlockCopy(protoTag, 0, tailPlain, 0, 4);
            WriteInt16LittleEndian(tailPlain, 4, (short)dcIndex);
            Buffer.BlockCopy(RandomBytes(2), 0, tailPlain, 6, 2);

            byte[] encryptedFull;
            using (TgAesCtr encryptor = new TgAesCtr(encKey, encIv))
            {
                encryptedFull = encryptor.Update(random);
            }

            byte[] result = (byte[])random.Clone();
            for (int i = 0; i < 8; i++)
            {
                byte keyStream = (byte)(encryptedFull[ProtoTagPos + i] ^
                                        random[ProtoTagPos + i]);
                result[ProtoTagPos + i] = (byte)(tailPlain[i] ^ keyStream);
            }
            return result;
        }

        internal static TgCryptoContext BuildCryptoContext(
            byte[] clientPrekeyIv, byte[] secret, byte[] relayInit)
        {
            byte[] clientDecryptPrekey = Slice(clientPrekeyIv, 0, PrekeyLen);
            byte[] clientDecryptIv = Slice(clientPrekeyIv, PrekeyLen, IvLen);
            byte[] clientDecryptKey = DeriveKey(clientDecryptPrekey, secret);

            byte[] clientEncryptPrekeyIv = Reversed(clientPrekeyIv);
            byte[] clientEncryptKey = DeriveKey(
                Slice(clientEncryptPrekeyIv, 0, PrekeyLen), secret);
            byte[] clientEncryptIv = Slice(
                clientEncryptPrekeyIv, PrekeyLen, IvLen);

            TgAesCtr clientDecryptor = new TgAesCtr(
                clientDecryptKey, clientDecryptIv);
            TgAesCtr clientEncryptor = new TgAesCtr(
                clientEncryptKey, clientEncryptIv);
            clientDecryptor.Update(new byte[HandshakeLen]);

            byte[] relayEncryptKey = Slice(relayInit, SkipLen, PrekeyLen);
            byte[] relayEncryptIv = Slice(
                relayInit, SkipLen + PrekeyLen, IvLen);

            byte[] relayDecryptPrekeyIv = Reversed(
                Slice(relayInit, SkipLen, PrekeyLen + IvLen));
            byte[] relayDecryptKey = Slice(relayDecryptPrekeyIv, 0, KeyLen);
            byte[] relayDecryptIv = Slice(relayDecryptPrekeyIv, KeyLen, IvLen);

            TgAesCtr telegramEncryptor = new TgAesCtr(
                relayEncryptKey, relayEncryptIv);
            TgAesCtr telegramDecryptor = new TgAesCtr(
                relayDecryptKey, relayDecryptIv);
            telegramEncryptor.Update(new byte[HandshakeLen]);

            return new TgCryptoContext(
                clientDecryptor, clientEncryptor,
                telegramEncryptor, telegramDecryptor);
        }

        // Sends one unauthenticated req_pq_multi through an upgraded WebSocket
        // and waits for any bytes back: only a path that really reaches a live
        // data centre answers, so this separates a working front from one that
        // accepts the upgrade and relays nothing.
        internal static async Task<bool> ProbeRelayAsync(
            TgWebSocket ws, int dc, int timeoutMs, CancellationToken ct)
        {
            byte[] relayInit = GenerateRelayInit(ProtoTagIntermediate, dc);
            using (TgAesCtr encryptor = new TgAesCtr(
                Slice(relayInit, SkipLen, PrekeyLen),
                Slice(relayInit, SkipLen + PrekeyLen, IvLen)))
            {
                encryptor.Update(new byte[HandshakeLen]);

                byte[] message = new byte[8 + 8 + 4 + 20];
                long messageId = CurrentTelegramMessageId();
                WriteInt64LittleEndian(message, 8, messageId);
                WriteInt32LittleEndian(message, 16, 20);
                WriteUInt32LittleEndian(message, 20, 0xbe7e8ef1);
                Buffer.BlockCopy(RandomBytes(16), 0, message, 24, 16);

                byte[] framed = new byte[4 + message.Length];
                WriteInt32LittleEndian(framed, 0, message.Length);
                Buffer.BlockCopy(message, 0, framed, 4, message.Length);
                byte[] encrypted = encryptor.Update(framed);

                await ws.SendAsync(relayInit, ct).ConfigureAwait(false);
                await ws.SendAsync(encrypted, ct).ConfigureAwait(false);

                Task<byte[]> receive = ws.RecvAsync(ct);
                Task finished = await Task.WhenAny(
                    receive, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
                if (finished != receive)
                {
                    Observe(receive);
                    return false;
                }
                byte[] response = await receive.ConfigureAwait(false);
                return response != null && response.Length > 0;
            }
        }

        internal static long CurrentTelegramMessageId()
        {
            long seconds = (long)(DateTime.UtcNow -
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return (seconds << 32) & ~3L;
        }

        internal static uint ProtoInt(byte[] protoTag)
        {
            if (Equal(protoTag, ProtoTagAbridged))
            {
                return ProtoAbridgedInt;
            }
            if (Equal(protoTag, ProtoTagIntermediate))
            {
                return ProtoIntermediateInt;
            }
            return ProtoPaddedIntermediateInt;
        }

        internal static byte[] RandomBytes(int length)
        {
            byte[] value = new byte[length];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(value);
            }
            return value;
        }

        internal static int RandomInt(int exclusiveMax)
        {
            if (exclusiveMax <= 1)
            {
                return 0;
            }
            byte[] value = RandomBytes(4);
            uint raw = (uint)(value[0] | (value[1] << 8) |
                              (value[2] << 16) | (value[3] << 24));
            return (int)(raw % (uint)exclusiveMax);
        }

        internal static byte[] DeriveKey(byte[] prekey, byte[] secret)
        {
            byte[] buffer = new byte[prekey.Length + secret.Length];
            Buffer.BlockCopy(prekey, 0, buffer, 0, prekey.Length);
            Buffer.BlockCopy(secret, 0, buffer, prekey.Length, secret.Length);
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(buffer);
            }
        }

        internal static byte[] Slice(byte[] source, int offset, int length)
        {
            byte[] value = new byte[length];
            Buffer.BlockCopy(source, offset, value, 0, length);
            return value;
        }

        internal static byte[] Reversed(byte[] source)
        {
            byte[] value = (byte[])source.Clone();
            Array.Reverse(value);
            return value;
        }

        internal static bool Equal(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }
            return true;
        }

        internal static string ToHex(byte[] data)
        {
            if (data == null)
            {
                return string.Empty;
            }
            char[] chars = new char[data.Length * 2];
            const string digits = "0123456789abcdef";
            for (int i = 0; i < data.Length; i++)
            {
                chars[i * 2] = digits[data[i] >> 4];
                chars[i * 2 + 1] = digits[data[i] & 0x0F];
            }
            return new string(chars);
        }

        internal static byte[] FromHex(string hex)
        {
            if (hex == null || (hex.Length & 1) != 0)
            {
                return null;
            }
            byte[] value = new byte[hex.Length / 2];
            for (int i = 0; i < value.Length; i++)
            {
                int high = HexDigit(hex[i * 2]);
                int low = HexDigit(hex[i * 2 + 1]);
                if (high < 0 || low < 0)
                {
                    return null;
                }
                value[i] = (byte)((high << 4) | low);
            }
            return value;
        }

        private static int HexDigit(char value)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }
            if (value >= 'a' && value <= 'f')
            {
                return value - 'a' + 10;
            }
            if (value >= 'A' && value <= 'F')
            {
                return value - 'A' + 10;
            }
            return -1;
        }

        internal static void WriteInt16LittleEndian(
            byte[] buffer, int offset, short value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        internal static void WriteInt32LittleEndian(
            byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        internal static void WriteUInt32LittleEndian(
            byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        internal static void WriteInt64LittleEndian(
            byte[] buffer, int offset, long value)
        {
            for (int i = 0; i < 8; i++)
            {
                buffer[offset + i] = (byte)((value >> (8 * i)) & 0xFF);
            }
        }

        internal static int ReadInt32LittleEndian(byte[] buffer, int offset)
        {
            return buffer[offset] |
                   (buffer[offset + 1] << 8) |
                   (buffer[offset + 2] << 16) |
                   (buffer[offset + 3] << 24);
        }

        internal static long ReadInt64BigEndian(byte[] buffer, int offset)
        {
            long value = 0;
            for (int i = 0; i < 8; i++)
            {
                value = (value << 8) | buffer[offset + i];
            }
            return value;
        }

        internal static void WriteInt64BigEndian(
            byte[] buffer, int offset, long value)
        {
            for (int i = 0; i < 8; i++)
            {
                buffer[offset + i] = (byte)((value >> (8 * (7 - i))) & 0xFF);
            }
        }

        private static void Observe(Task task)
        {
            task.ContinueWith(delegate(Task completed)
            {
                Exception ignored = completed.Exception;
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
