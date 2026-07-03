using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Zori.ClapRouter;

namespace Zori.ClapRouter.Tests
{
    public sealed class GoldenVectorTests
    {
        private static IEnumerable<TestCaseData> EventCases()
        {
            yield return Case("note_on", MrEvent.NoteOn(2, 480, 7, 0, 3, 60, 0.8, true));
            yield return Case("note_off", MrEvent.NoteOff(2, 960, 7, 0, 3, 60, 0.0, false));
            yield return Case("note_choke", MrEvent.NoteChoke(1, 1024, -1, 0, 0, 64, false));
            yield return Case("note_expr_pitchbend", MrEvent.NoteExpr(0, 512, MrExpr.Tuning, 5, 0, 1, 48, 2.0));
            yield return Case("note_expr_pressure", MrEvent.NoteExpr(0, 600, MrExpr.Pressure, 5, 0, 1, 48, 0.75));
            yield return Case("param_value", MrEvent.ParamValue(1, MrDest.Instrument, 0, 42, 0.5));
            yield return Case("param_mod", MrEvent.ParamMod(1, 0, 256, 7, -0.25));
            yield return Case("midi1", MrEvent.Midi1(3, 128, 0x90, 0x3C, 0x64, 0, true));
            yield return Case("midi2", MrEvent.Midi2(3, 2048, 0x40903C00u, 0xFFFF0000u, 0u, 0u, 1, false));
        }

        private static MrEvent[] RingSequence()
        {
            return new[]
            {
                MrEvent.NoteOn(0, 100, 1, 0, 0, 40, 1.0, true),
                MrEvent.NoteExpr(0, 140, MrExpr.Tuning, 1, 0, 0, 40, -12.0),
                MrEvent.NoteOff(0, 400, 1, 0, 0, 40, 0.0, false)
            };
        }

        [Test]
        public void MrEvent_is_48_bytes()
        {
            Assert.AreEqual(48, Marshal.SizeOf<MrEvent>());
        }

        [TestCaseSource(nameof(EventCases))]
        public void Encoded_event_matches_golden(string name, MrEvent ev)
        {
            byte[] encoded = ToBytes(ev);
            byte[] golden = File.ReadAllBytes(GoldenPath(name + ".bin"));

            Assert.AreEqual(48, golden.Length, $"{name}: golden .bin is not 48 bytes");
            CollectionAssert.AreEqual(golden, encoded, $"{name}: encoded bytes differ from golden");
        }

        [Test]
        public void Ring_sequence_matches_golden()
        {
            MrEvent[] sequence = RingSequence();
            byte[] concatenated = new byte[sequence.Length * 48];
            for (int i = 0; i < sequence.Length; i++)
            {
                ToBytes(sequence[i]).CopyTo(concatenated, i * 48);
            }

            byte[] golden = File.ReadAllBytes(GoldenPath("ring_roundtrip.bin"));

            Assert.AreEqual(sequence.Length * 48, golden.Length, "ring_roundtrip: golden size mismatch");
            CollectionAssert.AreEqual(golden, concatenated, "ring_roundtrip: slot bytes differ from golden");
        }

        private static IEnumerable<TestCaseData> ParamInfoCases()
        {
            yield return ParamCase("param_info_continuous", new MrParamInfo
            {
                Id = 1024, Flags = MrParamFlags.IsAutomatable,
                MinValue = 0.0, MaxValue = 1.0, DefaultValue = 1.0, CurrentValue = 0.15
            });
            yield return ParamCase("param_info_stepped", new MrParamInfo
            {
                Id = 1026, Flags = MrParamFlags.IsStepped | MrParamFlags.IsAutomatable | MrParamFlags.IsEnum,
                MinValue = 0.0, MaxValue = 2.0, DefaultValue = 0.0, CurrentValue = 1.0
            });
        }

