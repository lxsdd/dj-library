using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DJLibrary
{
    public sealed class CdMetadataBulkApplyResult
    {
        public string Source { get; set; }
        public int Applied { get; set; }
        public int Missing { get; set; }
        public int Ambiguous { get; set; }
        public int Considered { get { return Applied + Missing + Ambiguous; } }
        public bool Changed { get { return Applied > 0; } }

        public string Summary
        {
            get
            {
                string text = Applied.ToString(CultureInfo.CurrentCulture) + " field" + (Applied == 1 ? "" : "s") + " updated";
                if (Missing > 0) text += " · " + Missing.ToString(CultureInfo.CurrentCulture) + " without a source value left unchanged";
                if (Ambiguous > 0) text += " · " + Ambiguous.ToString(CultureInfo.CurrentCulture) + " ambiguous field" + (Ambiguous == 1 ? "" : "s") + " left unchanged";
                return text;
            }
        }
    }

    public sealed class CdMetadataBaseSetChoice
    {
        public string Source { get; set; }
        public string Status { get; set; }
        public string DisplayText
        {
            get
            {
                string source=Source??"";
                string prefix;
                if(String.Equals(source,"Recommended",StringComparison.OrdinalIgnoreCase)) prefix="Recommended values";
                else if(String.Equals(source,"Catalog",StringComparison.OrdinalIgnoreCase)) prefix="Keep Catalog values";
                else if(String.Equals(source,"Current",StringComparison.OrdinalIgnoreCase)) prefix="Keep existing values";
                else prefix="Use " + source + " as starting values";
                return String.IsNullOrWhiteSpace(Status)?prefix:(prefix + " · " + Status);
            }
        }
    }

    internal sealed class CdMetadataReviewState
    {
        public string BaseSource = "";
        public Dictionary<string, string> Overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed partial class CdMetadataSelectionSession
    {
        private readonly Dictionary<string, string> _overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string _baseSource = "";

        public string BaseSource { get { return String.IsNullOrWhiteSpace(_baseSource) ? "Current" : _baseSource; } }
        public int OverrideCount { get { return _overrides.Count; } }

        public IList<string> BaseSources
        {
            get
            {
                List<string> sources = _report.Evidence
                    .Where(x => x != null && !String.IsNullOrWhiteSpace(x.Value))
                    .Select(x => NormalizeSource(x.Source))
                    .Where(IsAllowedBaseSource)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(BaseSourcePriority)
                    .ThenBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                if (sources.Count == 0) sources.Add("Current");
                else if (!sources.Contains("Recommended",StringComparer.OrdinalIgnoreCase)) sources.Insert(0,"Recommended");
                return sources.AsReadOnly();
            }
        }

        public IList<CdMetadataBaseSetChoice> BaseSetChoices
        {
            get
            {
                return BaseSources.Select(delegate(string source)
                {
                    string status = BaseSourceStatus(source);
                    if (String.Equals(source, "Current", StringComparison.OrdinalIgnoreCase) && String.IsNullOrWhiteSpace(status))
                        status = "metadata state before this review";
                    return new CdMetadataBaseSetChoice { Source = source, Status = status };
                }).ToList().AsReadOnly();
            }
        }

        public CdMetadataBaseSetChoice FindBaseSetChoice(string source)
        {
            return BaseSetChoices.FirstOrDefault(x => x != null && String.Equals(x.Source, source, StringComparison.OrdinalIgnoreCase));
        }

        internal CdMetadataReviewState CaptureReviewState()
        {
            CdMetadataReviewState state = new CdMetadataReviewState { BaseSource = BaseSource };
            foreach (KeyValuePair<string,string> pair in _overrides) state.Overrides[pair.Key] = pair.Value;
            return state;
        }

        internal void RestoreReviewState(CdMetadataReviewState state)
        {
            if (state == null) return;
            _baseSource = String.IsNullOrWhiteSpace(state.BaseSource) ? "Current" : NormalizeSource(state.BaseSource);
            _overrides.Clear();
            foreach (KeyValuePair<string,string> pair in state.Overrides) _overrides[pair.Key] = pair.Value;
            RebuildRows();
        }

        public string BaseSourceStatus(string source)
        {
            string normalized=NormalizeSource(source);
            if(String.Equals(normalized,"Recommended",StringComparison.OrdinalIgnoreCase))
                return "strongest unambiguous value per field; existing Catalog evidence wins ties and missing fields may be filled from other returned sources";
            if(String.Equals(normalized,"Current",StringComparison.OrdinalIgnoreCase))
                return "metadata state before matching/review";
            string status;
            return _sourceStatus.TryGetValue(normalized, out status) ? status ?? "" : "";
        }

        private string ChooseInitialBaseSource()
        {
            bool catalog=_report.Evidence.Any(x=>x!=null && !String.IsNullOrWhiteSpace(x.Value) && String.Equals(NormalizeSource(x.Source),"Catalog",StringComparison.OrdinalIgnoreCase));
            if(catalog) return "Catalog";
            bool any=_report.Evidence.Any(x=>x!=null && !String.IsNullOrWhiteSpace(x.Value) && IsAllowedBaseSource(NormalizeSource(x.Source)) && !String.Equals(NormalizeSource(x.Source),"Current",StringComparison.OrdinalIgnoreCase));
            return any?"Recommended":"Current";
        }

        private static bool IsAllowedBaseSource(string source)
        {
            return !String.IsNullOrWhiteSpace(source) &&
                !String.Equals(source, "Manual", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(source, "Title Analysis", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(source, "Automatic", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(source, "Do Not Use", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(source, "Unknown", StringComparison.OrdinalIgnoreCase);
        }

        private static int BaseSourcePriority(string source)
        {
            if (String.Equals(source, "Catalog", StringComparison.OrdinalIgnoreCase)) return 100;
            if (String.Equals(source, "Discogs", StringComparison.OrdinalIgnoreCase)) return 90;
            if (String.Equals(source, "MusicBrainz", StringComparison.OrdinalIgnoreCase)) return 85;
            if (String.Equals(source, "CD-TEXT", StringComparison.OrdinalIgnoreCase)) return 80;
            if (String.Equals(source, "foobar", StringComparison.OrdinalIgnoreCase)) return 70;
            if (String.Equals(source, "Current", StringComparison.OrdinalIgnoreCase)) return 10;
            return 20;
        }

        public void SetBaseSource(string source)
        {
            string normalized = NormalizeSource(source);
            if (!IsAllowedBaseSource(normalized) && !String.Equals(normalized, "Current", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A coherent metadata source is required for the base metadata set.", "source");
            _baseSource = normalized;
            _overrides.Clear();
            RebuildRows();
        }

        public void ResetOverrides()
        {
            _overrides.Clear();
            RebuildRows();
        }

        private static string SelectionIdentity(CdMetadataChoiceOption option)
        {
            if (option == null) return "";
            return (option.Source ?? "") + "\u001f" + (option.Value ?? "") + "\u001f" + (option.IsNone ? "1" : "0");
        }

        private CdMetadataChoiceOption RecommendedOption(CdMetadataChoiceRow row)
        {
            if(row==null) return null;
            var candidates=row.Options
                .Where(x=>x!=null && !x.IsAutomatic && !x.IsNone && !String.IsNullOrWhiteSpace(x.Value))
                .Where(x=>!String.Equals(NormalizeSource(x.Source),"Manual",StringComparison.OrdinalIgnoreCase))
                .Where(x=>!String.Equals(NormalizeSource(x.Source),"Current",StringComparison.OrdinalIgnoreCase))
                .GroupBy(x=>NormalizeSource(x.Source),StringComparer.OrdinalIgnoreCase)
                .Select(g=>new { Items=g.ToList(), Distinct=g.Select(x=>NormalizeValue(x.Value)).Where(v=>v.Length>0).Distinct(StringComparer.Ordinal).ToList() })
                .Where(x=>x.Distinct.Count==1)
                .Select(x=>x.Items.OrderByDescending(y=>y.Confidence).First())
                .OrderByDescending(x=>x.Confidence)
                .ThenByDescending(x=>RecommendedSourcePriority(row.Key,NormalizeSource(x.Source)))
                .ToList();
            if(candidates.Count>0) return candidates[0];
            return row.Options.FirstOrDefault(x=>x!=null && !x.IsNone && !String.IsNullOrWhiteSpace(x.Value) && String.Equals(NormalizeSource(x.Source),"Current",StringComparison.OrdinalIgnoreCase));
        }

        private static int RecommendedSourcePriority(string key,string source)
        {
            bool track=!String.IsNullOrWhiteSpace(key) && key.StartsWith("track.",StringComparison.OrdinalIgnoreCase);
            bool version=track && key.EndsWith(".version",StringComparison.OrdinalIgnoreCase);
            if(String.Equals(source,"Catalog",StringComparison.OrdinalIgnoreCase)) return 120;
            if(version && String.Equals(source,"foobar",StringComparison.OrdinalIgnoreCase)) return 112;
            if(version && String.Equals(source,"Discogs",StringComparison.OrdinalIgnoreCase)) return 108;
            if(track && String.Equals(source,"foobar",StringComparison.OrdinalIgnoreCase)) return 105;
            if(String.Equals(source,"MusicBrainz",StringComparison.OrdinalIgnoreCase)) return 100;
            if(String.Equals(source,"Discogs",StringComparison.OrdinalIgnoreCase)) return 95;
            if(String.Equals(source,"CD-TEXT",StringComparison.OrdinalIgnoreCase)) return 90;
            if(version && String.Equals(source,"Title Analysis",StringComparison.OrdinalIgnoreCase)) return 85;
            return 50;
        }

        private void ApplyBaseAndOverrides()
        {
            string baseSource = BaseSource;
            foreach (CdMetadataChoiceRow row in Rows)
            {
                row.Options.RemoveAll(x => x != null && x.IsAutomatic);
                foreach (CdMetadataChoiceOption option in row.Options)
                {
                    option.IsBaseValue = false;
                    option.IsBaseMissing = false;
                }

                CdMetadataChoiceOption baseOption=null;
                List<CdMetadataChoiceOption> baseValues=new List<CdMetadataChoiceOption>();
                if(String.Equals(baseSource,"Recommended",StringComparison.OrdinalIgnoreCase))
                {
                    baseOption=RecommendedOption(row);
                }
                else
                {
                    baseValues = row.Options
                        .Where(x => x != null && !x.IsNone && !String.IsNullOrWhiteSpace(x.Value) && String.Equals(NormalizeSource(x.Source), baseSource, StringComparison.OrdinalIgnoreCase))
                        .GroupBy(x => NormalizeValue(x.Value), StringComparer.Ordinal)
                        .Select(g => g.OrderByDescending(x => x.Confidence).First())
                        .ToList();
                    if(baseValues.Count==1) baseOption=baseValues[0];
                }

                if(baseOption!=null)
                {
                    baseOption.IsBaseValue=true;
                    row.Options.Remove(baseOption);
                    row.Options.Insert(0,baseOption);
                }
                else
                {
                    baseOption = new CdMetadataChoiceOption
                    {
                        Source = baseSource,
                        EffectiveSource = baseSource,
                        Value = "",
                        IsBaseValue = true,
                        IsBaseMissing = true,
                        Detail = baseValues.Count > 1
                            ? "The selected starting source returned multiple different values for this field."
                            : "The selected starting values do not provide this field."
                    };
                    row.Options.Insert(0, baseOption);
                }
                row.SelectedOption = baseOption;

                string identity;
                if (_overrides.TryGetValue(row.Key, out identity))
                {
                    CdMetadataChoiceOption selected = row.Options.FirstOrDefault(x => SelectionIdentity(x) == identity);
                    if (selected != null) row.SelectedOption = selected;
                }
            }
        }

        public IList<CdMetadataChoiceOption> VisibleOptions(CdMetadataChoiceRow row)
        {
            if(row==null) return new List<CdMetadataChoiceOption>().AsReadOnly();
            return row.Options.Where(delegate(CdMetadataChoiceOption option)
            {
                if(option==null || option.IsAutomatic) return false;
                if(option.IsBaseValue) return true;
                return !String.Equals(NormalizeSource(option.Source),"Current",StringComparison.OrdinalIgnoreCase);
            }).ToList().AsReadOnly();
        }

        public int SourceEvidenceCount(string source)
        {
            string normalized=NormalizeSource(source);
            return _report.Evidence.Count(x=>x!=null && !String.IsNullOrWhiteSpace(x.Value) && String.Equals(NormalizeSource(x.Source),normalized,StringComparison.OrdinalIgnoreCase));
        }

        public CdSnapshot Snapshot { get { return _snapshot; } }

        public CdMetadataChoiceRow FindRow(string key)
        {
            return Rows.FirstOrDefault(x => String.Equals(x.Key,key,StringComparison.OrdinalIgnoreCase));
        }

        public string SelectedValue(string key)
        {
            CdMetadataChoiceRow row=FindRow(key);
            return row==null || row.SelectedOption==null ? "" : row.SelectedOption.Value??"";
        }

        public string SelectedSource(string key)
        {
            CdMetadataChoiceRow row=FindRow(key);
            if(row==null || row.SelectedOption==null) return "";
            return row.SelectedOption.IsAutomatic ? row.SelectedOption.EffectiveSource??"" : row.SelectedOption.Source??"";
        }

        public bool HasConflict(string key)
        {
            CdMetadataChoiceRow row=FindRow(key); return row!=null && row.HasConflict;
        }

        public void SelectOption(string key, CdMetadataChoiceOption option)
        {
            CdMetadataChoiceRow row=FindRow(key); if(row==null || option==null || !row.Options.Contains(option)) return;
            row.SelectedOption=option;
            if (option.IsBaseValue) _overrides.Remove(key);
            else _overrides[key]=SelectionIdentity(option);
        }

        public bool IsOverride(string key)
        {
            return !String.IsNullOrWhiteSpace(key) && _overrides.ContainsKey(key);
        }

        public bool IsBaseMissing(string key)
        {
            CdMetadataChoiceRow row=FindRow(key);
            return row!=null && row.Options.Any(x => x!=null && x.IsBaseValue && x.IsBaseMissing);
        }

        public void SetManualValue(string key,string value)
        {
            string field=EvidenceFieldForKey(key);
            if(String.IsNullOrWhiteSpace(field)) return;
            _report.Evidence.RemoveAll(x => String.Equals(x.Source,"Manual",StringComparison.OrdinalIgnoreCase) && String.Equals(x.Field,field,StringComparison.OrdinalIgnoreCase));
            if(!String.IsNullOrWhiteSpace(value)) _report.Add(field,value,"Manual",1.0,"set manually in the shared DJ Library editor","");
            RebuildRows();
            CdMetadataChoiceRow row=FindRow(key);
            if(row==null) return;
            CdMetadataChoiceOption manual=row.Options.FirstOrDefault(x => String.Equals(x.Source,"Manual",StringComparison.OrdinalIgnoreCase) && String.Equals(x.Value,value??"",StringComparison.Ordinal));
            if(manual!=null)
            {
                row.SelectedOption=manual;
                _overrides[key]=SelectionIdentity(manual);
            }
        }

        public CdMetadataBulkApplyResult ApplySourceToRelease(string source)
        {
            return ApplySourceToRows(source, Rows.Where(x => x.Key != null && x.Key.StartsWith("release.", StringComparison.OrdinalIgnoreCase)));
        }

        public CdMetadataBulkApplyResult ApplySourceToTrack(int position, string source)
        {
            string prefix = "track." + position.ToString(CultureInfo.InvariantCulture) + ".";
            return ApplySourceToRows(source, Rows.Where(x => x.Key != null && x.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
        }

        public CdMetadataBulkApplyResult ApplySourceToAllTracks(string source)
        {
            return ApplySourceToRows(source, Rows.Where(x => x.Key != null && x.Key.StartsWith("track.", StringComparison.OrdinalIgnoreCase)));
        }

        public string ConcreteSourceForOption(CdMetadataChoiceOption option)
        {
            if (option == null || option.IsNone || option.IsBaseValue) return "";
            string source = option.IsAutomatic ? option.EffectiveSource : option.Source;
            source = NormalizeSource(source);
            if (String.IsNullOrWhiteSpace(source) ||
                String.Equals(source, "Automatic", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(source, "Current", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(source, "Manual", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(source, "Do Not Use", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(source, "Unknown", StringComparison.OrdinalIgnoreCase)) return "";
            return source;
        }

        private CdMetadataBulkApplyResult ApplySourceToRows(string source, IEnumerable<CdMetadataChoiceRow> rows)
        {
            string normalizedSource = NormalizeSource(source);
            if (String.IsNullOrWhiteSpace(normalizedSource) ||
                String.Equals(normalizedSource, "Automatic", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(normalizedSource, "Current", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(normalizedSource, "Manual", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(normalizedSource, "Do Not Use", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(normalizedSource, "Unknown", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A concrete metadata source is required.", "source");

            CdMetadataBulkApplyResult result = new CdMetadataBulkApplyResult { Source = normalizedSource };
            foreach (CdMetadataChoiceRow row in rows.ToList())
            {
                List<CdMetadataChoiceOption> matches = row.Options
                    .Where(delegate(CdMetadataChoiceOption option)
                    {
                        return option != null && !option.IsAutomatic && !option.IsNone && !String.IsNullOrWhiteSpace(option.Value) &&
                               String.Equals(NormalizeSource(option.Source), normalizedSource, StringComparison.OrdinalIgnoreCase);
                    })
                    .GroupBy(x => NormalizeValue(x.Value), StringComparer.Ordinal)
                    .Select(x => x.OrderByDescending(y => y.Confidence).First())
                    .ToList();

                if (matches.Count == 0)
                {
                    result.Missing++;
                    continue;
                }
                if (matches.Count > 1)
                {
                    result.Ambiguous++;
                    continue;
                }
                row.SelectedOption = matches[0];
                _overrides[row.Key] = SelectionIdentity(matches[0]);
                result.Applied++;
            }
            return result;
        }

        public string ReviewSummary(IEnumerable<string> keys)
        {
            List<string> list=keys==null?new List<string>():keys.Where(x=>!String.IsNullOrWhiteSpace(x)).ToList();
            int changed=list.Count(IsOverride);
            int missing=list.Count(x=>IsBaseMissing(x) && !IsOverride(x));
            int alternatives=list.Count(x=>{ CdMetadataChoiceRow row=FindRow(x); return row!=null && row.HasConflict && !IsOverride(x) && !IsBaseMissing(x); });
            List<string> parts=new List<string>();
            if(changed>0) parts.Add(changed.ToString(CultureInfo.CurrentCulture)+" changed");
            if(missing>0) parts.Add(missing.ToString(CultureInfo.CurrentCulture)+" missing");
            if(alternatives>0) parts.Add(alternatives.ToString(CultureInfo.CurrentCulture)+" alternative"+(alternatives==1?"":"s"));
            return parts.Count==0?"✓ Ready":String.Join(" · ",parts.ToArray());
        }

        public string ReviewFieldSummary(IEnumerable<string> keys)
        {
            List<string> list=keys==null?new List<string>():keys.Where(x=>!String.IsNullOrWhiteSpace(x)).ToList();
            // Compact a single release/track row to field names users recognize.
            // Cross-track summaries retain full labels so each track is identifiable.
            bool oneRelease=list.Count>0 && list.All(x=>x.StartsWith("release.",StringComparison.OrdinalIgnoreCase));
            string trackPrefix="";
            if(list.Count>0 && list[0].StartsWith("track.",StringComparison.OrdinalIgnoreCase))
            {
                int lastDot=list[0].LastIndexOf('.');
                if(lastDot>0) trackPrefix=list[0].Substring(0,lastDot+1);
            }
            bool oneTrack=trackPrefix.Length>0 && list.All(x=>x.StartsWith(trackPrefix,StringComparison.OrdinalIgnoreCase));
            Func<string,string> fieldName=delegate(string key)
            {
                CdMetadataChoiceRow row=FindRow(key);
                string label=row==null?"":row.FieldLabel??"";
                if(oneRelease || oneTrack)
                {
                    int separator=label.IndexOf(" · ",StringComparison.Ordinal);
                    if(separator>=0) return label.Substring(separator+3);
                }
                return label;
            };
            List<string> changed=list.Where(IsOverride).Select(fieldName).Where(x=>x.Length>0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            List<string> missing=list.Where(x=>IsBaseMissing(x) && !IsOverride(x)).Select(fieldName).Where(x=>x.Length>0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            List<string> alternatives=list.Where(x=>
            {
                CdMetadataChoiceRow row=FindRow(x);
                return row!=null && row.HasConflict && !IsOverride(x) && !IsBaseMissing(x);
            }).Select(fieldName).Where(x=>x.Length>0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();

            List<string> parts=new List<string>();
            if(changed.Count>0) parts.Add("Changed: "+String.Join(", ",changed.ToArray()));
            if(missing.Count>0) parts.Add("Missing: "+String.Join(", ",missing.ToArray()));
            if(alternatives.Count>0) parts.Add("Alt: "+String.Join(", ",alternatives.ToArray()));
            return parts.Count==0?"✓ Ready":String.Join(" · ",parts.ToArray());
        }

        public string ScopeSummary(IEnumerable<string> keys)
        {
            return ReviewSummary(keys);
        }

        public string ReviewToolTip(IEnumerable<string> keys)
        {
            List<string> list=keys==null?new List<string>():keys.Where(x=>!String.IsNullOrWhiteSpace(x)).ToList();
            List<string> changed=list.Where(IsOverride).Select(x=>FindRow(x)).Where(x=>x!=null).Select(x=>x.FieldLabel).ToList();
            List<string> missing=list.Where(x=>IsBaseMissing(x) && !IsOverride(x)).Select(x=>FindRow(x)).Where(x=>x!=null).Select(x=>x.FieldLabel).ToList();
            List<string> alternatives=list.Where(x=>{ CdMetadataChoiceRow row=FindRow(x); return row!=null && row.HasConflict && !IsOverride(x); }).Select(x=>FindRow(x).FieldLabel).ToList();
            List<string> lines=new List<string>();
            lines.Add("Starting values: " + (String.Equals(BaseSource,"Recommended",StringComparison.OrdinalIgnoreCase)?"Recommended values":BaseSource));
            if(changed.Count>0) lines.Add("Changed: "+String.Join(", ",changed.ToArray()));
            if(missing.Count>0) lines.Add("Missing: "+String.Join(", ",missing.ToArray()));
            if(alternatives.Count>0) lines.Add("Alternative values available: "+String.Join(", ",alternatives.ToArray()));
            if(lines.Count==1) lines.Add("No unresolved metadata decisions in this scope.");
            return String.Join(Environment.NewLine,lines.ToArray());
        }

        public string OverallReviewSummary()
        {
            return ReviewSummary(Rows.Select(x=>x.Key));
        }

        public string FieldToolTip(string key)
        {
            CdMetadataChoiceRow row=FindRow(key);
            if(row==null) return "";
            List<string> lines=new List<string>();
            lines.Add(FieldExplanation(row));
            List<string> alternatives=VisibleOptions(row)
                .Where(x=>x!=null && !Object.ReferenceEquals(x,row.SelectedOption) && !x.IsNone && !String.IsNullOrWhiteSpace(x.Value))
                .Select(x=>(String.Equals(x.Source,"Current",StringComparison.OrdinalIgnoreCase)?"Existing value":x.Source)+" = "+x.Value)
                .Take(6).ToList();
            if(alternatives.Count>0) lines.Add("Alternatives: "+String.Join("; ",alternatives.ToArray()));
            return String.Join(Environment.NewLine,lines.ToArray());
        }

        public string CandidateExplanation(CdMetadataChoiceRow row,CdMetadataChoiceOption option)
        {
            if(row==null || option==null) return "Select a value/source candidate.";
            if(Object.ReferenceEquals(option,row.SelectedOption)) return FieldExplanation(row);
            if(option.IsNone) return "Preview: clear this field. Click Use Selected Value to apply only this field.";
            string source=String.Equals(option.Source,"Current",StringComparison.OrdinalIgnoreCase)?"Existing value":option.Source;
            string text="Preview: " + source + " would set this field to “" + (option.Value??"") + "”. Click Use Selected Value for this field, or Use Source For… for a wider scope.";
            if(!String.IsNullOrWhiteSpace(option.Detail)) text += " Evidence: " + option.Detail;
            return text;
        }

        public string FieldExplanation(CdMetadataChoiceRow row)
        {
            if (row == null || row.SelectedOption == null) return "Select a metadata field to compare its current result with returned alternatives.";
            CdMetadataChoiceOption selected=row.SelectedOption;
            string source=String.Equals(selected.Source,"Current",StringComparison.OrdinalIgnoreCase)?"existing pre-match value":selected.Source;
            string text;
            if(IsOverride(row.Key))
            {
                if(selected.IsNone) text="Current result: intentionally empty (field change).";
                else text="Current result: " + source + " changed this field from the selected starting values.";
            }
            else if(selected.IsBaseMissing)
                text="Current result: missing. The selected starting values do not provide one unambiguous value for this field.";
            else if(String.Equals(BaseSource,"Recommended",StringComparison.OrdinalIgnoreCase))
                text="Current result: " + source + " was chosen by Recommended values for this field.";
            else
                text="Current result: " + source + " from the selected starting values.";
            if(row.HasConflict) text += " Other returned sources contain alternative values.";
            return text;
        }

        private static string EvidenceFieldForKey(string key)
        {
            if(String.IsNullOrWhiteSpace(key)) return "";
            if(key=="release.album") return "Album";
            if(key=="release.albumartist") return "Album Artist";
            if(key=="release.date") return "Date";
            if(key=="release.genre") return "Genre/Style";
            if(key=="release.label") return "Label";
            if(key=="release.catalog") return "Catalog Number";
            if(key=="release.country") return "Country";
            string[] p=key.Split('.');
            if(p.Length!=3 || p[0]!="track") return "";
            string suffix=p[2]=="title"?"Title":p[2]=="version"?"Mix/Version":p[2]=="artist"?"Artist":p[2]=="genre"?"Genre":"";
            return suffix.Length==0?"":"Track "+p[1]+" "+suffix;
        }

        internal static string ValidateBulkSourceContract()
        {
            CdSnapshot snapshot = new CdSnapshot
            {
                Album = "Current Album", AlbumArtist = "Current Artist", Country = "Current Country"
            };
            snapshot.Tracks.Add(new CdTrackCapture { Position = 1, Artist = "Current T1 Artist", Title = "Current T1 Title", Genre = "Current T1 Genre" });
            snapshot.Tracks.Add(new CdTrackCapture { Position = 2, Artist = "Current T2 Artist", Title = "Current T2 Title", Genre = "Current T2 Genre" });

            CdMetadataReport report = new CdMetadataReport();
            report.Add("Album", "Catalog Album", "Catalog", 1.00, "catalog", "");
            report.Add("Album Artist", "Catalog Artist", "Catalog", 1.00, "catalog", "");
            report.Add("Country", "Catalog Country", "Catalog", 1.00, "catalog", "");
            report.Add("Track 1 Title", "Catalog T1 Title", "Catalog", 1.00, "catalog", "");
            report.Add("Track 1 Artist", "Catalog T1 Artist", "Catalog", 1.00, "catalog", "");
            report.Add("Track 2 Title", "Catalog T2 Title", "Catalog", 1.00, "catalog", "");
            report.Add("Album", "Discogs Album", "Discogs", 0.95, "release", "");
            report.Add("Album Artist", "Discogs Artist", "Discogs", 0.95, "release", "");
            report.Add("Label", "Discogs Label A", "Discogs", 0.95, "release", "");
            report.Add("Label", "Discogs Label B", "Discogs", 0.94, "release ambiguity", "");
            report.Add("Track 1 Title", "Discogs T1 Title", "Discogs", 0.95, "track", "");
            report.Add("Track 1 Artist", "Discogs T1 Artist", "Discogs", 0.95, "track", "");
            report.Add("Track 2 Title", "Discogs T2 Title", "Discogs", 0.95, "track", "");
            report.Add("Track 2 Genre", "House", "Discogs", 0.95, "track", "");

            CdMetadataSelectionSession session = new CdMetadataSelectionSession(snapshot, report);
            session.SetBaseSource("Catalog");
            session.SetManualValue("release.country", "Manual Country");
            session.SetManualValue("track.1.genre", "Manual Genre");

            CdMetadataBulkApplyResult release = session.ApplySourceToRelease("Discogs");
            if (release.Applied != 2 || release.Ambiguous != 1 || release.Missing < 1)
                throw new InvalidDataException("Release scoped override counts are incorrect.");
            if (!session.IsOverride("release.album") || session.SelectedSource("release.album") != "Discogs" || session.SelectedValue("release.album") != "Discogs Album")
                throw new InvalidDataException("Release scoped override did not choose the Discogs album.");
            if (session.SelectedSource("release.country") != "Manual" || session.SelectedValue("release.country") != "Manual Country")
                throw new InvalidDataException("Release scoped override overwrote a field without a Discogs value.");
            if (session.SelectedSource("release.label") == "Discogs")
                throw new InvalidDataException("Release scoped override chose an ambiguous Discogs field.");

            CdMetadataBulkApplyResult track1 = session.ApplySourceToTrack(1, "Discogs");
            if (track1.Applied != 2 || session.SelectedSource("track.1.title") != "Discogs" || session.SelectedSource("track.1.artist") != "Discogs")
                throw new InvalidDataException("Current-track scoped override failed.");
            if (session.SelectedSource("track.1.genre") != "Manual")
                throw new InvalidDataException("Current-track scoped override overwrote a missing-source manual value.");

            CdMetadataBulkApplyResult allTracks = session.ApplySourceToAllTracks("Discogs");
            if (allTracks.Applied < 4 || session.SelectedSource("track.2.title") != "Discogs" || session.SelectedSource("track.2.genre") != "Discogs")
                throw new InvalidDataException("All-tracks scoped override failed.");

            int before = session.OverrideCount;
            session.SetBaseSource("Discogs");
            if (session.OverrideCount != 0 || before == 0)
                throw new InvalidDataException("Changing the base metadata set did not reset previous overrides.");
            if (!session.IsBaseMissing("release.label"))
                throw new InvalidDataException("Ambiguous base-source field did not fail closed as not provided/ambiguous.");

            CdMetadataReviewState saved = session.CaptureReviewState();
            CdMetadataChoiceOption catalogOverride = session.FindRow("release.album").Options.FirstOrDefault(x => String.Equals(x.Source, "Catalog", StringComparison.OrdinalIgnoreCase));
            session.SelectOption("release.album", catalogOverride);
            if (!session.IsOverride("release.album")) throw new InvalidDataException("Review-state fixture did not create an override.");
            session.RestoreReviewState(saved);
            if (session.IsOverride("release.album") || !String.Equals(session.BaseSource, "Discogs", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Metadata Review Cancel-state restore did not restore base/overrides exactly.");

            string detailed=session.ReviewFieldSummary(new string[] { "release.label", "release.country" });
            if(detailed.IndexOf("Missing: Label",StringComparison.OrdinalIgnoreCase)<0)
                throw new InvalidDataException("Detailed Review summary does not expose the concrete missing field.");
            if(detailed.IndexOf("Country",StringComparison.OrdinalIgnoreCase)<0)
                throw new InvalidDataException("Detailed Review summary does not expose the concrete reviewed field names.");

            return "base metadata set + release/current-track/all-tracks scoped overrides + missing preservation for overrides + ambiguity fail-closed + base-switch reset + transactional review state + field-named Review summary";
        }

    }
}
