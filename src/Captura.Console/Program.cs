using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Captura.Fakes;
using Captura.Native;
using CommandLine;
using static System.Console;
// ReSharper disable LocalizableElement

namespace Captura
{
    static class Program
    {
        [STAThread]
        static async Task Main(string[] Args)
        {
            User32.SetProcessDPIAware();

            ServiceProvider.LoadModule(new CoreModule());
            ServiceProvider.LoadModule(new FakesModule());
            ServiceProvider.LoadModule(new VerbsModule());

            var verbTypes = ServiceProvider
                .Get<IEnumerable<ICmdlineVerb>>()
                .Select(M => M.GetType())
                .ToArray();

            await Parser.Default.ParseArguments(Args, verbTypes)
                .WithParsedAsync(async (ICmdlineVerb Verb) =>
                {
                    // Always display Banner
                    Banner();

                    await Verb.Run();
                });
        }

        static void Banner()
        {
            var version = ServiceProvider.AppVersion.ToString(3);

            WriteLine($@"Captura v{version}
(c) {DateTime.Now.Year} Mathew Sachin
");
        }
    }
}
