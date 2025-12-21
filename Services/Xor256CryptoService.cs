using System;
using System.Text;

namespace WindowsOptimizer.Services
{
    public class Xor256CryptoService
    {
        private static readonly Lazy<Xor256CryptoService> _instance =
            new Lazy<Xor256CryptoService>(() => new Xor256CryptoService());
        public static Xor256CryptoService Instance => _instance.Value;

        private const string CRYPTO_PW = "TGA_util";
        private const int KEY_MAX = 256;
        private const int DEFAULT_ROUNDS = 4;

        private Xor256CryptoService() { }

        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            var prng = new ArcfourPrng();
            byte[] key = new byte[KEY_MAX];
            byte[] keyBytes = Encoding.UTF8.GetBytes(CRYPTO_PW);

            for (int i = 0, j = 0; i < KEY_MAX; i++, j = (j + 1) % keyBytes.Length)
                key[i] = keyBytes[j];

            prng.SetKey(key);
            byte ucPrev0 = prng.Rand();

            prng.Reset();
            prng.Rand();
            byte ucPrev = ucPrev0;

            byte[] input = Encoding.UTF8.GetBytes(plainText);
            byte[] output = new byte[input.Length];

            for (int i = 0; i < input.Length; i++)
            {
                ucPrev ^= input[i];
                byte cipher = (byte)((ucPrev ^ prng.Rand()) + prng.Rand());

                for (int j = 1; j < DEFAULT_ROUNDS; j++)
                {
                    cipher ^= prng.Rand();
                    cipher = (byte)(cipher + prng.Rand());
                }

                output[i] = cipher;
            }

            return BitConverter.ToString(output).Replace("-", "");
        }

        private class ArcfourPrng
        {
            private readonly byte[] _state0 = new byte[256];
            private readonly byte[] _state = new byte[256];
            private byte _i;
            private byte _j;

            public void SetKey(byte[] keyData)
            {
                for (int i = 0; i < 256; i++)
                    _state0[i] = (byte)i;

                _i = 0;
                _j = 0;

                int keyIndex = 0;
                for (int i = 0; i < 256; i++)
                {
                    _j = (byte)(_j + _state0[i] + keyData[keyIndex]);
                    byte temp = _state0[i];
                    _state0[i] = _state0[_j];
                    _state0[_j] = temp;
                    keyIndex = (keyIndex + 1) % keyData.Length;
                }

                Array.Copy(_state0, _state, 256);
                _i = 0;
                _j = 0;
            }

            public void Reset()
            {
                Array.Copy(_state0, _state, 256);
                _i = 0;
                _j = 0;
            }

            public byte Rand()
            {
                _i++;
                _j = (byte)(_j + _state[_i]);
                byte temp = _state[_i];
                _state[_i] = _state[_j];
                _state[_j] = temp;
                return _state[(_state[_i] + _state[_j]) & 0xFF];
            }
        }
    }
}
