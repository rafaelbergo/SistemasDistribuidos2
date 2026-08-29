using Foundation;
using Foundation.Keys;

string solutionRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var keyManagement = new KeyManagement(solutionRootPath, "MS.Principal");
var signature = new SignatureService();

keyManagement.CheckKeys();

// Test sign and verify text
string text = "Test message";
string privateKeyPath = Path.Combine(solutionRootPath, "MS.Principal", "Keys", "MS.Principal.private.pem");
string publicKeyPath = Path.Combine(solutionRootPath, "MS.Principal", "Keys", "MS.Principal.public.pem");

string validateResult = signature.SignText(text, privateKeyPath);
Console.WriteLine(validateResult);

bool isVerified = signature.VerifySignature(text, validateResult, publicKeyPath);
Console.WriteLine($"Valid: {isVerified}");
