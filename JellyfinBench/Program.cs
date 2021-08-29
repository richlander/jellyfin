using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;

namespace JellyfinBench
{
    class Program
    {
        const string WindowsWritingImageString = "writing image sha256:";
        const int Iterations = 10;
        const bool UseReadyToRun = true;
        const bool UseTieredCompilation = true;

        struct BuildMode
        {
            public string Name;

            public bool NetCoreComposite;
            public bool NetCoreIncludeAspNet;
            public bool AspNetComposite;
            public bool AppR2R;
            public bool AppComposite;
            public bool OneBigComposite;
            public bool AppAVX2;
        }

        private static BuildMode[] s_buildModes =
        {
            new BuildMode()
            {
                Name = "baseline",
                NetCoreComposite = false,
                NetCoreIncludeAspNet = false,
                AspNetComposite = false,
                AppR2R = false,
                AppComposite = false,
                OneBigComposite = false,
            },
            new BuildMode()
            {
                Name = "r2r",
                NetCoreComposite = false,
                NetCoreIncludeAspNet = false,
                AspNetComposite = false,
                AppR2R = true,
                AppComposite = false,
                OneBigComposite = false,
            },
            new BuildMode()
            {
                Name = "app-composite-avx2",
                NetCoreComposite = false,
                NetCoreIncludeAspNet = false,
                AspNetComposite = false,
                AppR2R = true,
                AppComposite = true,
                OneBigComposite = false,
                AppAVX2 = true,
            },
            new BuildMode()
            {
                Name = "one-big-composite-avx2",
                NetCoreComposite = false,
                NetCoreIncludeAspNet = false,
                AspNetComposite = false,
                AppR2R = true,
                AppComposite = true,
                OneBigComposite = true,
                AppAVX2 = true,
            },
            new BuildMode()
            {
                Name = "r2r-platform-composite-avx2",
                NetCoreComposite = true,
                NetCoreIncludeAspNet = true,
                AspNetComposite = true,
                AppR2R = true,
                AppComposite = true,
                OneBigComposite = false,
                AppAVX2 = true,
            },
            new BuildMode()
            {
                Name = "jit-platform-composite-avx2",
                NetCoreComposite = true,
                NetCoreIncludeAspNet = true,
                AspNetComposite = true,
                AppR2R = false,
                AppComposite = false,
                OneBigComposite = false,
                AppAVX2 = true,
            },
        };

        static string s_folderName;

        static string s_timestamp;

        static TextWriter s_buildLogFile;
        static TextWriter s_execLogFile;

        static int Main(string[] args)
        {
            s_timestamp = DateTime.Now.ToString("MMdd-HHmm");
            s_folderName = Directory.GetCurrentDirectory();

            string xmlFile;
            if (args.Length > 0)
            {
                xmlFile = args[0];
            }
            else
            {
                StringBuilder xml = new StringBuilder();
                xml.AppendLine("<Xml>");
                string buildLogFile = Path.Combine(s_folderName, $"jellyfin-build-{s_timestamp}.log");
                string execLogFile = Path.Combine(s_folderName, $"jellyfin-run-{s_timestamp}.log");
                using (StreamWriter buildLogWriter = new StreamWriter(buildLogFile))
                using (StreamWriter execLogWriter = new StreamWriter(execLogFile))
                {
                    s_buildLogFile = buildLogWriter;
                    s_execLogFile = execLogWriter;
                    for (int modeIndex = 0; modeIndex < s_buildModes.Length; modeIndex++)
                    {
                        BuildAndRun(s_buildModes[modeIndex], xml, modeIndex, s_buildModes.Length);
                    }
                    s_buildLogFile = null;
                    s_execLogFile = null;
                }
                xml.AppendLine("</Xml>");
                Console.WriteLine(new string('=', 70));
                Console.WriteLine(xml.ToString());
                xmlFile = Path.Combine(s_folderName, $"results-{s_timestamp}.xml");
                File.WriteAllText(xmlFile, xml.ToString());
            }

            string resultsFile = Path.ChangeExtension(xmlFile, "results.txt");
            ProcessXmlFile(xmlFile, resultsFile);
            return 0;
        }

