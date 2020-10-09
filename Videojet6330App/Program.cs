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

        private const string Host = "192.168.1.1";
        private const int Port = 3010;
        private const string Template = "DM-24x24-1str";

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
            await printer.Connect(Host, Port);

            var templates = await printer.GetTemplates();
            Console.WriteLine($"Templates {string.Join(", ", templates)}");

            await printer.Init(Template);
            await printer.ClearBuffer();
            var count = await printer.GetBufferCount();
            Console.WriteLine($"Buffer count {count}");

            await printer.WriteNewCodes(GetListString(150));

            count = await printer.GetBufferCount();
            Console.WriteLine($"Buffer count {count}");

            await printer.SwitchOff();
            printer.Disconnect();
            Log.Information("Done!");
            Log.CloseAndFlush();
        }

        private static IEnumerable<string> GetListString(int count = 100)
        {
            var list = new List<string>();
            for (var i = 0; i < count; i++)
            {
                list.Add(RandomString());
            }
            return list;
        }

        private static IEnumerable<string> GetListCodes(int count = 100)
        {
            var list = new List<string>();
            for (var i = 0; i < count; i++)
            {
                list.Add(RandomCode());
            }
            return list;
        }

        private static readonly Random Rnd = new Random();

        private static string RandomCode()
        {
            var list = new List<string>
            {
                "0111111111111113211111111\x001D8005010101\x001D931111",
                "0122222222222226212222222\x001D8005020202\x001D932222",
                "0133333333333339213333333\x001D8005030303\x001D933333",
                "0144444444444442214444444\x001D8005040404\x001D934444",
                "0155555555555555215555555\x001D8005050505\x001D935555",
                "0166666666666668216666666\x001D8005060606\x001D936666",
                "0177777777777771217777777\x001D8005070707\x001D937777",
                "0188888888888884218888888\x001D8005080808\x001D938888",
                "0199999999999997219999999\x001D8005090909\x001D939999"
            };
            return list[Rnd.Next(0, list.Count - 1)];
        }

        private static string RandomString()
        {
            var list = new List<string>
            {
                "0111111111111113211111111X8005010101X931111",
                "0122222222222226212222222X8005020202X932222",
                "0133333333333339213333333X8005030303X933333",
                "0144444444444442214444444X8005040404X934444",
                "0155555555555555215555555X8005050505X935555",
                "0166666666666668216666666X8005060606X936666",
                "0177777777777771217777777X8005070707X937777",
                "0188888888888884218888888X8005080808X938888",
                "0199999999999997219999999X8005090909X939999"
            };
            return list[Rnd.Next(0, list.Count - 1)];
        }

        // ReSharper disable once UnusedMember.Local
        private static string RandomStringFromChars(int length) =>
            new string(Enumerable.Repeat(Chars, length)
                                 .Select(s => s[Random.Next(s.Length)])
                                 .ToArray());

        // ReSharper disable once UnusedMember.Local
        private static IEnumerable<string> RandomStringFromInt(int length)
        {
            var list = new List<string>();
            for (var i = 0; i < length; i++)
                list.Add((_count++).ToString("D43"));
            return list;
        }
    }
}