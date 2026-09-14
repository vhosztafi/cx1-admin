using System.Security.Cryptography;

namespace BackOffice.Application.Agencies;

public sealed class InvitationToken
{
    private InvitationToken(byte[] bytes){Value=Encode(bytes);Hash=SHA256.HashData(bytes);CryptographicOperations.ZeroMemory(bytes);}
    public string Value {get;}
    public byte[] Hash {get;}
    public override string ToString()=>"Protected invitation token";
    public static InvitationToken Create()=>new(RandomNumberGenerator.GetBytes(32));
    public static bool TryHash(string? value,out byte[] hash)
    {
        hash=[];
        if(value is null||value.Length!=43||value.Any(x=>!char.IsAsciiLetterOrDigit(x)&&x is not ('-' or '_')))return false;
        byte[] bytes;
        try{bytes=Convert.FromBase64String(value.Replace('-','+').Replace('_','/')+"=");}
        catch(FormatException){return false;}
        try{if(bytes.Length!=32||Encode(bytes)!=value)return false;hash=SHA256.HashData(bytes);return true;}
        finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    private static string Encode(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
}
