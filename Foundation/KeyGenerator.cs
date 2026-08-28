using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Foundation;

public class KeyGenerator
{
    public static void GenerateKeyPair(string privateKeyPath = "private_key.pem", string publicKeyPath = "public_key.pem")
    {
        using var rsa = RSA.Create(2048);

        string privateKeyPem = rsa.ExportPkcs8PrivateKeyPem();
        string publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();

        File.WriteAllText(privateKeyPath, privateKeyPem);
        File.WriteAllText(publicKeyPath, publicKeyPem);

        Console.WriteLine($"Chaves geradas com sucesso:");
        Console.WriteLine($"- Privada: {Path.GetFullPath(privateKeyPath)}");
        Console.WriteLine($"- Pública: {Path.GetFullPath(publicKeyPath)}");
    }
}