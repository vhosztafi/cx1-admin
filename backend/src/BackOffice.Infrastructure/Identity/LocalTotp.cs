using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace BackOffice.Infrastructure.Identity;

// RFC 4226 dynamic truncation; RFC 6238: SHA-1, six digits, 30-second step.
// Replay prevention and attempt limits live in the held credential transaction.
public static class LocalTotp
{
    public static string NewSecret()=>Base32(RandomNumberGenerator.GetBytes(20));
    public static string Code(string secret,long step,int digits=6)
    {
        if(step<0||digits is not(6 or 8))throw new ArgumentOutOfRangeException(nameof(step));
        Span<byte> counter=stackalloc byte[8];BinaryPrimitives.WriteInt64BigEndian(counter,step);
        var mac=HMACSHA1.HashData(Decode(secret),counter);var offset=mac[^1]&15;
        var value=BinaryPrimitives.ReadUInt32BigEndian(mac.AsSpan(offset,4))&0x7fffffff;
        return (value%(digits==6?1000000u:100000000u)).ToString(new string('0',digits),CultureInfo.InvariantCulture);
    }
    public static long? Verify(string secret,string code,DateTimeOffset now,long? last)
    {
        if(code is null||code.Length!=6||!code.All(char.IsAsciiDigit))return null;
        var step=now.ToUnixTimeSeconds()/30;long? matched=null;
        for(var delta=-1;delta<=1;delta++)if(step+delta>=0&&CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret,step+delta)),Encoding.ASCII.GetBytes(code))&&step+delta>(last??-1))matched=step+delta;
        return matched;
    }
    public static string Base32(byte[] bytes)
    {
        const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";var result=new StringBuilder();int value=0,bits=0;
        foreach(var b in bytes){value=(value<<8)|b;bits+=8;while(bits>=5){bits-=5;result.Append(alphabet[(value>>bits)&31]);}}
        if(bits>0)result.Append(alphabet[(value<<(5-bits))&31]);return result.ToString();
    }
    private static byte[] Decode(string text)
    {
        const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";var result=new List<byte>();int value=0,bits=0;
        foreach(var c in text){var n=alphabet.IndexOf(c);if(n<0)throw new FormatException("Invalid authenticator secret.");value=(value<<5)|n;bits+=5;if(bits>=8){bits-=8;result.Add((byte)(value>>bits));}}
        return result.ToArray();
    }
}
