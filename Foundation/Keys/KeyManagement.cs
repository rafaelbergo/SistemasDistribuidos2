using System.Security.Cryptography;

namespace Foundation.Keys;

public class KeyManagement
{
    private readonly string _solutionRootPath;
    private readonly string _currentProjectName;

    private static readonly string[] Microservices =
    [
        "MS.Principal",
        "MS.Entrega",
        "MS.Estoque",
        "MS.Pagamento",
        "MS.Promocoes",
    ];

    public KeyManagement(string solutionRootPath, string currentProjectName)
    {
        _solutionRootPath = Path.GetFullPath(solutionRootPath);
        _currentProjectName = currentProjectName;
    }

    public string GetProjectPath(string projectName) => Path.Combine(_solutionRootPath, projectName);
    

    public void CheckKeys()
    {
        string currentProjectPath = GetProjectPath(_currentProjectName);
        string keysDirectory = Path.Combine(currentProjectPath, "Keys");

        Directory.CreateDirectory(keysDirectory);

        string privateKeyName = $"{_currentProjectName}.private.pem";
        string publicKeyName = $"{_currentProjectName}.public.pem";

        string privateKeyPath = Path.Combine(keysDirectory, privateKeyName);
        string publicKeyPath = Path.Combine(keysDirectory, publicKeyName);

        EnsureKeyPair(privateKeyPath, publicKeyPath);
        DistributePublicKey(publicKeyPath, publicKeyName);
    }

    private static void EnsureKeyPair(string privateKeyPath, string publicKeyPath)
    {
        bool privateExists = File.Exists(privateKeyPath);
        bool publicExists = File.Exists(publicKeyPath);

        // None exists, generate new pair
        if (!privateExists && !publicExists)
        {
            Console.WriteLine("Keys not found, generating new");
            GenerateKeyPair(privateKeyPath, publicKeyPath);

            return;
        }

        // If only one exists, remove and generate new pair
        if (privateExists && !publicExists || !privateExists && publicExists)
        {
            Console.WriteLine("Only one key found, generating new");
            GenerateKeyPair(privateKeyPath, publicKeyPath);
            return;
        }

        Console.WriteLine($"Keys valid");
    }

    private static void GenerateKeyPair(string privateKeyPath, string publicKeyPath)
    {
        using RSA rsa = RSA.Create();

        rsa.KeySize = 2048;

        string privateKey = rsa.ExportPkcs8PrivateKeyPem();
        string publicKey = rsa.ExportSubjectPublicKeyInfoPem();

        File.WriteAllText(privateKeyPath, privateKey);
        File.WriteAllText(publicKeyPath, publicKey);
    }

    private void DistributePublicKey(string publicKeyPath, string publicKeyName)
    {
        foreach (string microservice in Microservices)
        {
            if (microservice.Equals(_currentProjectName, StringComparison.OrdinalIgnoreCase))
                continue;

            string projectPath = GetProjectPath(microservice);
            string targetKeysDirectory = Path.Combine(projectPath, "Keys");
            Directory.CreateDirectory(targetKeysDirectory);
            string destinationPath = Path.Combine(targetKeysDirectory, publicKeyName);

            CopyPublicKey(publicKeyPath, destinationPath, microservice);
        }
    }

    private static void CopyPublicKey(string sourcePath, string destinationPath, string destinationMicroservice)
    {
        if (!File.Exists(destinationPath))
        {
            File.Copy(sourcePath, destinationPath);
            Console.WriteLine($"Public key moved to {destinationMicroservice}.");

            return;
        }
    }   
}