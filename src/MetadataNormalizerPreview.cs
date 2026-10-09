using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace DJLibrary
{
    internal sealed class MetadataNormalizerProposalRow
    {
        public int FieldIndex { get; private set; }
        public string Field { get; private set; }
        public int ValueIndex { get; private set; }
        public string Original { get; private set; }
        public string Proposed { get; private set; }
        public string Safety { get; private set; }
        public string RuleIdsText { get; private set; }
        public string RationalesText { get; private set; }

        internal MetadataNormalizerProposalRow(
            int fieldIndex,
            string field,
            int valueIndex,
            string original,
            string proposed,
            string safety,
            string ruleIdsText,
            string rationalesText)
        {
            FieldIndex = fieldIndex;
            Field = field ?? "";
            ValueIndex = valueIndex;
            Original = original ?? "";
            Proposed = proposed ?? "";
            Safety = safety ?? "";
            RuleIdsText = ruleIdsText ?? "";
            RationalesText = rationalesText ?? "";
        }
    }

    internal sealed class MetadataNormalizerPreviewModel
    {
        public string BridgeTagFingerprint { get; private set; }
        public string NativeInputFingerprint { get; private set; }
        public string RulesetRevision { get; private set; }
        public string RulesSourceLabel { get; private set; }
        public string RulesSourcePath { get; private set; }
        public List<MetadataNormalizerProposalRow> Proposals { get; private set; }
        public int SafeCount { get; private set; }
        public int ConfidentCount { get; private set; }
        public int ReviewCount { get; private set; }

        internal MetadataNormalizerPreviewModel(
            string bridgeTagFingerprint,
            string nativeInputFingerprint,
            string rulesetRevision,
            string rulesSourceLabel,
            string rulesSourcePath,
            List<MetadataNormalizerProposalRow> proposals)
        {
            BridgeTagFingerprint = bridgeTagFingerprint ?? "";
            NativeInputFingerprint = nativeInputFingerprint ?? "";
            RulesetRevision = rulesetRevision ?? "";
            RulesSourceLabel = rulesSourceLabel ?? "";
            RulesSourcePath = rulesSourcePath ?? "";
            Proposals = proposals ?? new List<MetadataNormalizerProposalRow>();

            foreach (MetadataNormalizerProposalRow proposal in Proposals)
            {
                if (String.Equals(proposal.Safety, "SAFE", StringComparison.Ordinal)) SafeCount++;
                else if (String.Equals(proposal.Safety, "CONFIDENT", StringComparison.Ordinal)) ConfidentCount++;
                else if (String.Equals(proposal.Safety, "REVIEW", StringComparison.Ordinal)) ReviewCount++;
            }
        }

        internal bool IsCurrentFor(DigitalItem item)
        {
            return item != null &&
                !String.IsNullOrWhiteSpace(BridgeTagFingerprint) &&
                String.Equals(BridgeTagFingerprint, item.Fingerprint, StringComparison.Ordinal);
        }
    }

    // Read-only projection of the native engine result for DJ Library UI.
    // This class never reimplements normalization rules and exposes no write API.
    internal static class MetadataNormalizerPreview
    {
        internal static MetadataNormalizerPreviewModel AnalyzeForPreview(DigitalItem item, string baseDirectory)
        {
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return AnalyzeForPreview(item, baseDirectory, roaming);
        }

        internal static MetadataNormalizerPreviewModel AnalyzeForPreview(
            DigitalItem item,
            string baseDirectory,
            string roamingAppData)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (String.IsNullOrWhiteSpace(item.Fingerprint))
                throw new InvalidDataException("Bridge item has no tag_fingerprint; stale-input protection cannot be established.");

            MetadataNormalizerAnalysisResult analysis = MetadataNormalizerAnalysis.Analyze(item, baseDirectory, roamingAppData);
            MetadataNormalizerPreviewModel model = Parse(analysis);
            if (!model.IsCurrentFor(item))
                throw new InvalidDataException("Bridge tag_fingerprint changed during normalization preview analysis.");
            return model;
        }

        internal static MetadataNormalizerPreviewModel Parse(MetadataNormalizerAnalysisResult analysis)
        {
            if (analysis == null) throw new ArgumentNullException("analysis");
            if (String.IsNullOrWhiteSpace(analysis.AnalysisJson))
                throw new InvalidDataException("DJ Metadata Normalizer returned an empty analysis document.");

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 16 * 1024 * 1024;
            Dictionary<string, object> root = AsDictionary(serializer.DeserializeObject(analysis.AnalysisJson), "analysis root");
            if (GetInt(root, "schema_version") != 1)
                throw new InvalidDataException("Unsupported DJ Metadata Normalizer analysis schema.");

            string nativeInputFingerprint = GetString(root, "input_fingerprint");
            string rulesetRevision = GetString(root, "ruleset_revision");
            if (String.IsNullOrWhiteSpace(nativeInputFingerprint))
                throw new InvalidDataException("DJ Metadata Normalizer analysis is missing input_fingerprint.");
            if (String.IsNullOrWhiteSpace(rulesetRevision))
                throw new InvalidDataException("DJ Metadata Normalizer analysis is missing ruleset_revision.");

            List<MetadataNormalizerProposalRow> proposals = new List<MetadataNormalizerProposalRow>();
            object rawProposals;
            if (!root.TryGetValue("proposals", out rawProposals))
                throw new InvalidDataException("DJ Metadata Normalizer analysis is missing proposals.");

            IEnumerable sequence = rawProposals as IEnumerable;
            if (sequence == null || rawProposals is string)
                throw new InvalidDataException("DJ Metadata Normalizer proposals are not an array.");

            foreach (object raw in sequence)
            {
                Dictionary<string, object> proposal = AsDictionary(raw, "proposal");
                string safety = GetString(proposal, "safety");
                if (!String.Equals(safety, "SAFE", StringComparison.Ordinal) &&
                    !String.Equals(safety, "CONFIDENT", StringComparison.Ordinal) &&
                    !String.Equals(safety, "REVIEW", StringComparison.Ordinal))
                    throw new InvalidDataException("Unknown DJ Metadata Normalizer safety class: " + safety);

                proposals.Add(new MetadataNormalizerProposalRow(
                    GetInt(proposal, "field_index"),
                    GetString(proposal, "field"),
                    GetInt(proposal, "value_index"),
                    GetString(proposal, "original"),
                    GetString(proposal, "proposed"),
                    safety,
                    JoinStrings(proposal, "rule_ids"),
                    JoinStrings(proposal, "rationales")));
            }

            return new MetadataNormalizerPreviewModel(
                analysis.InputTagFingerprint,
                nativeInputFingerprint,
                rulesetRevision,
                analysis.RulesSourceLabel,
                analysis.RulesSourcePath,
                proposals);
        }

        internal static void ValidateSelfTest(string baseDirectory)
        {
            DigitalItem item = new DigitalItem();
            item.Fingerprint = "bridge-tag-fingerprint-preview-self-test";
            item.MetadataVectorsJson = "[{\"name\":\"TITLE\",\"values\":[\"  A   Title  \"]},{\"name\":\"ARTIST\",\"values\":[\"Artist\"]}]";
            string originalVectors = item.MetadataVectorsJson;
            string testRoaming = Path.Combine(Path.GetTempPath(), "DJLibrary-NormalizerPreview-" + Guid.NewGuid().ToString("N"));

            MetadataNormalizerPreviewModel model = AnalyzeForPreview(item, baseDirectory, testRoaming);
            if (!model.IsCurrentFor(item))
                throw new InvalidDataException("DJ Metadata Normalizer preview rejected its current Bridge fingerprint.");
            if (!String.Equals(model.RulesetRevision, "2026-10-07.2", StringComparison.Ordinal))
                throw new InvalidDataException("DJ Metadata Normalizer preview lost the ruleset revision.");
            if (!String.Equals(model.RulesSourceLabel, "Bundled default ruleset", StringComparison.Ordinal))
                throw new InvalidDataException("DJ Metadata Normalizer preview lost ruleset source provenance.");
            if (model.Proposals.Count != 1 ||
                !String.Equals(model.Proposals[0].Field, "TITLE", StringComparison.Ordinal) ||
                !String.Equals(model.Proposals[0].Original, "  A   Title  ", StringComparison.Ordinal) ||
                !String.Equals(model.Proposals[0].Proposed, "A Title", StringComparison.Ordinal) ||
                !String.Equals(model.Proposals[0].Safety, "SAFE", StringComparison.Ordinal) ||
                model.Proposals[0].RuleIdsText.IndexOf("safe.trim-whitespace", StringComparison.Ordinal) < 0 ||
                model.Proposals[0].RuleIdsText.IndexOf("safe.collapse-whitespace", StringComparison.Ordinal) < 0)
                throw new InvalidDataException("DJ Metadata Normalizer preview projection contract failed.");
            if (!String.Equals(item.MetadataVectorsJson, originalVectors, StringComparison.Ordinal))
                throw new InvalidDataException("DJ Metadata Normalizer preview mutated Bridge metadata vectors.");

            DigitalItem stale = new DigitalItem();
            stale.Fingerprint = item.Fingerprint + "-changed";
            stale.MetadataVectorsJson = item.MetadataVectorsJson;
            if (model.IsCurrentFor(stale))
                throw new InvalidDataException("DJ Metadata Normalizer stale-input guard accepted a changed Bridge fingerprint.");

            DigitalItem missingFingerprint = new DigitalItem();
            missingFingerprint.MetadataVectorsJson = item.MetadataVectorsJson;
            ExpectFailure(delegate { AnalyzeForPreview(missingFingerprint, baseDirectory, testRoaming); }, "missing tag_fingerprint");
        }

        private static Dictionary<string, object> AsDictionary(object value, string label)
        {
            Dictionary<string, object> dictionary = value as Dictionary<string, object>;
            if (dictionary == null) throw new InvalidDataException("DJ Metadata Normalizer " + label + " is not an object.");
            return dictionary;
        }

        private static string GetString(Dictionary<string, object> dictionary, string key)
        {
            object value;
            if (!dictionary.TryGetValue(key, out value) || value == null)
                throw new InvalidDataException("DJ Metadata Normalizer analysis is missing " + key + ".");
            string text = value as string;
            if (text == null)
                throw new InvalidDataException("DJ Metadata Normalizer analysis field " + key + " is not a string.");
            return text;
        }

        private static int GetInt(Dictionary<string, object> dictionary, string key)
        {
            object value;
            if (!dictionary.TryGetValue(key, out value) || value == null)
                throw new InvalidDataException("DJ Metadata Normalizer analysis is missing " + key + ".");
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("DJ Metadata Normalizer analysis field " + key + " is not an integer.", ex);
            }
        }

        private static string JoinStrings(Dictionary<string, object> dictionary, string key)
        {
            object value;
            if (!dictionary.TryGetValue(key, out value) || value == null)
                throw new InvalidDataException("DJ Metadata Normalizer analysis is missing " + key + ".");
            IEnumerable sequence = value as IEnumerable;
            if (sequence == null || value is string)
                throw new InvalidDataException("DJ Metadata Normalizer analysis field " + key + " is not an array.");

            List<string> values = new List<string>();
            foreach (object raw in sequence)
            {
                string text = raw as string;
                if (text == null)
                    throw new InvalidDataException("DJ Metadata Normalizer analysis field " + key + " contains a non-string value.");
                values.Add(text);
            }
            return String.Join("; ", values.ToArray());
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
            throw new InvalidDataException("DJ Metadata Normalizer preview did not fail closed for " + scenario + ".");
        }
    }
}
