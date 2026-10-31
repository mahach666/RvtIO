namespace RvtIO.Model
{
    /// <summary>Facts stored in the basic file information of the Revit file.</summary>
    public class FileMetadata
    {
        /// <summary>The stream version stored in the file.</summary>
        public string? StreamVersion { get; set; }
        /// <summary>The Revit release that saved the file.</summary>
        public string? RevitVersion { get; set; }
        /// <summary>The build identifier of the saving Revit.</summary>
        public string? Build { get; set; }
        /// <summary>The path the file was last saved under.</summary>
        public string? LastSavePath { get; set; }
        /// <summary>BasicFileInfo "Locale when saved" (for example RUS or ENU).</summary>
        public string? SavedLocale { get; set; }
        /// <summary>The preview image stored in the file.</summary>
        public string? PreviewImage { get; set; }
        /// <summary>The media type of the preview image.</summary>
        public string? PreviewImageMimeType { get; set; }
    }
}
