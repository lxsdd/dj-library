using System;
using System.IO;
using System.Text;

namespace DJLibrary
{
    internal sealed class MetadataNormalizerAnalysisResult
    {
        internal string InputTagFingerprint { get; private set; }
        internal string AnalysisJson { get; private set; }
        internal string RulesSourceLabel { get; private set; }
        internal string RulesSourcePath { get; private set; }

        internal MetadataNormalizerAnalysisResult(
            string inputTagFingerprint,
            string analysisJson,
            string rulesSourceLabel,
            string rulesSourcePath)
        {
            InputTagFingerprint = inputTagFingerprint;
            AnalysisJson = analysisJson;
            RulesSourceLabel = rulesSourceLabel ?? "";
            RulesSourcePath = rulesSourcePath ?? "";
        }
    }

    internal sealed class MetadataNormalizerRulesText
    {
        internal string Json { get; private set; }
        internal string SourceLabel { get; private set; }
        internal string SourcePath { get; private set; }

        internal MetadataNormalizerRulesText(string json, string sourceLabel, string sourcePath)
        {
            Json = json;
            SourceLabel = sourceLabel;
            SourcePath = sourcePath;
        }
    }

    // Analysis-only orchestration. Original Bridge values stay on DigitalItem;
    // canonical proposals are returned as opaque native analysis JSON.
    internal static class MetadataNormalizerAnalysis
    {
        internal const string RulesRelativePath = @"rules\default-rules.json";
        internal const string SharedRulesRelativePath = @"DJMetadataNormalizer\ruleset.json";
        private const long MaxRulesBytes = 16L * 1024L * 1024L;

        internal static void ValidateSelfTest(string baseDirectory)
        {
            string testRoaming = Path.Combine(Path.GetTempPath(), "DJLibrary-NormalizerRules-" + Guid.NewGuid().ToString("N"));
            try
            {
                DigitalItem item = new DigitalItem();
                item.Fingerprint = "bridge-tag-fingerprint-self-test";
                item.MetadataVectorsJson = "[{\"name\":\"TITLE\",\"values\":[\"  A   Title  \"]},{\"name\":\"ARTIST\",\"values\":[\"Artist\"]}]";
                string originalVectors = item.MetadataVectorsJson;

                MetadataNormalizerAnalysisResult first = Analyze(item, baseDirectory, testRoaming);
                MetadataNormalizerAnalysisResult second = Analyze(item, baseDirectory, testRoaming);
                if (!String.Equals(first.AnalysisJson, second.AnalysisJson, StringComparison.Ordinal))
                    throw new InvalidDataException("DJ Metadata Normalizer analysis is not deterministic.");
                if (!String.Equals(first.InputTagFingerprint, item.Fingerprint, StringComparison.Ordinal))
                    throw new InvalidDataException("Bridge tag fingerprint was not preserved with the analysis result.");
                if (!String.Equals(first.RulesSourceLabel, "Bundled default ruleset", StringComparison.Ordinal))
                    throw new InvalidDataException("DJ Metadata Normalizer bundled rules fallback was not selected.");
                if (!String.Equals(item.MetadataVectorsJson, originalVectors, StringComparison.Ordinal))
                    throw new InvalidDataException("DJ Metadata Normalizer analysis mutated the original Bridge vectors.");
                if (first.AnalysisJson.IndexOf("\"ruleset_revision\":\"2026-10-07.2\"", StringComparison.Ordinal) < 0 ||
                    first.AnalysisJson.IndexOf("\"original\":\"  A   Title  \",\"proposed\":\"A Title\"", StringComparison.Ordinal) < 0 ||
                    first.AnalysisJson.IndexOf("\"rule_ids\":[\"safe.trim-whitespace\",\"safe.collapse-whitespace\"]", StringComparison.Ordinal) < 0)
                    throw new InvalidDataException("DJ Metadata Normalizer native preview contract failed.");

                DigitalItem missingVectors = new DigitalItem();
                ExpectFailure(delegate { Analyze(missingVectors, baseDirectory, testRoaming); }, "missing Bridge vectors");

                DigitalItem malformed = new DigitalItem();
                malformed.MetadataVectorsJson = "{not-json";
                ExpectFailure(delegate { Analyze(malformed, baseDirectory, testRoaming); }, "malformed Bridge vectors");

                string sharedDirectory = Path.Combine(testRoaming, "DJMetadataNormalizer");
                Directory.CreateDirectory(sharedDirectory);
                string bundledPath = Path.Combine(baseDirectory, RulesRelativePath);
                string sharedJson = File.ReadAllText(bundledPath, Encoding.UTF8)
                    .Replace("\"revision\": \"2026-10-07.2\"", "\"revision\": \"shared-self-test\"");
                string sharedPath = Path.Combine(sharedDirectory, "ruleset.json");
                File.WriteAllText(sharedPath, sharedJson, new UTF8Encoding(false));

                MetadataNormalizerAnalysisResult shared = Analyze(item, baseDirectory, testRoaming);
                if (shared.AnalysisJson.IndexOf("\"ruleset_revision\":\"shared-self-test\"", StringComparison.Ordinal) < 0)
                    throw new InvalidDataException("DJ Metadata Normalizer did not prefer the shared roaming ruleset.");
                if (!String.Equals(shared.RulesSourceLabel, "Shared ruleset", StringComparison.Ordinal) ||
                    !String.Equals(shared.RulesSourcePath, sharedPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("DJ Metadata Normalizer lost shared ruleset provenance.");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(testRoaming)) Directory.Delete(testRoaming, true);
                }
                catch { }
            }
        }

