namespace RvtIO.Model
{
    /// <summary>Identifies a parameter by its built-in name, id and display name.</summary>
    public class Definition
    {
        /// <summary>BuiltInParameter enum name (e.g. "WALL_USER_HEIGHT_PARAM"), null for user-defined params.</summary>
        public string? BuiltInParameter { get; set; }

        /// <summary>
        /// Negative int32 for built-in parameters (Revit BIP enum value).
        /// Positive int32 for user-defined params (element ID of the param definition element).
        /// </summary>
        public int Id { get; set; }

        /// <summary>Display name. For BIPs same as BuiltInParameter; for user params the shared/family param name.</summary>
        public string? Name { get; set; }
    }
}
