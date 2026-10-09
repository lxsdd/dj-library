using System;
using System.Collections.Generic;

namespace DJLibrary
{
    public sealed class FieldDefinition
    {
        public string Key { get; set; }
        public string Header { get; set; }
        public string BindingPath { get; set; }
        public double Width { get; set; }
        public bool ReadOnly { get; set; }

        public FieldDefinition(string key, string header, string bindingPath, double width, bool readOnly)
        {
            Key = key;
            Header = header;
            BindingPath = bindingPath;
            Width = width;
            ReadOnly = readOnly;
        }
    }

    public static class FieldSchema
    {
        private static readonly List<FieldDefinition> _release = new List<FieldDefinition>
        {
            new FieldDefinition("AlbumArtist", "Album Artist", "AlbumArtist", 170, false),
            new FieldDefinition("Album", "Album", "Album", 210, false),
            new FieldDefinition("ReleaseDate", "Date/Year", "ReleaseDate", 85, false),
            new FieldDefinition("Genre", "Genre/Style", "Genre", 130, false),
            new FieldDefinition("Label", "Label", "Label", 150, false),
            new FieldDefinition("Catalog", "Catalog Number", "Catalog", 120, false),
            new FieldDefinition("Country", "Country", "Country", 90, false),
            new FieldDefinition("TotalDiscs", "Discs", "TotalDiscs", 60, false)
        };

        private static readonly List<FieldDefinition> _track = new List<FieldDefinition>
        {
            new FieldDefinition("Position", "#", "Position", 45, true),
            new FieldDefinition("Artist", "Artist", "Artist", 210, false),
            new FieldDefinition("Title", "Title", "Title", 270, false),
            new FieldDefinition("Version", "Mix/Version", "Version", 220, false),
            new FieldDefinition("Genre", "Genre", "Genre", 150, false),
            new FieldDefinition("Bpm", "BPM", "Bpm", 70, false),
            new FieldDefinition("DurationText", "Duration", "DurationText", 85, false)
        };

        public static IList<FieldDefinition> ReleaseFields { get { return _release.AsReadOnly(); } }
        public static IList<FieldDefinition> TrackFields { get { return _track.AsReadOnly(); } }

        public static FieldDefinition FindTrack(string key)
        {
            return _track.Find(delegate(FieldDefinition x) { return String.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase); });
        }

        public static FieldDefinition FindRelease(string key)
        {
            return _release.Find(delegate(FieldDefinition x) { return String.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase); });
        }

        internal static string ValidateContract()
        {
            if (_track.Count < 5 || _track[1].Key != "Artist" || _track[2].Key != "Title" || _track[3].Key != "Version")
                throw new InvalidOperationException("FieldSchema: Track-Standardreihenfolge muss #, Artist, Title, Mix/Version sein.");
            if (FindRelease("Catalog") == null || FindTrack("Genre") == null)
                throw new InvalidOperationException("FieldSchema: Kernfelder fehlen.");
            return "shared field schema: Artist -> Title -> Mix/Version + release/catalog labels";
        }
    }
}
