using System.Text.Json;
using System.Windows.Forms;
static class PolicySelfTest {
 public static async Task Run(string root){
  if(Environment.GetEnvironmentVariable("PSExecutionPolicyPreference")!="Restricted")throw new Exception("Test must inherit Restricted policy.");
  using var studio=new Studio(root);
  using var serial=new SerialMonitor(root);
  using var diag=new DiagnosticsDialog(root,Array.Empty<string>());
  using var isp=new IspDialog(root,Array.Empty<string>());
  SynchronizationContext.SetSynchronizationContext(null);
  await studio.AutomationTest();
  var project=await ProjectWorkspace.Call(root,"list");ProjectWorkspace.Require(project);
  var prod=await ProductionBackend.Call(root,"list");ProductionBackend.Require(prod);
  var profiles=await diag.PolicyProbe();if(!profiles.GetProperty("success").GetBoolean())throw new Exception("Diagnostics failed");
  try{await serial.PolicyProbe();throw new Exception("Unexpected serial session");}catch(Exception e) when(e.Message=="Unknown session"){}
  var detected=await isp.PolicyProbe();
  if(detected.GetProperty("success").GetBoolean()||!detected.GetProperty("error").GetString()!.Contains("Explicit connected Arduino ISP COM port required"))throw new Exception("ISP probe did not reach validation");
  File.WriteAllText(Path.Combine(root,"policy-test.json"),JsonSerializer.Serialize(new{success=true,inherited_policy="Restricted",checks=new[]{"ports","compile","projects","production","diagnostics","serial validation","ISP validation"}}));
 }
}