        private static void BuildAndRun(in BuildMode buildMode, StringBuilder xml, int index, int count)
        {
            string image = Build(buildMode, index, count);
            if (image == null)
            {
                return;
            }
            xml.AppendFormat("<BuildAndRun Name=\"{0}\">\n", buildMode.Name);
            xml.AppendFormat("<NetCoreComposite>{0}</NetCoreComposite>\n", buildMode.NetCoreComposite);
            xml.AppendFormat("<NetCoreIncludeAspNet>{0}</NetCoreIncludeAspNet>\n", buildMode.NetCoreIncludeAspNet);
            xml.AppendFormat("<AspNetComposite>{0}</AspNetComposite>\n", buildMode.AspNetComposite);
            xml.AppendFormat("<AppR2R>{0}</AppR2R>\n", buildMode.AppR2R);
            xml.AppendFormat("<AppComposite>{0}</AppComposite>\n", buildMode.AppComposite);
            xml.AppendFormat("<OneBigComposite>{0}</OneBigComposite>\n", buildMode.OneBigComposite);
            xml.AppendFormat("<AppAVX2>{0}</AppAVX2>\n", buildMode.AppAVX2);
            xml.AppendFormat("<UseTieredCompilation>{0}</UseTieredCompilation>\n", UseTieredCompilation);
            xml.AppendFormat("<UseReadyToRun>{0}</UseReadyToRun>\n", UseReadyToRun);
            xml.AppendLine("<Results>");
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                Run(image, xml);
            }
            xml.AppendLine("</Results>");
            xml.AppendLine("</BuildAndRun>");
        }

        private static string Build(in BuildMode buildMode, int index, int total)
        {
            Stopwatch sw = Stopwatch.StartNew();
            Console.WriteLine("Building configuration: {0} ({1} / {2})", buildMode.Name, index, total);

            StringBuilder commandLine = new StringBuilder();
            commandLine.AppendFormat("build {0}", s_folderName);
            commandLine.AppendFormat(" --build-arg NETCORE_COMPOSITE={0}", buildMode.NetCoreComposite);
            commandLine.AppendFormat(" --build-arg NETCORE_INCLUDE_ASPNET={0}", buildMode.NetCoreIncludeAspNet);
            commandLine.AppendFormat(" --build-arg ASPNET_COMPOSITE={0}", buildMode.AspNetComposite);
            commandLine.AppendFormat(" --build-arg APP_R2R={0}", buildMode.AppR2R);
            commandLine.AppendFormat(" --build-arg APP_COMPOSITE={0}", buildMode.AppComposite);
            commandLine.AppendFormat(" --build-arg ONE_BIG_COMPOSITE={0}", buildMode.OneBigComposite);
            commandLine.AppendFormat(" --build-arg APP_AVX2={0}", buildMode.AppAVX2);

            ProcessStartInfo psi = new ProcessStartInfo()
            {
                FileName = "docker",
                Arguments = commandLine.ToString(),
                UseShellExecute = false,
            };

            string imageId = null;
            int exitCode = RunProcess(psi, s_buildLogFile, out List<string> stdout);
            if (exitCode == 0)
            {
                for (int i = stdout.Count - 1; i >= 0 && i >= stdout.Count - 10; i--)
                {
                    string line = stdout[i];
                    int writingImage = line.IndexOf(WindowsWritingImageString);
                    if (writingImage >= 0)
                    {
                        imageId = line.Substring(writingImage + WindowsWritingImageString.Length);
                        break;
                    }
                }
            }
            Console.WriteLine("Done building configuration: {0} ({1} / {2}, {3} msecs)", buildMode.Name, index, total, sw.ElapsedMilliseconds);
            return imageId;
        }

        private static bool Run(string dockerImageId, StringBuilder xml)
        {
            StringBuilder commandLine = new StringBuilder();
            commandLine.Append("run");
            commandLine.AppendFormat(" --env COMPlus_TieredCompilation={0}", UseTieredCompilation ? "1" : "0");
            commandLine.AppendFormat(" --env COMPlus_ReadyToRun={0}", UseReadyToRun ? "1" : "0");
            commandLine.AppendFormat(" -it {0}", dockerImageId);

            ProcessStartInfo psi = new ProcessStartInfo()
            {
                FileName = "docker",
                Arguments = commandLine.ToString(),
                UseShellExecute = false,
            };

            int exitCode = RunProcess(psi, s_execLogFile, out List<string> stdout);
            if (exitCode != 143)
            {
                return false;
            }
            for (int line = 0; line < stdout.Count; line++)
            {
                if (stdout[line] == "XMLXMLXML")
                {
                    int startLine = ++line;
                    while (line < stdout.Count && stdout[line] != "LMXLMXLMX")
                    {
                        xml.AppendLine(stdout[line++]);
                    }
                }
            }
            return true;
        }