        private static void ExpectFailure(Action action, string scenario)
        {
            try
            {
                action();
            }
            catch
            {
                return;
            }
            throw new InvalidDataException("DJ Metadata Normalizer did not fail closed for " + scenario + ".");
        }

        internal static MetadataNormalizerAnalysisResult Analyze(DigitalItem item, string baseDirectory)
        {
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Analyze(item, baseDirectory, roaming);
        }

        internal static MetadataNormalizerAnalysisResult Analyze(
            DigitalItem item,
            string baseDirectory,
            string roamingAppData)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (String.IsNullOrWhiteSpace(item.MetadataVectorsJson))
                throw new InvalidDataException("Bridge item has no schema-v3 metadata_vectors_json.");
            if (String.IsNullOrWhiteSpace(baseDirectory))
                throw new ArgumentException("Base directory is required.", "baseDirectory");

            MetadataNormalizerRulesText rules = LoadRulesText(baseDirectory, roamingAppData);
            string analysisJson = MetadataNormalizerNative.Analyze(item.MetadataVectorsJson, rules.Json);
            return new MetadataNormalizerAnalysisResult(
                item.Fingerprint,
                analysisJson,
                rules.SourceLabel,
                rules.SourcePath);
        }

        internal static MetadataNormalizerRulesText LoadRulesText(string baseDirectory, string roamingAppData)
        {
            if (String.IsNullOrWhiteSpace(baseDirectory))
                throw new ArgumentException("Base directory is required.", "baseDirectory");

            if (!String.IsNullOrWhiteSpace(roamingAppData))
            {
                string sharedPath = Path.Combine(roamingAppData, SharedRulesRelativePath);
                if (File.Exists(sharedPath))
                    return new MetadataNormalizerRulesText(
                        ReadRulesFile(sharedPath),
                        "Shared ruleset",
                        sharedPath);
            }

            string bundledPath = Path.Combine(baseDirectory, RulesRelativePath);
            if (!File.Exists(bundledPath))
                throw new FileNotFoundException("DJ Metadata Normalizer ruleset is missing.", bundledPath);

            return new MetadataNormalizerRulesText(
                ReadRulesFile(bundledPath),
                "Bundled default ruleset",
                bundledPath);
        }

        private static string ReadRulesFile(string path)
        {
            FileInfo info = new FileInfo(path);
            if (info.Length < 0 || info.Length > MaxRulesBytes)
                throw new InvalidDataException("DJ Metadata Normalizer ruleset exceeds the 16 MiB safety limit.");

            string json = File.ReadAllText(path, Encoding.UTF8);
            if (String.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("DJ Metadata Normalizer ruleset is empty.");
            return json;
        }
    }
}
