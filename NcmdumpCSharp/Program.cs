using System.CommandLine;
using System.Reflection;
using NcmdumpCSharp.Core;

namespace NcmdumpCSharp;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("網易雲音樂NCM檔案解密工具 - C#版本");

        // 版本選項
        var versionOption = new Option<bool>("--version", "-v")
        {
            Description = "顯示版本資訊並退出",
        };

        rootCommand.Options.Add(versionOption);

        // 目錄選項
        var directoryOption = new Option<string?>("--directory", "-d")
        {
            Description = "處理指定目錄下的所有NCM檔案",
        };

        rootCommand.Options.Add(directoryOption);

        // 遞迴選項
        var recursiveOption = new Option<bool>("--recursive", "-r")
        {
            Description = "遞迴處理子目錄",
        };

        rootCommand.Options.Add(recursiveOption);

        // 輸出目錄選項
        var outputOption = new Option<string?>("--output", "-o")
        {
            Description = "指定輸出目錄",
        };

        rootCommand.Options.Add(outputOption);

        // 檔案參數
        var filesArgument = new Argument<string[]>("files")
        {
            Description = "要處理的NCM檔案",
            Arity = ArgumentArity.ZeroOrMore,
        };

        rootCommand.Arguments.Add(filesArgument);

        rootCommand.SetAction(parseResult =>
        {
            if (parseResult.GetValue(versionOption))
            {
                var asm = Assembly.GetExecutingAssembly();

                string ver = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
                 ?? asm.GetName().Version?.ToString()
                 ?? "unknown";

                Console.WriteLine(ver);

                return 0;
            }

            string? directory = parseResult.GetValue(directoryOption);
            bool recursive = parseResult.GetValue(recursiveOption);
            string? output = parseResult.GetValue(outputOption);
            string[] files = parseResult.GetValue(filesArgument) ?? [];

            ProcessFiles(directory, recursive, output, files);

            return 0;
        });

        return await rootCommand.Parse(args).InvokeAsync();
    }

    private static void ProcessFiles(string? directory, bool recursive, string? output, string[] files)
    {
        // === 1. 參數驗證 ===
        if (string.IsNullOrEmpty(directory) && files.Length == 0)
        {
            Console.WriteLine("錯誤：請指定要處理的檔案或目錄");
            Console.WriteLine("使用 --help 查看說明資訊");

            return;
        }

        if (recursive && string.IsNullOrEmpty(directory))
        {
            Console.WriteLine("錯誤：-r 選項需要配合 -d 選項使用");

            return;
        }

        // === 2. 輸出目錄準備 ===
        string? outputDir = null;

        if (!string.IsNullOrWhiteSpace(output))
        {
            if (File.Exists(output))
            {
                Console.WriteLine($"錯誤：'{output}' 不是有效的目錄");

                return;
            }

            Directory.CreateDirectory(output);
            outputDir = output;
        }

        // === 3. 收集所有待處理檔案 ===
        var filesToProcess = CollectFiles(directory, recursive, files).ToList();

        if (filesToProcess.Count == 0)
        {
            Console.WriteLine("未找到任何 .ncm 檔案");

            return;
        }

        // === 4. 逐個處理檔案 ===
        foreach ((string filePath, string? relativeToBase) in filesToProcess)
        {
            string? targetOutputDir = null;

            if (outputDir != null && relativeToBase != null)
            {
                targetOutputDir = Path.Combine(outputDir, Path.GetDirectoryName(relativeToBase) ?? "");
                Directory.CreateDirectory(targetOutputDir);
            }

            ProcessSingleFile(filePath, targetOutputDir);
        }
    }

    private static IEnumerable<(string FilePath, string? RelativePath)> CollectFiles(
        string? directory,
        bool recursive,
        string[] files
        )
    {
        var list = new List<(string, string?)>();

        // 處理命令列傳入的檔案
        foreach (string file in files)
        {
            if (!File.Exists(file))
            {
                Console.WriteLine($"警告：檔案 '{file}' 不存在，跳過");

                continue;
            }

            if (file.EndsWith(".ncm", StringComparison.OrdinalIgnoreCase))
            {
                list.Add((file, null)); // 无相对路径
            }
        }

        // 處理目錄中的檔案
        if (string.IsNullOrEmpty(directory))
            return list;

        if (!Directory.Exists(directory))
        {
            Console.WriteLine($"錯誤：目錄 '{directory}' 不存在");

            return list;
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        string[] ncmFiles = Directory.GetFiles(directory, "*.ncm", searchOption);

        list.AddRange(from file in ncmFiles let relativePath = Path.GetRelativePath(directory, file) select (file, relativePath));

        return list;
    }

    private static void ProcessSingleFile(string filePath, string? outputDir)
    {
        try
        {
            using var crypt = new NeteaseCrypt(filePath);

            crypt.Dump(outputDir);

            crypt.FixMetadata();

            Console.WriteLine($"[完成] '{filePath}' -> '{crypt.DumpFilePath}'");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[錯誤] 處理檔案 '{filePath}' 時發生例外：{ex.Message}");
        }
    }
}