        private static int RunProcess(ProcessStartInfo psi, TextWriter logFile, out List<string> stdout)
        {
            Stopwatch sw = Stopwatch.StartNew();

            using (Process process = new Process())
            {
                process.StartInfo = psi;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.Environment["DOCKER_BUILDKIT"] = "1";

                logFile.WriteLine("Running {0} {1}", psi.FileName, psi.Arguments);
                process.Start();

                List<string> stdoutLines = new List<string>();
                process.OutputDataReceived += new DataReceivedEventHandler((object sender, DataReceivedEventArgs eventArgs) =>
                {
                    string data = eventArgs?.Data;
                    if (!string.IsNullOrEmpty(data))
                    {
                        Console.WriteLine(data);
                        logFile.WriteLine(data);
                        stdoutLines.Add(data);
                    }
                });
                process.ErrorDataReceived += new DataReceivedEventHandler((object sender, DataReceivedEventArgs eventArgs) =>
                {
                    string data = eventArgs?.Data;
                    if (!string.IsNullOrEmpty(data))
                    {
                        Console.Error.WriteLine(data);
                        logFile.WriteLine("!!" + data);
                        stdoutLines.Add(data);
                    }
                });

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                logFile.WriteLine(
                    "Finished in {0} msecs with exit code {1}: {2} {3}",
                    sw.ElapsedMilliseconds,
                    process.ExitCode,
                    psi.FileName,
                    psi.Arguments);

                stdout = stdoutLines;

                return process.ExitCode;
            }
        }

        struct ValueStatistics
        {
            public int Count;
            public int Sum;
            public long SumSquared;
            public int Min;
            public int Max;

            public int NonzeroCount => Math.Max(Count, 1);
            public int Average => Sum / NonzeroCount;
            public long Variance
            {
                get
                {
                    long avg = Average;
                    return avg * avg + (SumSquared - 2 * avg * Sum) / NonzeroCount;
                }
            }
            public int StandardDeviation => (int)Math.Sqrt(Variance);

            public void Add(int value)
            {
                if (Count == 0)
                {
                    Min = value;
                    Max = value;
                }
                else
                {
                    if (value < Min)
                    {
                        Min = value;
                    }
                    else if (value > Max)
                    {
                        Max = value;
                    }
                }
                Count++;
                Sum += value;
                SumSquared += (long)value * (long)value;
            }

            public void WriteTo(StringBuilder builder, string name)
            {
                builder.AppendLine($"{name,-30}: COUNT={Count,-5} AVG={Average,-5} INTERVAL={Max - Min,-5} STDDEV={StandardDeviation,-5}");
            }
        }

        struct PhaseStatistics
        {
            public ValueStatistics Total;
            public ValueStatistics User;
            public ValueStatistics System;

            public void Add(int total, int user, int system)
            {
                Total.Add(total);
                User.Add(user);
                System.Add(system);
            }

            public void WriteTo(StringBuilder builder, string name)
            {
                Total.WriteTo(builder, name + " (total)");
                User.WriteTo(builder, name + " (user)");
                System.WriteTo(builder, name + " (system)");
            }
        }

