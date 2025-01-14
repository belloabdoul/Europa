using System.Runtime.InteropServices;
using Api.Client.Repositories;
using Core.Entities.Commons;
using MysticMind.PostgresEmbed;
using Architecture = System.Runtime.InteropServices.Architecture;

namespace Api;

public class DatabaseService : IHostedService, IConnectionStringBuilder
{
    private PgServer _server = null!;

    private static readonly string EuropaFolder = Path.GetRelativePath(Environment.CurrentDirectory,
        string.Join(Path.DirectorySeparatorChar,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Europa"));
    
    private static readonly string ExtensionsLocation = Path.Combine("share", "extension");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(EuropaFolder)!);

        var pgServerSettings = new Dictionary<string, string>
        {
            { "max_connections", "40" },
            { "shared_buffers", "256MB" },
            { "effective_cache_size", "768MB" },
            { "maintenance_work_mem", "128MB" },
            { "checkpoint_completion_target", "0.9" },
            { "wal_buffers", "7864kB" },
            { "default_statistics_target", "500" },
            { "random_page_cost", "1.1" },
            { "work_mem", "1638kB" },
            { "huge_pages", "off" },
            { "min_wal_size", "4GB" },
            { "max_wal_size", "16GB" },
            { "max_worker_processes", Environment.ProcessorCount.ToString() },
            { "max_parallel_workers_per_gather", (Environment.ProcessorCount / 2).ToString() },
            { "max_parallel_workers", Environment.ProcessorCount.ToString() },
            { "max_parallel_maintenance_workers", (Environment.ProcessorCount / 2).ToString() }
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            pgServerSettings.Add("effective_io_concurrency", "200");

        _server = new PgServer("16.6.0", Environment.UserName, EuropaFolder, Guid.Empty, pgServerParams: pgServerSettings);
        await _server.StartAsync(cancellationToken);
        
        var extensionDir =
            Path.Combine(
#pragma warning disable SYSLIB0044
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase)![6..],
#pragma warning restore SYSLIB0044
                "Libs", "pgvector");
        
        // Copy extension file to PostgreSQL
        Utils.CopyDirectory(Path.Combine(extensionDir, ExtensionsLocation),
            _server.DataDir.Replace("data", ExtensionsLocation), true);

        Utils.CopyDirectory(Path.Combine(extensionDir, "include"),
            _server.DataDir.Replace("data", "include"), true);

        // Copy dynamic library to PostgreSQL depending on OS
        // Windows
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.Copy(Path.Combine(extensionDir, "lib", "vector.dll"),
                    _server.DataDir.Replace("data", Path.Join("lib", "vector.dll")));
            }

            // Linux
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                File.Copy(Path.Combine(extensionDir, "lib", "vector.so"),
                    _server.DataDir.Replace("data", Path.Join("lib", "vector.so")));

            // OSX - Either X64 OR ARM64
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var architecture = RuntimeInformation.ProcessArchitecture;
                switch (architecture)
                {
                    case Architecture.Arm64:
                        File.Copy(Path.Combine(extensionDir, "lib", "vector-arm64.dylib"),
                            _server.DataDir.Replace("data", Path.Join("lib", "vector.dylib")));
                        break;
                    case Architecture.X64:
                        File.Copy(Path.Combine(extensionDir, "lib", "vector-x64.dylib"),
                            _server.DataDir.Replace("data", Path.Join("lib", "vector.dylib")));
                        break;
                    case Architecture.X86:
                    case Architecture.Arm:
                    case Architecture.Wasm:
                    case Architecture.S390x:
                    case Architecture.LoongArch64:
                    case Architecture.Armv6:
                    case Architecture.Ppc64le:
                    case Architecture.RiscV64:
                    default:
                        throw new UnsupportedPlatformException();
                }
            }
        }
        catch (IOException)
        {
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _server.StopAsync(cancellationToken);
        await _server.DisposeAsync();
    }

    public string ConnectionString =>
        $"Server=localhost;Port={_server.PgPort};User Id={_server.PgUser};Database={_server.PgDbName};Pooling=true;Maximum Pool Size=40;Tcp Keepalive=true;Timeout=1024;Command Timeout=0;Include Error Detail=true";
}