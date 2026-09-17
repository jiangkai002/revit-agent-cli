using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RevitAgentMcp;

var builder = Host.CreateApplicationBuilder(args);

// MCP stdio owns stdout for JSON-RPC frames — any log line printed there would corrupt the
// protocol. Route ALL console logging to stderr (same discipline the executor bridge already
// follows: results travel through files, never stdout).
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
