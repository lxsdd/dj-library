using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DJLibrary
{
    public sealed class CdMetadataChoiceOption
    {
        public string Source { get; set; }
        public string Value { get; set; }
        public string Detail { get; set; }
        public double Confidence { get; set; }
        public bool IsAutomatic { get; set; }
        public bool IsNone { get; set; }
        public bool IsBaseValue { get; set; }
        public bool IsBaseMissing { get; set; }
        public string EffectiveSource { get; set; }

        public CdMetadataChoiceOption()
        {
            Source = Value = Detail = EffectiveSource = "";
        }

        public string DisplayText
        {
            get
            {
                string source=String.Equals(Source,"Current",StringComparison.OrdinalIgnoreCase)?"Existing value":(Source??"");
                string value=String.IsNullOrWhiteSpace(Value)?"—":Value;
                if (IsBaseValue)
                {
                    if (IsBaseMissing) return "Starting value · " + source + " — not provided";
                    return "Starting value · " + source + " — " + value;
                }
                if (IsNone) return "Clear this field";
                if (IsAutomatic)
                {
                    if (String.IsNullOrWhiteSpace(Value)) return "Recommended — no unambiguous value";
                    return "Recommended — " + value + " [" + EffectiveSource + "]";
                }
                return source + " — " + value;
            }

        }
    }

    public sealed class CdMetadataChoiceRow
    {
        public string Key { get; set; }
        public string FieldLabel { get; set; }
        public string CurrentValue { get; set; }
        public bool HasConflict { get; set; }
        public List<CdMetadataChoiceOption> Options { get; set; }
        public CdMetadataChoiceOption SelectedOption { get; set; }

        public CdMetadataChoiceRow()
        {
            Key = FieldLabel = CurrentValue = "";
            Options = new List<CdMetadataChoiceOption>();
        }

        public string ConflictText { get { return HasConflict ? "⚠" : ""; } }
    }

    public sealed class CdMetadataSourceToggle
    {
        public string Source { get; set; }
        public string Status { get; set; }
        public bool Enabled { get; set; }

        public CdMetadataSourceToggle()
        {
            Source = Status = "";
            Enabled = true;
        }
    }
}
