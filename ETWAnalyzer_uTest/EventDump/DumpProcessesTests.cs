﻿//// SPDX-FileCopyrightText:  © 2022 Siemens Healthcare GmbH
//// SPDX-License-Identifier:   MIT

using ETWAnalyzer.EventDump;
using ETWAnalyzer.Extract;
using ETWAnalyzer.Infrastructure;
using ETWAnalyzer_uTest.TestInfrastructure;
using Microsoft.Diagnostics.Tracing.Parsers.FrameworkEventSource;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace ETWAnalyzer_uTest.EventDump
{
    public class DumpProcessesTests
    {

        private ITestOutputHelper myWriter;

        public DumpProcessesTests(ITestOutputHelper myWriter)
        {
            this.myWriter = myWriter;
        }


        [Fact]
        public void Can_Dump_OnlyStarted_Processes()
        {
            DumpProcesses processStart = new DumpProcesses()
            {
                NewProcessFilter = ETWProcess.ProcessStates.OnlyStarted
            };

            processStart.myPreloadedTests = new Lazy<SingleTest>[] { new Lazy<SingleTest>(CreateSingleTest) };
            List<DumpProcesses.MatchData> matching = processStart.ExecuteInternal();

            Assert.Single(matching);
            Assert.Equal("cmdStartOnly.exe", matching[0].ProcessName);
        }

        [Fact]
        public void Can_Dump_OnlyStopped_Processes()
        {
            DumpProcesses processStart = new DumpProcesses()
            {
                NewProcessFilter = ETWProcess.ProcessStates.OnlyStopped
            };

            processStart.myPreloadedTests = new Lazy<SingleTest>[] { new Lazy<SingleTest>(CreateSingleTest) };
            List<DumpProcesses.MatchData> matching = processStart.ExecuteInternal();

            Assert.Single(matching);
            Assert.Equal("cmdEndOnly.exe", matching[0].ProcessName);
        }

        [Fact]
        public void Can_Dump_Started_Processes()
        {
            DumpProcesses processStart = new DumpProcesses()
            {
                NewProcessFilter = ETWProcess.ProcessStates.Started
            };

            processStart.myPreloadedTests = new Lazy<SingleTest>[] { new Lazy<SingleTest>(CreateSingleTest) };
            List<DumpProcesses.MatchData> matching = processStart.ExecuteInternal();

            Assert.Equal(2, matching.Count);
            Assert.Equal("cmdStartStop.exe", matching[1].ProcessName);
            Assert.Equal("cmdStartOnly.exe", matching[0].ProcessName);
        }


        [Fact]
        public void ZeroPoint_ProcessStart_Shifts_Time_Correctly()
        {
            DumpProcesses processStart = new DumpProcesses()
            {
                ZeroTimeMode = ETWAnalyzer.Commands.DumpCommand.ZeroTimeModes.ProcessStart,
                ZeroTimeProcessNameFilter = Matcher.CreateMatcher("cmdStartOnly", MatchingMode.CaseInsensitive, true),
                NewProcessFilter = ETWProcess.ProcessStates.Started,

            };

            processStart.myPreloadedTests = new Lazy<SingleTest>[] { new Lazy<SingleTest>(CreateSingleTest) };
            List<DumpProcesses.MatchData> matching = processStart.ExecuteInternal();
            
            foreach(var m in matching)
            {
                Assert.Equal(15.0d, m.ZeroTimeS);
            }

            var match = matching.Where(x => x.ProcessName == "cmdStartStop.exe").Single();
            Assert.Equal(TenClock.AddSeconds(10.0d).AddSeconds(-15.0d), match.StartTime);
            Assert.Equal(TenClock.AddSeconds(20.0d).AddSeconds(-15.0d), match.EndTime);
        }

        [Fact]
        public void Verify_Process_Tree_Output()
        {
            using var testOutput = new ExceptionalPrinter(myWriter, true);
            using CultureSwitcher invariant = new();

            DumpProcesses dumper = new DumpProcesses()
            {
                //  Parent = Matcher.CreateMatcher("Parent", MatchingMode.CaseInsensitive, true),
                ProcessFormatOption = DumpBase.TimeFormats.second,
                SortOrder = ETWAnalyzer.Commands.DumpCommand.SortOrders.Tree,
            };

            dumper.myPreloadedTests = new Lazy<SingleTest>[] { new Lazy<SingleTest>(() => CreateProcessTree() )};

            List<DumpProcesses.MatchData> matching = dumper.ExecuteInternal();

            testOutput.Flush();
            string[] expectedOutput = new string[]
            {
                "1/1/2000 12:00:00 AM   test ",
                "123456  - 11.000  cmd.exe /EndOnly ",
                "5000     ImmortalParent.exe ",
                "  |- 5001     ImmortalChild.exe ",
                "500      ImmortalRoot.exe ",
                "1234    +10.000 - 20.000 00:00:10  Parent.exe hi ",
                "  |- 10      +12.000  cmd.exe /StartOnly ",
                "  |- 12345   +15.000 - 25.000 00:00:10  cmd.exe /TransientChild ",
                "1234    +-0.000 - 9.990 00:00:09.9900000  ParentWrong.exe hi ",
            };

            var lines = testOutput.GetSingleLines();
            Assert.Equal(expectedOutput.Length, lines.Count);
            for(int i=0;i<expectedOutput.Length;i++)
            {
                Assert.Equal(expectedOutput[i], lines[i]);
            }
        }


        [Fact]
        public void Process_Filter_DisplaysJustMatchingProcesses_When_ParentFilter_Is_NotActive()
        {
            using var testOutput = new ExceptionalPrinter(myWriter, true);
            using CultureSwitcher invariant = new();

            DumpProcesses dumper = new DumpProcesses()
            {
                ProcessNameFilter = Matcher.CreateMatcher("ImmortalChild", MatchingMode.CaseInsensitive, true),
            };

            dumper.myPreloadedTests = new Lazy<SingleTest>[] { new Lazy<SingleTest>(() => CreateProcessTree()) };

            List<DumpProcesses.MatchData> matching = dumper.ExecuteInternal();

            testOutput.Flush();
            string[] expectedOutput = new string[]
            {
                "1/1/2000 12:00:00 AM   test ",
                "PID: 5001   Start:  Stop:  Duration:  RCode:  Parent:  5000  ImmortalChild.exe "
            };

            var lines = testOutput.GetSingleLines();
            Assert.Equal(expectedOutput.Length, lines.Count);
            for (int i = 0; i < expectedOutput.Length; i++)
            {
                Assert.Equal(expectedOutput[i], lines[i]);
            }
        }


        [Fact]
        public void SessionFilter_Matches_Numbers_Exactly()
        {
            DumpProcesses dumper = new DumpProcesses()
            {
                //  Parent = Matcher.CreateMatcher("Parent", MatchingMode.CaseInsensitive, true),
                Session = Matcher.CreateMatcher("1"),
            };

            var procSession0 = new ETWProcess
            {
                SessionId = 0,
            };

            var procSession1 = new ETWProcess
            {
                SessionId = 1,
            };

            var procSession2 = new ETWProcess
            {
                SessionId = 2,
            };

            Assert.False(dumper.SessionIdFilter(procSession0));
            Assert.True( dumper.SessionIdFilter(procSession1));
            Assert.False(dumper.SessionIdFilter(procSession2));
        }

        DateTimeOffset TenClock = new DateTimeOffset(2000, 1, 1, 10, 0, 0, TimeSpan.Zero); // start at 10:00:00

        SingleTest CreateSingleTest()
        {
            TestDataFile file = new TestDataFile("Test", "test.etl", new DateTime(2000, 1, 1), 5000, 100, "TestMachine", "None", true);
            
            var extract = new ETWExtract()
            {
                SessionStart = TenClock,
            };

            ETWProcess startStop = new ETWProcess
            {
                ProcessID = 1234,
                ProcessName = "cmdStartStop.exe",
                CmdLine = "hi",
                HasEnded = true,
                IsNew = true,
                StartTime = TenClock + TimeSpan.FromSeconds(10), // Start at 10:00:10
                EndTime = TenClock + TimeSpan.FromSeconds(20),   // Stop at  10:00:20
            };
            extract.Processes.Add(startStop);

            ETWProcess startOnly = new ETWProcess
            {
                ProcessID = 12345,
                ProcessName = "cmdStartOnly.exe",
                HasEnded = false,
                IsNew = true,
                StartTime = TenClock + TimeSpan.FromSeconds(15)  // Start at 10:00:15
            };
            extract.Processes.Add(startOnly);

            ETWProcess endOnly = new ETWProcess
            {
                ProcessID = 123456,
                ProcessName = "cmdEndOnly.exe",
                HasEnded = true,
                IsNew = false,
                EndTime = TenClock + TimeSpan.FromSeconds(5),   // End at 10:00:05
            };
            extract.Processes.Add(endOnly);

            ETWProcess immortal = new ETWProcess
            {
                ProcessID = 4,
                ProcessName = "cmdImmortal.exe",
                HasEnded = false,
                IsNew = false,
            };
            extract.Processes.Add(immortal);

            file.Extract = extract;
            SingleTest t = new SingleTest(new TestDataFile[] { file });

            return t;
        }

        [Fact]
        public void CanConvert_ProcessTree_WithOneParent_One_Child()
        {
            SingleTest test = CreateProcessTree(false, false, false, false);

            IETWExtract extract = test.Files[0].Extract;
            List<DumpProcesses.MatchData> matches = new List<DumpProcesses.MatchData>();
            foreach (var proc in extract.Processes)
            {
                matches.Add(new DumpProcesses.MatchData
                {
                    EndTime = proc.EndTime == DateTimeOffset.MaxValue ? null : proc.EndTime,
                    StartTime = proc.StartTime == DateTimeOffset.MinValue ? null : proc.StartTime,
                    ProcessName = proc.ProcessName,
                    HasEnded = proc.HasEnded, 
                    ProcessId = proc.ProcessID,
                    ParentProcessId = proc.ParentPid,
                    PerformedAt = test.PerformedAt,
                    SessionId = proc.SessionId,
                    ReturnCode  = proc.ReturnCode,
                    User = proc.Identity,
                });
            }

            List<DumpProcesses.MatchData> tree = DumpProcesses.MatchData.ConvertToTree(matches);
            Assert.Single(tree);
            var root = tree.First();
            Assert.Equal("Parent.exe", root.ProcessName);
            Assert.Equal(1234, root.ProcessId);
            Assert.Single(root.Childs);
            var child = root.Childs[0];
            Assert.Empty(child.Childs);
            Assert.Equal("cmd.exe", child.ProcessName);
            Assert.Equal(12345, child.ProcessId);
        }

        [Fact]
        public void CanConvert_ComplexProcessTree()
        {
            SingleTest test = CreateProcessTree(true, true, true,true);

            IETWExtract extract = test.Files[0].Extract;
            List<DumpProcesses.MatchData> matches = new List<DumpProcesses.MatchData>();
            foreach (var proc in extract.Processes)
            {
                matches.Add(new DumpProcesses.MatchData
                {
                    EndTime = proc.EndTime == DateTimeOffset.MaxValue ? null : proc.EndTime,
                    StartTime = proc.StartTime == DateTimeOffset.MinValue ? null : proc.StartTime,
                    ProcessName = proc.ProcessName,
                    HasEnded = proc.HasEnded,
                    ProcessId = proc.ProcessID,
                    ParentProcessId = proc.ParentPid,
                    PerformedAt = test.PerformedAt,
                    SessionId = proc.SessionId,
                    ReturnCode = proc.ReturnCode,
                    User = proc.Identity,
                });
            }

            List<DumpProcesses.MatchData> tree = DumpProcesses.MatchData.ConvertToTree(matches);
            /*
             * 
            [0]: "cmd.exe(123456) Count: 0 "
            [1]: "ImmortalParent.exe(5000) Count: 1 5001"
            [2]: "ImmortalRoot.exe(500) Count: 0 "
            [3]: "Parent.exe(1234) Count: 2 12345\r\n10"
            [4]: "ParentWrong.exe(1234) Count: 0 "
             */

            Assert.Equal(5, tree.Count);
            var first = tree[0];
            Assert.Equal("cmd.exe", first.ProcessName);
            Assert.Equal(123456, first.ProcessId);
            Assert.Empty(first.Childs);
            Assert.Null(first.Parent);

            var second = tree[1];
            Assert.Equal("ImmortalParent.exe", second.ProcessName);
            Assert.Equal(5000, second.ProcessId);
            Assert.Single(second.Childs);
            Assert.Null(second.Parent);
            var secondChild = second.Childs[0];
            Assert.Equal(5001, secondChild.ProcessId);
            Assert.Equal("ImmortalChild.exe", secondChild.ProcessName);


            var third = tree[2]; ;
            Assert.Equal("ImmortalRoot.exe", third.ProcessName);
            Assert.Equal(500, third.ProcessId);
            Assert.Empty(third.Childs);
            Assert.Null(third.Parent);

            var parent = tree[3];
            Assert.Equal("Parent.exe", parent.ProcessName);
            Assert.Equal(1234, parent.ProcessId);
            Assert.Equal(2, parent.Childs.Count);
            Assert.Null(parent.Parent);

            var parentChild1 = parent.Childs[0];
            Assert.Equal("cmd.exe", parentChild1.ProcessName);
            Assert.Equal(12345, parentChild1.ProcessId);
            Assert.Empty(parentChild1.Childs);
            Assert.Equal(parent, parentChild1.Parent);

            var parentChild2 = parent.Childs[1];
            Assert.Equal("cmd.exe", parentChild2.ProcessName);
            Assert.Equal(10, parentChild2.ProcessId);
            Assert.Empty(parentChild2.Childs);
            Assert.Equal(parent, parentChild2.Parent);

            var fifth = tree[4];
            Assert.Equal("ParentWrong.exe", fifth.ProcessName);
            Assert.Equal(1234, fifth.ProcessId);
            Assert.Empty(fifth.Childs);
        }

        SingleTest CreateProcessTree(bool bParentWrong=true, bool bEndOnly=true, bool bStartOnly=true, bool bImmortal=true)
        {
            TestDataFile file = new TestDataFile("Test", "test.etl", new DateTime(2000, 1, 1), 5000, 100, "TestMachine", "None", true);

            var extract = new ETWExtract()
            {
                SessionStart = TenClock,
            };

            const int ParentPid = 1234;

            if (bParentWrong)
            {
                ETWProcess wrongParent = new ETWProcess
                {
                    ProcessID = ParentPid,
                    ParentPid = 9999,
                    ProcessName = "ParentWrong.exe",
                    CmdLine = "hi",
                    HasEnded = true,
                    IsNew = true,
                    StartTime = TenClock,                              // Start at 10:00:00
                    EndTime = TenClock + TimeSpan.FromSeconds(9.99),   // Stop at  10:00:09.99
                };
                extract.Processes.Add(wrongParent);
            }

            
            // Same Pid but started later as ParentWrong
            ETWProcess parent = new ETWProcess
            {
                ProcessID = ParentPid,
                ParentPid = 9999,
                ProcessName = "Parent.exe",
                CmdLine = "hi",
                HasEnded = true,
                IsNew = true,
                StartTime = TenClock + TimeSpan.FromSeconds(10), // Start at 10:00:10
                EndTime = TenClock + TimeSpan.FromSeconds(20),   // Stop at  10:00:20
            };
            extract.Processes.Add(parent);


            ETWProcess transientChild = new ETWProcess
            {
                ProcessID = 12345,
                ParentPid = ParentPid,
                ProcessName = "cmd.exe",
                CmdLine = "/TransientChild",
                HasEnded = true,
                IsNew = true,
                StartTime = TenClock + TimeSpan.FromSeconds(15),  // Start at 10:00:15
                EndTime = TenClock + TimeSpan.FromSeconds(25),    // Stop at  10:00:25
            };
            extract.Processes.Add(transientChild);

            if (bEndOnly)
            {
                ETWProcess cmdEndOnly = new ETWProcess
                {
                    ProcessID = 123456,
                    ParentPid = ParentPid,
                    ProcessName = "cmd.exe",
                    CmdLine = "/EndOnly",
                    HasEnded = true,
                    IsNew = false,
                    EndTime = TenClock + TimeSpan.FromSeconds(11),   // End at 10:00:11
                    StartTime = DateTimeOffset.MinValue,
                };
                extract.Processes.Add(cmdEndOnly);
            }

            if( bStartOnly )
            {
                ETWProcess cmdStartOnly = new ETWProcess
                {
                    ProcessID = 10,
                    ParentPid = ParentPid,
                    ProcessName = "cmd.exe",
                    CmdLine = "/StartOnly",
                    HasEnded = false,
                    IsNew = true,
                    StartTime = TenClock + TimeSpan.FromSeconds(12),   // End at 10:00:12
                    EndTime = DateTimeOffset.MaxValue,
                };
                extract.Processes.Add(cmdStartOnly);
            }

            if (bImmortal)
            {
                ETWProcess immortalParent = new ETWProcess
                {
                    ProcessID = 5000,
                    ParentPid = 4999,
                    ProcessName = "ImmortalParent.exe",
                    HasEnded = false,
                    IsNew = false,
                    EndTime = DateTimeOffset.MaxValue,
                };
                extract.Processes.Add(immortalParent);

                ETWProcess immortalChild = new ETWProcess
                {
                    ProcessID = 5001,
                    ParentPid = 5000,
                    ProcessName = "ImmortalChild.exe",
                    HasEnded = false,
                    IsNew = false,
                    EndTime = DateTimeOffset.MaxValue,
                };
                extract.Processes.Add(immortalChild);

                ETWProcess immortal = new ETWProcess
                {
                    ProcessID = 500,
                    ParentPid = 499,
                    ProcessName = "ImmortalRoot.exe",
                    HasEnded = false,
                    IsNew = false,
                    EndTime = DateTimeOffset.MaxValue,
                };
                extract.Processes.Add(immortal);
            }

            file.Extract = extract;
            SingleTest t = new SingleTest(new TestDataFile[] { file });

            return t;
        }
    }
}
