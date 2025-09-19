using DialerAgent;

var builder = Host.CreateApplicationBuilder(args);
// Allow running as a Windows Service (optional)
builder.Services.AddWindowsService(options => options.ServiceName = "DialerOne Agent");
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
