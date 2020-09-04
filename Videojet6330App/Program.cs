using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using Videojet6330App.Printer;

namespace VideoJet6330App
{
    class Program
    {
        private static int _count;
        private static readonly Random Random = new Random();
        private const string Chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        static async Task Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Blue;

            Log.Logger = new LoggerConfiguration()
                        .MinimumLevel.Debug()
                        .WriteTo.Console()
                        .WriteTo.File("C:\\ASPU\\myapp\\myapp.txt", rollingInterval:RollingInterval.Day)
                        .CreateLogger();
            Log.Information("Running!");

            // TCP server address
            var printer = new VideoJetPrinter();
            await printer.Connect("192.168.1.1", 3010);

            var templates = await printer.GetTemplates();
            Console.WriteLine($"Templates {string.Join(", ", templates)}");

            await printer.Init("!_6320test");
            await printer.ClearBuffer();
            var count = await printer.GetBufferCount();
            Console.WriteLine($"Buffer count {count}");

            for (;;)
            {
                var str = Console.ReadKey();
                if (str.Key == ConsoleKey.Escape)
                    break;

                await printer.WriteNewCodes(RandomListString(10));
                count = await printer.GetBufferCount();
                Console.WriteLine($"Buffer count {count}");
            }

            Console.Read();

            printer.Disconnect();
            Log.Information("Done!");
            Log.CloseAndFlush();
        }

        private static string RandomString(int length) =>
            new string(Enumerable.Repeat(Chars, length)
                                 .Select(s => s[Random.Next(s.Length)])
                                 .ToArray());

        private static IEnumerable<string> RandomListString(int length)
        {
            var list = new List<string>();
            for (var i = 0; i < length; i++)
                list.Add((_count++).ToString("D10"));
            return list;
        }
    }
}