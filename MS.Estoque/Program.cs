using Foundation.Keys;

string solutionRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var keyManagement = new KeyManagement(solutionRootPath, "MS.Estoque");

keyManagement.CheckKeys();