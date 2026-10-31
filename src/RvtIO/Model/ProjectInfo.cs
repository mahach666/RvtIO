namespace RvtIO.Model
{
    /// <summary>The project information of the Revit file.</summary>
    public class ProjectInfo
    {
        /// <summary>The project name.</summary>
        public string? Name { get; set; }
        /// <summary>The project number.</summary>
        public string? Number { get; set; }
        /// <summary>The client name.</summary>
        public string? Client { get; set; }
        /// <summary>The project address.</summary>
        public string? Address { get; set; }
        /// <summary>The project status.</summary>
        public string? Status { get; set; }
        /// <summary>The project issue date.</summary>
        public string? IssueDate { get; set; }
        /// <summary>The title of the project.</summary>
        public string? Title { get; set; }
        /// <summary>The Revit release of the project information.</summary>
        public string? RevitVersion { get; set; }
        /// <summary>The author.</summary>
        public string? Author { get; set; }
        /// <summary>The building name.</summary>
        public string? BuildingName { get; set; }
        /// <summary>The organization name.</summary>
        public string? OrganizationName { get; set; }
    }
}
