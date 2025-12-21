using System;
using System.Text;

namespace WindowsOptimizer.Services
{
    /// <summary>
    /// XOR256 스트림 암호화 서비스
    /// C++ jhcrypt.cpp의 C# 포팅
    /// 암호화 키: TGA_util
    /// </summary>
    public class Xor256CryptoService
    {
        private static readonly Lazy<Xor256CryptoService> _instance =
            new Lazy<Xor256CryptoService>(() => new Xor256CryptoService());
        public static Xor256CryptoService Instance => _instance.Value;

        private const string CRYPTO_PW = "TGA_util";
        private const int KEY_MAX = 256;
        private const int DEFAULT_ROUNDS = 4;

        private Xor256CryptoService() { }

        /// <summary>
        /// 문자열을 XOR256 암호화 후 HEX 문자열로 반환
        /// </summary>
        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            var stream = new Xor256Stream();
            stream.Initialize(CRYPTO_PW, DEFAULT_ROUNDS);

            byte[] input = Encoding.UTF8.GetBytes(plainText);
            byte[] output = new byte[input.Length];

            stream.ResetChain();
            stream.Encrypt(input, output);

            // Binary to Hex
            return BitConverter.ToString(output).Replace("-", "");
        }

        /// <summary>
        /// HEX 문자열을 복호화하여 평문 반환
        /// </summary>
        public string Decrypt(string hexText)
        {
            if (string.IsNullOrEmpty(hexText))
                return string.Empty;

            var stream = new Xor256Stream();
            stream.Initialize(CRYPTO_PW, DEFAULT_ROUNDS);

            // Hex to Binary
            byte[] input = HexToBytes(hexText);
            byte[] output = new byte[input.Length];

            stream.ResetChain();
            stream.Decrypt(input, output);

            // null 제거
            int len = Array.IndexOf(output, (byte)0);
            if (len < 0) len = output.Length;

            return Encoding.UTF8.GetString(output, 0, len);
        }

        private byte[] HexToBytes(string hex)
        {
            int len = hex.Length / 2;
            byte[] bytes = new byte[len];
            for (int i = 0; i < len; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
    }

    /// <summary>
    /// Arcfour (RC4) PRNG 구현
    /// </summary>
    internal class ArcfourPrng
    {
        private readonly byte[] _state0 = new byte[256];
        private readonly byte[] _state = new byte[256];
        private byte _i;
        private byte _j;
        private bool _initialized;

        public void SetKey(byte[] keyData)
        {
            if (keyData == null || keyData.Length < 1)
                throw new ArgumentException("Key data is required");

            // Initialize S-box
            for (int i = 0; i < 256; i++)
                _state0[i] = (byte)i;

            _i = 0;
            _j = 0;

            // Key scheduling
            int keyIndex = 0;
            for (int i = 0; i < 256; i++)
            {
                _j = (byte)(_j + _state0[i] + keyData[keyIndex]);
                // Swap
                byte temp = _state0[i];
                _state0[i] = _state0[_j];
                _state0[_j] = temp;

                keyIndex = (keyIndex + 1) % keyData.Length;
            }

            Array.Copy(_state0, _state, 256);
            _i = 0;
            _j = 0;
            _initialized = true;
        }

        public void Reset()
        {
            if (!_initialized)
                throw new InvalidOperationException("PRNG not initialized");

            Array.Copy(_state0, _state, 256);
            _i = 0;
            _j = 0;
        }

        public byte Rand()
        {
            if (!_initialized)
                throw new InvalidOperationException("PRNG not initialized");

            _i++;
            _j = (byte)(_j + _state[_i]);

            // Swap
            byte temp = _state[_i];
            _state[_i] = _state[_j];
            _state[_j] = temp;

            return _state[(_state[_i] + _state[_j]) & 0xFF];
        }
    }

    /// <summary>
    /// XOR256 스트림 암호화 구현
    /// </summary>
    internal class Xor256Stream
    {
        private const int KEY_MAX = 256;

        private readonly ArcfourPrng _prng = new ArcfourPrng();
        private byte[] _key;
        private int _rounds;
        private byte _ucPrev0;
        private byte _ucPrev;
        private bool _initialized;

        public void Initialize(string keyData, int rounds = 4)
        {
            if (string.IsNullOrEmpty(keyData))
                throw new ArgumentException("Key data is required");
            if (rounds < 1)
                throw new ArgumentException("Rounds must be at least 1");

            _rounds = rounds;

            // 키를 256바이트로 확장
            _key = new byte[KEY_MAX];
            byte[] keyBytes = Encoding.UTF8.GetBytes(keyData);
            for (int i = 0, j = 0; i < KEY_MAX; i++, j = (j + 1) % keyBytes.Length)
            {
                _key[i] = keyBytes[j];
            }

            _prng.SetKey(_key);
            _ucPrev0 = _prng.Rand();
            _ucPrev = _ucPrev0;
            _initialized = true;
        }

        public void ResetChain()
        {
            if (!_initialized)
                throw new InvalidOperationException("Stream not initialized");

            _prng.Reset();
            _prng.Rand(); // 첫 번째 Rand() 건너뛰기
            _ucPrev = _ucPrev0;
        }

        public void Encrypt(byte[] input, byte[] output)
        {
            if (!_initialized)
                throw new InvalidOperationException("Stream not initialized");

            for (int i = 0; i < input.Length; i++)
            {
                // First round
                _ucPrev ^= input[i];
                byte cipher = (byte)((_ucPrev ^ _prng.Rand()) + _prng.Rand());

                // Remaining rounds
                for (int j = 1; j < _rounds; j++)
                {
                    cipher ^= _prng.Rand();
                    cipher = (byte)(cipher + _prng.Rand());
                }

                output[i] = cipher;
            }
        }

        public void Decrypt(byte[] input, byte[] output)
        {
            if (!_initialized)
                throw new InvalidOperationException("Stream not initialized");

            byte[] seqX = new byte[_rounds];
            byte[] seqM = new byte[_rounds];

            for (int i = 0; i < input.Length; i++)
            {
                // 상수 계산
                for (int j = 0; j < _rounds; j++)
                {
                    seqX[j] = _prng.Rand();
                    seqM[j] = _prng.Rand();
                }

                // 마지막 라운드부터 역순으로
                byte plain = input[i];
                for (int j = _rounds - 1; j > 0; j--)
                {
                    if (seqM[j] <= plain)
                        plain = (byte)(plain - seqM[j]);
                    else
                        plain = (byte)((plain + (byte)(~seqM[j])) + 1);

                    plain ^= seqX[j];
                }

                // First round
                if (seqM[0] <= plain)
                    plain = (byte)(plain - seqM[0]);
                else
                    plain = (byte)((plain + (byte)(~seqM[0])) + 1);

                plain ^= (byte)(_ucPrev ^ seqX[0]);
                output[i] = plain;
                _ucPrev ^= plain;
            }
        }
    }
}