        private static void ProcessXmlFile(string xmlFile, string resultsFile)
        {
            XmlDocument xmlDocument = new XmlDocument();
            xmlDocument.Load(xmlFile);

            StringBuilder details = new StringBuilder();
            details.AppendLine("Details");
            details.AppendLine("=======");

            StringBuilder summary = new StringBuilder();
            summary.AppendLine("Summary");
            summary.AppendLine("=======");
            summary.AppendLine("TOTAL |  %  | RUNTIME |  %  | APPHOST |  %  | WEBHOST |  %  |   APP   |  %  | MODE");
            summary.AppendLine("==================================================================================");
            int baselineTotal = 0;
            int baselineRuntime = 0;
            int baselineApphostDelta = 0;
            int baselineWebhostDelta = 0;
            int baselineAppDelta = 0;
            bool isBaseline = true;

            foreach (XmlNode buildAndRun in xmlDocument.GetElementsByTagName("BuildAndRun"))
            {
                string name = buildAndRun.Attributes["Name"].InnerText;
                bool netCoreComposite = bool.Parse(buildAndRun["NetCoreComposite"].InnerText);
                bool netCoreIncludeAspNet = bool.Parse(buildAndRun["NetCoreIncludeAspNet"].InnerText);
                bool aspNetComposite = bool.Parse(buildAndRun["AspNetComposite"].InnerText);
                bool appR2R = bool.Parse(buildAndRun["AppR2R"].InnerText);
                bool appComposite = bool.Parse(buildAndRun["AppComposite"].InnerText);
                bool oneBigComposite = bool.Parse(buildAndRun["OneBigComposite"].InnerText);
                bool appAvx2 = bool.Parse(buildAndRun["AppAVX2"].InnerText);
                bool useTieredCompilation = bool.Parse(buildAndRun["UseTieredCompilation"].InnerText);
                bool useReadyToRun = bool.Parse(buildAndRun["UseReadyToRun"].InnerText);

                PhaseStatistics runtime = new PhaseStatistics();
                PhaseStatistics app = new PhaseStatistics();
                PhaseStatistics appHostInit = new PhaseStatistics();
                PhaseStatistics webHostStartAsync = new PhaseStatistics();

                foreach (XmlNode result in buildAndRun["Results"].ChildNodes)
                {
                    string phase = result.Attributes["Phase"].InnerText;
                    int totalMsecs = int.Parse(result["TotalTimeMsec"].InnerText);
                    int userMsecs = int.Parse(result["UserTimeMsec"].InnerText);
                    int systemMsecs = int.Parse(result["SystemTimeMsec"].InnerText);

                    switch (phase)
                    {
                        case "APP":
                            app.Add(totalMsecs, userMsecs, systemMsecs);
                            break;

                        case "RUNTIME":
                            runtime.Add(totalMsecs, userMsecs, systemMsecs);
                            break;

                        case "APPHOST-INIT":
                            appHostInit.Add(totalMsecs, userMsecs, systemMsecs);
                            break;

                        case "WEBHOST-START-ASYNC":
                            webHostStartAsync.Add(totalMsecs, userMsecs, systemMsecs);
                            break;

                        default:
                            throw new NotImplementedException("Unknown phase: " + phase);
                    }
                }

                StringBuilder buildModeName = new StringBuilder();
                buildModeName.Append(name);
                buildModeName.Append(": ");
                if (oneBigComposite)
                {
                    buildModeName.Append("one big composite");
                }
                else
                {
                    buildModeName.AppendFormat(".NET Core{0}={1}",
                        netCoreIncludeAspNet ? "+ASP.NET" : "",
                        netCoreComposite ? "composite" : "default");
                    if (!netCoreIncludeAspNet)
                    {
                        buildModeName.AppendFormat(" / ASP.NET={0}", aspNetComposite ? "composite" : "default");
                    }
                    buildModeName.AppendFormat(" / APP={0}", !appR2R ? "JIT" : !appComposite ? "R2R" : "composite");
                }
                if (appAvx2)
                {
                    buildModeName.Append(" / AVX2");
                }
                buildModeName.AppendFormat(" / TC {0}", useTieredCompilation ? "ON" : "OFF");
                buildModeName.AppendFormat(" / RTR {0}", useReadyToRun ? "ON" : "OFF");

                details.AppendLine(buildModeName.ToString());
                details.AppendLine(new string('=', buildModeName.Length));
                runtime.WriteTo(details, "RUNTIME");
                appHostInit.WriteTo(details, "APPHOST-INIT");
                webHostStartAsync.WriteTo(details, "WEBHOST-START-ASYNC");
                app.WriteTo(details, "APP");
                details.AppendLine();

                int apphostDelta = appHostInit.Total.Average - runtime.Total.Average;
                int webhostDelta = webHostStartAsync.Total.Average - appHostInit.Total.Average;
                int appDelta = app.Total.Average - webHostStartAsync.Total.Average;
                if (isBaseline)
                {
                    isBaseline = false;
                    baselineTotal = app.Total.Average;
                    baselineRuntime = runtime.Total.Average;
                    baselineApphostDelta = apphostDelta;
                    baselineWebhostDelta = webhostDelta;
                    baselineAppDelta = appDelta;
                }

                summary.AppendFormat("{0,5} | {1,3} | ", app.Total.Average, Percentage(app.Total.Average, baselineTotal));
                summary.AppendFormat("{0,7} | {1,3} | ", runtime.Total.Average, Percentage(runtime.Total.Average, baselineRuntime));
                summary.AppendFormat("{0,7} | {1,3} | ", apphostDelta, Percentage(apphostDelta, baselineApphostDelta));
                summary.AppendFormat("{0,7} | {1,3} | ", webhostDelta, Percentage(webhostDelta, baselineWebhostDelta));
                summary.AppendFormat("{0,7} | {1,3} | ", appDelta, Percentage(appDelta, baselineAppDelta));
                summary.AppendLine(name);
            }

            string results = summary.ToString() + Environment.NewLine + details.ToString();

            Console.Write(results);
            File.WriteAllText(resultsFile, results);
        }

        private static int Percentage(int numerator, int denominator)
        {
            return (int)(numerator * 100.0 / Math.Max(denominator, 1));
        }
    }
}
