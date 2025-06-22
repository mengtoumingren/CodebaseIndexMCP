using System.Collections.Generic;

namespace CodebaseMcpServer.Models.Domain
{
    /// <summary>
    /// Represents the request to update the presets associated with an index library.
    /// </summary>
    public class UpdateLibraryPresetsRequest
    {
        /// <summary>
        /// Gets or sets the list of preset IDs to associate with the library.
        /// </summary>
        public List<string> PresetIds { get; set; } = new();
    }
}