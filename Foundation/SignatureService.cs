using System.Security.Cryptography;
using System.Text;

namespace Foundation;

public class SignatureService
{
    public string SignText(string text, string privateKeyPath)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(privateKeyPath));
        
        byte[] data = Encoding.UTF8.GetBytes(text);
        byte[] signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
 
        return Convert.ToBase64String(signature);
    }

    public bool VerifySignature(string text, string signature, string publicKeyPath)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(publicKeyPath));
        
        byte[] data = Encoding.UTF8.GetBytes(text);
        byte[] signatureBytes = Convert.FromBase64String(signature);
        
        return rsa.VerifyData(data, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}