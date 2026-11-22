using System;
using System.Security.Cryptography;

namespace ZapretControl
{
    // AES-256-CTR for the Telegram MTProto proxy. .NET Framework has no
    // streaming CTR mode, so AES-ECB drives a big-endian 128-bit counter and
    // the leftover keystream is carried between calls. The port follows
    // Zapret2UI's AesCtr (MIT), which in turn follows Flowseal/tg-ws-proxy.
    internal sealed class TgAesCtr : IDisposable
    {
        private const int BatchBlocks = 256;

        private readonly Aes _aes;
        private readonly ICryptoTransform _ecb;
        private readonly byte[] _counter = new byte[16];
        private readonly byte[] _counters = new byte[BatchBlocks * 16];
        private readonly byte[] _keystream = new byte[BatchBlocks * 16];
        private int _keystreamPos;
        private int _keystreamLen;

        internal TgAesCtr(byte[] key, byte[] iv)
        {
            if (key == null || key.Length != 32)
            {
                throw new ArgumentException("AES-256 key must be 32 bytes.", "key");
            }
            if (iv == null || iv.Length != 16)
            {
                throw new ArgumentException("AES-CTR IV must be 16 bytes.", "iv");
            }
            _aes = Aes.Create();
            _aes.Mode = CipherMode.ECB;
            _aes.Padding = PaddingMode.None;
            _aes.Key = key;
            _ecb = _aes.CreateEncryptor();
            Buffer.BlockCopy(iv, 0, _counter, 0, 16);
        }

        internal byte[] Update(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return new byte[0];
            }
            byte[] output = new byte[data.Length];
            int offset = 0;
            while (offset < data.Length)
            {
                if (_keystreamPos == _keystreamLen)
                {
                    int blocks = (data.Length - offset + 15) / 16;
                    if (blocks > BatchBlocks)
                    {
                        blocks = BatchBlocks;
                    }
                    for (int block = 0; block < blocks; block++)
                    {
                        Buffer.BlockCopy(_counter, 0, _counters, block * 16, 16);
                        IncrementCounter();
                    }
                    _ecb.TransformBlock(_counters, 0, blocks * 16, _keystream, 0);
                    _keystreamPos = 0;
                    _keystreamLen = blocks * 16;
                }
                int take = data.Length - offset;
                if (take > _keystreamLen - _keystreamPos)
                {
                    take = _keystreamLen - _keystreamPos;
                }
                for (int i = 0; i < take; i++)
                {
                    output[offset + i] =
                        (byte)(data[offset + i] ^ _keystream[_keystreamPos + i]);
                }
                offset += take;
                _keystreamPos += take;
            }
            return output;
        }

        private void IncrementCounter()
        {
            for (int i = 15; i >= 0; i--)
            {
                if (++_counter[i] != 0)
                {
                    break;
                }
            }
        }

        public void Dispose()
        {
            if (_ecb != null)
            {
                _ecb.Dispose();
            }
            if (_aes != null)
            {
                _aes.Dispose();
            }
        }
    }

    // The four AES-CTR streams of one bridged connection: the client leg
    // (decrypt/encrypt) and the Telegram leg (encrypt/decrypt).
    internal sealed class TgCryptoContext : IDisposable
    {
        internal readonly TgAesCtr ClientDecrypt;
        internal readonly TgAesCtr ClientEncrypt;
        internal readonly TgAesCtr TelegramEncrypt;
        internal readonly TgAesCtr TelegramDecrypt;

        internal TgCryptoContext(
            TgAesCtr clientDecrypt,
            TgAesCtr clientEncrypt,
            TgAesCtr telegramEncrypt,
            TgAesCtr telegramDecrypt)
        {
            ClientDecrypt = clientDecrypt;
            ClientEncrypt = clientEncrypt;
            TelegramEncrypt = telegramEncrypt;
            TelegramDecrypt = telegramDecrypt;
        }

        public void Dispose()
        {
            ClientDecrypt.Dispose();
            ClientEncrypt.Dispose();
            TelegramEncrypt.Dispose();
            TelegramDecrypt.Dispose();
        }
    }
}
