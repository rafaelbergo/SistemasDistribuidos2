using Foundation.Keys;

Console.Title = "MS.Estoque";

string solutionRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var keyManagement = new KeyManagement(solutionRootPath, "MS.Estoque");

keyManagement.CheckKeys();