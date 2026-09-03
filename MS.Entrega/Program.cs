using Foundation.Keys;

Console.Title = "MS.Entrega";

string solutionRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var keyManagement = new KeyManagement(solutionRootPath, "MS.Entrega");

keyManagement.CheckKeys();