using Foundation.Keys;

Console.Title = "MS.Promocoes";
string solutionRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var keyManagement = new KeyManagement(solutionRootPath, "MS.Promocoes");

keyManagement.CheckKeys();