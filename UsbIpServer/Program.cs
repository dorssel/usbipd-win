/*
    usbipd-win: a server for hosting USB devices across networks
    Copyright (C) 2020  Frans van Dorsselaer

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Linq;

namespace UsbIpServer
{
    static class Program
    {
        static int Main(string[] args)
        {
            var app = new CommandLineApplication()
            {
                Name = "usbip",
            };
            app.VersionOption("-v|--version", "0.1");

            void DefaultCmdLine(CommandLineApplication cmd)
            {
                // all commands (as well as the top-level executable) have these
                cmd.FullName = "UsbIp";
                cmd.ShortVersionGetter = app.ShortVersionGetter;
                cmd.HelpOption("-h|--help").ShowInHelpText = false;
            }

            DefaultCmdLine(app);
            app.OptionHelp.ShowInHelpText = true;
            app.Command("bind", (cmd) => {
                cmd.Description = "Bind device to VBoxUsb.sys";
                DefaultCmdLine(cmd);
                cmd.Option("-b|--busid=<busid>", "Bind VBoxUsb.sys to device on <busid>", CommandOptionType.SingleValue);
            });
            app.Command("unbind", (cmd) => {
                cmd.Description = "Unbind device from VBoxUsb.sys";
                DefaultCmdLine(cmd);
                cmd.Option("-b|--busid=<busid>", "Unbind VBoxUsb.sys from device on <busid>", CommandOptionType.SingleValue);
            });
            app.Command("server", (cmd) => {
                cmd.Description = "Run the server stand-alone on the console";
                DefaultCmdLine(cmd);
                cmd.Argument("key=value", ".NET configuration override", true);
                cmd.OnExecute(() => ExecuteServer(cmd.Arguments.Single().Values.ToArray()));
            });

            app.OnExecute(() => {
                app.ShowHelp();
                return 0;
            });

            return app.Execute(args);
        }

        static int ExecuteServer(string[] args)
        {
            Host.CreateDefaultBuilder(args)
                .UseWindowsService()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Server>();
                    services.AddScoped<ClientContext>();
                    services.AddScoped<ConnectedClient>();
                    services.AddScoped<AttachedClient>();
                })
                .Build()
                .Run();
            return 0;
        }
    }
}
