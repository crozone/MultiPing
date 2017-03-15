using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public class Program {
        public static void Main(string[] args) {
            Task.Run(() => MainAsync()).GetAwaiter().GetResult();
        }

        public static async Task MainAsync() {

            List<IPAddress> ipAddresses = new List<IPAddress>();

            while (true) {
                Console.WriteLine("Enter IP Addresses to ping now.");
                Console.WriteLine("Enter a blank line to finish.");
                Console.WriteLine();

                string ipLineInput;
                while ((ipLineInput = Console.ReadLine()) != string.Empty) {
                    IPAddress inputIPAddress;
                    if (!IPAddress.TryParse(ipLineInput, out inputIPAddress)) {
                        Console.WriteLine("Invalid IP Address, try again");
                        continue;
                    }

                    if (ipAddresses.Contains(inputIPAddress)) {
                        Console.WriteLine("IP Address already entered, try again");
                        continue;
                    }

                    ipAddresses.Add(inputIPAddress);
                }

                if (ipAddresses.Count > 0) {
                    Console.WriteLine($"Using {ipAddresses.Count} IP Addresses.");
                    break;
                }

                Console.WriteLine("No IP Addresses entered.");
                Console.WriteLine();
                continue;
            }

            // the minumum delay between two pings to the same host.
            TimeSpan pingDelay; // 100ms

            // get ping rate limit from user
            Console.WriteLine();
            Console.WriteLine("Enter max requests per host, per second [default 5] (0 for infinite)");
            while (true) {
                string requestsPerSecondInput = Console.ReadLine();
                int requestsPerSecond = 5;
                if (string.IsNullOrWhiteSpace(requestsPerSecondInput)) {
                    Console.WriteLine("No input, using defaults");
                }
                else {
                    if (!int.TryParse(requestsPerSecondInput, out requestsPerSecond)) {
                        Console.WriteLine("Not an integer, try again.");
                        continue;
                    }
                }

                if (requestsPerSecond > 0) {
                    pingDelay = new TimeSpan(0, 0, 0, 0, 1000 / requestsPerSecond);
                }
                else {
                    pingDelay = TimeSpan.Zero;
                }

                Console.WriteLine($"Inter-ping delay set to {pingDelay.TotalMilliseconds}ms per host");
                break;
            }

            // get output CSV file name
            Console.WriteLine();
            Console.WriteLine("Enter output CSV file name");
            StreamWriter outputStreamWriter;
            while (true) {
                string filenameInput = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(filenameInput)) {
                    Console.WriteLine("No filename provided, will not save output.");
                    outputStreamWriter = null;
                    break;
                }

                try {
                    outputStreamWriter = new StreamWriter(File.OpenWrite(filenameInput));
                    Console.WriteLine($"{filenameInput} opened for writing.");
                    break;
                }
                catch (Exception ex) {
                    Console.WriteLine($"Could not open {filenameInput} for writing. {ex.Message}. Try again.");
                    continue;
                }
            }

            Console.WriteLine();
            Console.WriteLine("Starting ping loop.");

            // stopwatch used for regular status updates and write buffer flushing
            Stopwatch sw = new Stopwatch();
            sw.Start();

            // write output header
            if (outputStreamWriter != null) {
                await outputStreamWriter.WriteLineAsync(string.Join(",", (new string[] {
                "ISO DateTime", "Date", "Time", "UNIX Timestamp"
                }).Concat(ipAddresses.Select(ip => ip.ToString()))));
            }

            // keep track of ping counts
            Dictionary<IPAddress, PingCounter> pingCountsDict
                = ipAddresses.ToDictionary(ip => ip, ip => new PingCounter() { Attempted = 0, Success = 0 });

            // correlate an IPAddress to the task responsible for pinging that IPAddress
            Dictionary<IPAddress, Task<PingResult>> pingTaskDict
                    = new Dictionary<IPAddress, Task<PingResult>>();

            // enter ping loop
            while (true) {
                // go through each IP Address and spawn a ping task for it if none exists
                foreach (IPAddress thisIpAddress in ipAddresses) {
                    if (!pingTaskDict.ContainsKey(thisIpAddress)) {
                        // create the ping task, but do not await it here.
                        Task<PingResult> pingTask = Task.Run(async () => {
                            // rate limiting task
                            PingResult result = new PingResult();

                            // create pinger
                            using (Ping pinger = new Ping()) {
                                result.Target = thisIpAddress;
                                result.StartTime = DateTimeOffset.Now;

                                // start rate limiting timer before ping starts
                                Task delayTask = null;
                                if (pingDelay > TimeSpan.Zero) {
                                    delayTask = Task.Delay(pingDelay);
                                }

                                // send and await the ping
                                result.PingReply = await pinger.SendPingAsync(thisIpAddress, 3000);

                                // optionally await the holdoff time
                                if (delayTask != null) {
                                    await delayTask;
                                }
                            }

                            return result;
                        });

                        // add the task to the dict
                        pingTaskDict.Add(thisIpAddress, pingTask);
                    }
                }

                // wait until any ping task completes and get the result
                PingResult pingResult = await await Task.WhenAny(pingTaskDict.Values);

                // remove the completed task so that a fresh one is
                // recreated on the next loop
                pingTaskDict.Remove(pingResult.Target);

                // increment attempted ping counter
                // increment ping count for this ip
                pingCountsDict[pingResult.Target].Attempted++;

                if (pingResult.PingReply.Status == IPStatus.Success) {
                    // increment ping count for this ip
                    pingCountsDict[pingResult.Target].Success++;

                    if (outputStreamWriter != null) {
                        // save the result from the ping that completed in the correct column
                        string[] responseValueColumns = new string[ipAddresses.Count];
                        int ipAddressIndex = ipAddresses.IndexOf(pingResult.Target);

                        responseValueColumns[ipAddressIndex] = pingResult.PingReply.RoundtripTime.ToString();

                        // create the output line
                        var line = string.Join(
                            ",",
                            (new string[] {
                                pingResult.StartTime.ToString("o"),
                                pingResult.StartTime.ToString("yyyy-mm-dd"),
                                pingResult.StartTime.ToString("hh:MM:ss.fff"),
                                pingResult.StartTime.ToUnixTimeMilliseconds().ToString()
                            })
                            .Concat(responseValueColumns));

                        // write the output line
                        await outputStreamWriter.WriteLineAsync(line);
                    }
                }

                // if it has been more than a second since the last maintenance update,
                // do another one.
                if (sw.ElapsedMilliseconds > 1000) {
                    // write status update to console
                    Console.WriteLine();
                    Console.WriteLine($"[{DateTimeOffset.Now.ToString("o")}]");

                    if (outputStreamWriter != null) {
                        // flush the output writer to disk
                        await outputStreamWriter.FlushAsync();

                        FileStream fs = outputStreamWriter?.BaseStream as FileStream;

                        if (fs != null) {
                            Console.WriteLine($"{fs.Name}: {fs.Length / 1024} kb");
                        }
                    }

                    foreach (var thisPair in pingCountsDict) {
                        Console.WriteLine($"{thisPair.Key.ToString()}: {thisPair.Value.Success} / {thisPair.Value.Attempted}");
                    }
                    // reset the stopwatch.
                    sw.Restart();
                }
            }
        }
    }

    public class PingResult {
        public DateTimeOffset StartTime { get; set; }
        public IPAddress Target { get; set; }
        public PingReply PingReply { get; set; }
    }

    public class PingCounter {
        public int Success { get; set; }
        public int Attempted { get; set; }
    }
}
