using System.Text.Json;

// Executed only by safety tests. Never changes any display settings.
var marker = Environment.GetEnvironmentVariable("DQC_TEST_MARKER")!;
File.AppendAllText(marker, JsonSerializer.Serialize(args) + "\n");
if (args.Length != 3 || args[0] != "--nvpreset" || args[2] != "--nogui") return 23;
if (args[1] == "fail") return 9;
if (args[1] == "missing") { Console.WriteLine("Preset not found or error while executing."); return 0; }
if (args[1] == "slow") await Task.Delay(900);
Console.WriteLine("Done.");
return 0;
