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

        public string Decrypt(string hexCipherText)
        {
            if (string.IsNullOrEmpty(hexCipherText))
                return string.Empty;

            // Hex 문자열을 바이트 배열로 변환
            byte[] input;
            try
            {
                int length = hexCipherText.Length / 2;
                input = new byte[length];
                for (int i = 0; i < length; i++)
                    input[i] = Convert.ToByte(hexCipherText.Substring(i * 2, 2), 16);
            }
            catch
            {
                return string.Empty;
            }

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

            byte[] output = new byte[input.Length];
            byte[] seqX = new byte[DEFAULT_ROUNDS];
            byte[] seqM = new byte[DEFAULT_ROUNDS];

            for (int i = 0; i < input.Length; i++)
            {
                // 각 라운드에 대한 상수 계산
                for (int j = 0; j < DEFAULT_ROUNDS; j++)
                {
                    seqX[j] = prng.Rand();
                    seqM[j] = prng.Rand();
                }

                byte plain = input[i];

                // 마지막 라운드부터 역순으로 처리
                for (int j = DEFAULT_ROUNDS - 1; j > 0; j--)
                {
                    if (seqM[j] <= plain)
                        plain = (byte)(plain - seqM[j]);
                    else
                        plain = (byte)((plain + (byte)(~seqM[j])) + 1);
                    plain ^= seqX[j];
                }

                // 첫번째 라운드
                if (seqM[0] <= plain)
                    plain = (byte)(plain - seqM[0]);
                else
                    plain = (byte)((plain + (byte)(~seqM[0])) + 1);
                plain ^= (byte)(ucPrev ^ seqX[0]);

                output[i] = plain;
                ucPrev ^= plain;
            }

            return Encoding.UTF8.GetString(output);
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
