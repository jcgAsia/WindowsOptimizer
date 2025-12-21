Add-Type -TypeDefinition @"
using System;
using System.Text;

public class ArcfourPrng {
    private byte[] _state0 = new byte[256];
    private byte[] _state = new byte[256];
    private byte _i, _j;

    public void SetKey(byte[] keyData) {
        for (int i = 0; i < 256; i++) _state0[i] = (byte)i;
        _i = 0; _j = 0;
        int keyIndex = 0;
        for (int i = 0; i < 256; i++) {
            _j = (byte)(_j + _state0[i] + keyData[keyIndex]);
            byte temp = _state0[i];
            _state0[i] = _state0[_j];
            _state0[_j] = temp;
            keyIndex = (keyIndex + 1) % keyData.Length;
        }
        Array.Copy(_state0, _state, 256);
        _i = 0; _j = 0;
    }

    public void Reset() {
        Array.Copy(_state0, _state, 256);
        _i = 0; _j = 0;
    }

    public byte Rand() {
        _i++;
        _j = (byte)(_j + _state[_i]);
        byte temp = _state[_i];
        _state[_i] = _state[_j];
        _state[_j] = temp;
        return _state[(_state[_i] + _state[_j]) & 0xFF];
    }
}

public class Xor256Test {
    private ArcfourPrng _prng = new ArcfourPrng();
    private byte[] _key;
    private int _rounds;
    private byte _ucPrev0, _ucPrev;

    public void Initialize(string keyData, int rounds) {
        _rounds = rounds;
        _key = new byte[256];
        byte[] keyBytes = Encoding.UTF8.GetBytes(keyData);
        for (int i = 0, j = 0; i < 256; i++, j = (j + 1) % keyBytes.Length)
            _key[i] = keyBytes[j];
        _prng.SetKey(_key);
        _ucPrev0 = _prng.Rand();
        _ucPrev = _ucPrev0;
    }

    public void ResetChain() {
        _prng.Reset();
        _prng.Rand();
        _ucPrev = _ucPrev0;
    }

    public byte[] Encrypt(byte[] input) {
        byte[] output = new byte[input.Length];
        for (int i = 0; i < input.Length; i++) {
            _ucPrev ^= input[i];
            byte cipher = (byte)((_ucPrev ^ _prng.Rand()) + _prng.Rand());
            for (int j = 1; j < _rounds; j++) {
                cipher ^= _prng.Rand();
                cipher = (byte)(cipher + _prng.Rand());
            }
            output[i] = cipher;
        }
        return output;
    }

    public static string EncryptString(string plain) {
        var t = new Xor256Test();
        t.Initialize("TGA_util", 4);
        t.ResetChain();
        byte[] enc = t.Encrypt(Encoding.UTF8.GetBytes(plain));
        return BitConverter.ToString(enc).Replace("-", "");
    }
}
"@

Write-Host "===== Testing with NEW MAC address =====" -ForegroundColor Cyan
Write-Host ""

# Test with completely new MAC
$testMac = "AA:BB:CC:DD:EE:01"
# Test Launcher Install (target=0)
$query = "client=pb000&action=install&target=0&macadd=$testMac"
Write-Host "Query: $query" -ForegroundColor Yellow

$encrypted = [Xor256Test]::EncryptString($query)
Write-Host "BID: $encrypted" -ForegroundColor Green

$url = "https://bustabcc.net/PRG/lg_read.php?bid=$encrypted"
Write-Host ""
Write-Host "Calling API..." -ForegroundColor Cyan

try {
    $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 15
    Write-Host "Status: $($response.StatusCode)" -ForegroundColor Green
    Write-Host ""
    Write-Host ">> Check if 'Application Install' increased in the dashboard <<" -ForegroundColor Yellow
} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
}
