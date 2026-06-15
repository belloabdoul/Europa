using System.Runtime.InteropServices;
using Api.Client.Repositories;
using Core.Entities.Commons;
using Hardware.Info;
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

    private const int Megabyte = 1_048_576;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(EuropaFolder)!);
        var hardwareInfo = new HardwareInfo();
        hardwareInfo.RefreshMemoryStatus();
        var ram = hardwareInfo.MemoryStatus.TotalPhysical / Megabyte;
        var sixteenthRam = ram / 16;
        Console.WriteLine($"{sixteenthRam}MB");
        Console.WriteLine($"{sixteenthRam * 4}MB");
        Console.WriteLine($"{Convert.ToInt32(0.0625 * ram)}MB");
        Console.WriteLine($"{Convert.ToInt32(sixteenthRam / 48.0 * 1024)}kB");

        var pgServerSettings = new Dictionary<string, string>
        {
            { "max_connections", "100" },
            // Shared buffer is 1/16 of RAM
            { "shared_buffers", $"{sixteenthRam}MB" },
            // Effective cache size is 1/4 of RAM
            { "effective_cache_size", $"{sixteenthRam * 4}MB" },
            { "maintenance_work_mem", "64MB" },
            { "checkpoint_completion_target", "0.9" },
            { "wal_buffers", $"16MB" },
            { "default_statistics_target", "100" },
            // Random page cost is 1 for SSD and 4 for HDD
            { "random_page_cost", "1.1" },
            // Work memory is 1/768 of RAM
            { "work_mem", "4MB" },
            { "huge_pages", "off" },
            { "min_wal_size", "100MB" },
            { "max_wal_size", "2GB" },
            { "wal_level", "minimal" },
            { "max_wal_senders", "0" },
            { "max_worker_processes", Environment.ProcessorCount.ToString() },
            { "max_parallel_workers_per_gather", "0" },
            { "max_parallel_workers", Environment.ProcessorCount.ToString() },
            { "max_parallel_maintenance_workers", (Environment.ProcessorCount / 2).ToString() }
        };

        _server = new PgServer("17.4.0", Environment.UserName, EuropaFolder, Guid.Empty,
            pgServerParams: pgServerSettings);
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
        $"Server=localhost;Port={_server.PgPort};User Id={_server.PgUser};Database={_server.PgDbName};Pooling=true;Tcp Keepalive=true;Timeout=1024;Command Timeout=0;Enlist=false;Include Error Detail=true";
}