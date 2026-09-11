using ContosoFabric.Core.Models;
using ContosoFabric.Fabric.Pipeline;
using System.Diagnostics;
using System.Text.Json;
using Parquet;
var root = @"D:\PROJ\Contoso_Data_Fabric";
var project = new FabricProject("audit-tiny", BusinessScenario.SalesBi, DataScale.Tiny, 1, RawFormat.Parquet, PipelineStage.Generate, new FabricWorkspaceTarget());
var runner = new FabricPipelineRunner();
for (var run = 1; run <= 2; run++) {
 var timer = Stopwatch.StartNew();
 var result = await runner.RunToSelectedStageAsync(project, root, AppContext.BaseDirectory);
 using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result.GeneratedDataFolder,"truth_manifest.json")));
 foreach (var table in new[]{"orders","orderrows"}) {
  using var reader = await ParquetReader.CreateAsync(Path.Combine(result.GeneratedDataFolder,table + ".parquet"));
  long count = 0;
  for (int i=0;i<reader.RowGroupCount;i++) { using var group=reader.OpenRowGroupReader(i); count += group.RowCount; }
  var expected=manifest.RootElement.GetProperty(table=="orders"?"actualOrders":"actualOrderRows").GetInt64();
  if(count != expected) throw new Exception($"{table}: expected {expected}, found {count}");
  Console.WriteLine($"VERIFIED {table}={count}");
 }
 Console.WriteLine($"RUN {run} PASS elapsed={timer.Elapsed} receipt={result.ReceiptPath}");
}
