using System;
using System.Collections.Generic;

namespace GabrielAutoclick.Models
{
    public class Profile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Novo Perfil";
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ClickSettings Settings { get; set; } = new ClickSettings();
        public List<ClickPoint> MultiPoints { get; set; } = new List<ClickPoint>();
        public List<MacroAction> MacroSequence { get; set; } = new List<MacroAction>();

        public Profile Clone(string newName)
        {
            var p = new Profile
            {
                Id = Guid.NewGuid().ToString(),
                Name = newName,
                Description = this.Description,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Settings = this.Settings.Clone(),
                MultiPoints = new List<ClickPoint>(),
                MacroSequence = new List<MacroAction>(this.MacroSequence)
            };
            foreach (var pt in this.MultiPoints)
            {
                p.MultiPoints.Add(pt.Clone());
            }
            return p;
        }
    }
}
