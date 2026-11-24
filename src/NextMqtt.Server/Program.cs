var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Text("NextMQTT"));

app.Run();

/// <summary>Entry point marker used by integration tests.</summary>
public partial class Program;
