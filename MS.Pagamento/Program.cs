using Foundation.Keys;

Console.Title = "MS.Pagamento";

string solutionRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var keyManagement = new KeyManagement(solutionRootPath, "MS.Pagamento");

keyManagement.CheckKeys();