        private static IEnumerable<TestCaseData> NotificationCases()
        {
            yield return NotifyCase("notification_rescan", new MrNotification
            {
                Kind = MrNotifyKind.ParamRescan, TrackId = 1, DestSlot = -1, ParamId = 0,
                RescanFlags = MrRescanFlags.Values | MrRescanFlags.Text, Value = 0.0
            });
            yield return NotifyCase("notification_value", new MrNotification
            {
                Kind = MrNotifyKind.ParamValue, TrackId = 2, DestSlot = 0, ParamId = 1024,
                RescanFlags = MrRescanFlags.None, Value = 0.42
            });
        }

        [Test]
        public void MrParamInfo_is_40_bytes()
        {
            Assert.AreEqual(40, Marshal.SizeOf<MrParamInfo>());
        }

        [Test]
        public void MrNotification_is_32_bytes()
        {
            Assert.AreEqual(32, Marshal.SizeOf<MrNotification>());
        }

        [TestCaseSource(nameof(ParamInfoCases))]
        public void Encoded_param_info_matches_golden(string name, MrParamInfo info)
        {
            byte[] encoded = ToBytes(info);
            byte[] golden = File.ReadAllBytes(GoldenPath(name + ".bin"));

            Assert.AreEqual(40, golden.Length, $"{name}: golden .bin is not 40 bytes");
            CollectionAssert.AreEqual(golden, encoded, $"{name}: encoded bytes differ from golden");
        }

        [TestCaseSource(nameof(NotificationCases))]
        public void Encoded_notification_matches_golden(string name, MrNotification notification)
        {
            byte[] encoded = ToBytes(notification);
            byte[] golden = File.ReadAllBytes(GoldenPath(name + ".bin"));

            Assert.AreEqual(32, golden.Length, $"{name}: golden .bin is not 32 bytes");
            CollectionAssert.AreEqual(golden, encoded, $"{name}: encoded bytes differ from golden");
        }

        [Test]
        public void Cases_cover_manifest()
        {
            string manifest = File.ReadAllText(GoldenPath("manifest.json"));
            HashSet<string> manifestNames = new HashSet<string>();
            foreach (Match m in Regex.Matches(manifest, "\"name\"\\s*:\\s*\"([^\"]+)\""))
            {
                manifestNames.Add(m.Groups[1].Value);
            }

            HashSet<string> coded = new HashSet<string>();
            foreach (TestCaseData c in EventCases())
            {
                coded.Add((string)c.Arguments[0]);
            }
            foreach (TestCaseData c in ParamInfoCases())
            {
                coded.Add((string)c.Arguments[0]);
            }
            foreach (TestCaseData c in NotificationCases())
            {
                coded.Add((string)c.Arguments[0]);
            }

            CollectionAssert.AreEquivalent(manifestNames, coded,
                "C# golden cases must cover exactly the manifest.json case set");
        }

        private static TestCaseData ParamCase(string name, MrParamInfo info)
        {
            return new TestCaseData(name, info).SetName($"Encoded_param_info_matches_golden({name})");
        }

        private static TestCaseData NotifyCase(string name, MrNotification notification)
        {
            return new TestCaseData(name, notification).SetName($"Encoded_notification_matches_golden({name})");
        }

        private static TestCaseData Case(string name, MrEvent ev)
        {
            return new TestCaseData(name, ev).SetName($"Encoded_event_matches_golden({name})");
        }

        private static byte[] ToBytes<T>(T value) where T : unmanaged
        {
            System.ReadOnlySpan<T> one = MemoryMarshal.CreateReadOnlySpan(ref value, 1);
            return MemoryMarshal.AsBytes(one).ToArray();
        }

        private static string GoldenPath(string file, [CallerFilePath] string thisFile = "")
        {
            string testsDir = Path.GetDirectoryName(thisFile);
            string goldenDir = Path.GetFullPath(
                Path.Combine(testsDir, "..", "..", "..", "packages", "music-router", "spec", "golden"));
            return Path.Combine(goldenDir, file);
        }
    }
}
