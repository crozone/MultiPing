using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace MultiPinger {

    /// <summary>
    /// MultiPinger by RC 2017
    /// </summary>
    class Program {
        public static void Main(string[] args) {
            Task.Run(() => MainAsync()).GetAwaiter().GetResult();
        }

        public static async Task MainAsync() {
            Console.WriteLine("Enter IP Addresses to ping now.");
            Console.WriteLine("Enter a blank line to finish.");
            Console.WriteLine();

            List<IPAddress> ipAddresses = new List<IPAddress>();

            string input;
            while ((input = Console.ReadLine()) != string.Empty) {
                IPAddress inputIPAddress;
                if (!IPAddress.TryParse(input, out inputIPAddress)) {
                    Console.WriteLine("Invalid IP Address, try again");
                    continue;
                }

                ipAddresses.Add(inputIPAddress);
            }

            // get output CSV file name
            Console.WriteLine();
            Console.WriteLine("Enter output CSV file name");
            StreamWriter outputStreamWriter = null;
            while (true) {
                input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) {
                    Console.WriteLine("Invalid filename, try again");
                    continue;
                }

                try {
                    outputStreamWriter = new StreamWriter(File.OpenWrite(input));
                    break;
                }
                catch (Exception ex) {
                    Console.WriteLine($"Could not open {input} for writing. {ex.Message}. Try again.");
                    continue;
                }
            }

            // write header to output

            outputStreamWriter.WriteLine(string.Join(",", (new string[] {
                "ISO DateTime", "UNIX Timestamp"
            }).Concat(ipAddresses.Select(ip => ip.ToString()))));

            // enter ping loop
            while (true) {
                // create array to hold ping time results 
                int[] pingTimes = new int[ipAddresses.Count];

                DateTimeOffset currentTime = DateTimeOffset.Now;

                List<Task<PingReply>> pingTasks = new List<Task<PingReply>>();

                Console.WriteLine($"Starting ping group at {currentTime.ToString("o")}");

                foreach (IPAddress thisIpAddress in ipAddresses) {
                    // create pinger
                    Ping pinger = new Ping();

                    Console.WriteLine($"Pinging {thisIpAddress.ToString()}");
                    Task<PingReply> pingTask = pinger.SendPingAsync(thisIpAddress, 3000);
                    pingTasks.Add(pingTask);
                }

                Console.WriteLine("Waiting for ping results ...");
                PingReply[] pingReplies = await Task.WhenAll(pingTasks);
                Console.WriteLine("All pings recieved.");

                // we now have all the ping replies for this data point

                // write the output to file
                var orderedReplyTimes = pingReplies
                    .OrderBy(pr => ipAddresses.IndexOf(pr.Address))
                    .Select(pr => pr.Status == IPStatus.Success ? pr.RoundtripTime.ToString() : "");

                var line = string.Join(",", (new string[] {
                    currentTime.ToString("o"),
                    currentTime.ToUnixTimeMilliseconds().ToString()
                })
                    .Concat(orderedReplyTimes));

                outputStreamWriter.WriteLine(line);
                outputStreamWriter.Flush();

                await Task.Delay(new TimeSpan(0, 0, 0, 0, 200));
            }
        }
    }
}
